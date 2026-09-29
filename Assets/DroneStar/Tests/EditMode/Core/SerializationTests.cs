using System;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class SerializationTests
    {
        [Test]
        public void JsonRoundTripsAllValueTypes()
        {
            JsonValue v = JsonValue.NewObject()
                .Set("s", "quote \" backslash \\ newline \n tab \t unicode ✓ Hà Nội")
                .Set("n", -12.5)
                .Set("i", 42)
                .Set("f", 0.1f)
                .Set("b", true)
                .Set("z", JsonValue.Null)
                .Set("a", JsonValue.NewArray().Add(JsonValue.From(1.0)).Add(JsonValue.NewObject()));
            foreach (bool pretty in new[] { true, false })
            {
                JsonValue back = JsonValue.Parse(v.ToJson(pretty));
                Assert.That(back.GetString("s", null), Is.EqualTo(v.GetString("s", null)));
                Assert.That(back.GetNumber("n", 0), Is.EqualTo(-12.5));
                Assert.That(back.GetInt("i", 0), Is.EqualTo(42));
                Assert.That(back.GetFloat("f", 0f), Is.EqualTo(0.1f));
                Assert.That(back.GetBool("b", false), Is.True);
                Assert.That(back.Get("z").Type, Is.EqualTo(JsonType.Null));
                Assert.That(back.Get("a").Items.Count, Is.EqualTo(2));
            }
            Assert.That(v.ToJson(false), Does.Contain("\"f\":0.1,"));
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,2,]")]
        [TestCase("{\"a\":1}x")]
        [TestCase("\"unterminated")]
        [TestCase("01")]
        [TestCase("1.")]
        [TestCase("-")]
        [TestCase("tru")]
        [TestCase("\"bad \\q escape\"")]
        [TestCase("1e999")]
        public void MalformedJsonThrowsFormatException(string text)
        {
            Assert.Throws<FormatException>(() => JsonValue.Parse(text));
        }

        [Test]
        public void DeepNestingIsRejected()
        {
            string deep = new string('[', 100) + new string(']', 100);
            Assert.Throws<FormatException>(() => JsonValue.Parse(deep));
            Assert.DoesNotThrow(() => JsonValue.Parse(new string('[', 30) + new string(']', 30)));
        }

        [Test]
        public void ParsesEscapesAndByteOrderMark()
        {
            JsonValue v = JsonValue.Parse("﻿{\"k\":\"\\u0041\\n\\/\"}");
            Assert.That(v.GetString("k", null), Is.EqualTo("A\n/"));
        }

        [Test]
        public void DemoShowRoundTrips()
        {
            ShowDocument doc = DemoShows.StarGroupNight();
            string json = ShowSerializer.ToJson(doc);
            ShowDocument back = ShowSerializer.FromJson(json);
            Assert.That(ShowSerializer.ToJson(back), Is.EqualTo(json));
            Assert.That(back.Cues.Count, Is.EqualTo(doc.Cues.Count));
            Assert.That(back.Cues[4].Formation.PitchDegrees, Is.EqualTo(doc.Cues[4].Formation.PitchDegrees));
            Assert.That(back.Cues[0].Light.ColorA, Is.EqualTo(LedColor.FromHex(doc.Cues[0].Light.ColorA.ToHex())));
        }

        [Test]
        public void VenueRoundTripsAndDefaultsToTheLake()
        {
            ShowDocument doc = DemoShows.Blank();
            Assert.That(doc.Venue, Is.EqualTo(ShowVenue.Lake));
            doc.Venue = ShowVenue.Festival;
            Assert.That(ShowSerializer.FromJson(ShowSerializer.ToJson(doc)).Venue, Is.EqualTo(ShowVenue.Festival));
            string old = "{\"format\":\"dronestar-show\",\"version\":1,\"cues\":[]}";
            Assert.That(ShowSerializer.FromJson(old).Venue, Is.EqualTo(ShowVenue.Lake), "files from before venues open on the lake");
            string odd = "{\"format\":\"dronestar-show\",\"version\":1,\"venue\":\"Moon\",\"cues\":[]}";
            Assert.That(ShowSerializer.FromJson(odd).Venue, Is.EqualTo(ShowVenue.Lake));
            var flap = new Cue { Motion = new MotionSpec { Kind = MotionKind.Flap, Amount = 500f } };
            ShowSanitizer.SanitizeCue(flap);
            Assert.That(flap.Motion.Amount, Is.EqualTo(ShowBounds.MaxFlapDegrees));
        }

        [Test]
        public void CustomPointsRoundTrip()
        {
            ShowDocument doc = DemoShows.Blank();
            doc.Cues[0].Formation.Kind = FormationKind.Custom;
            doc.Cues[0].Formation.CustomPoints.Add(new Vector3(0.25f, -0.5f, 1f));
            ShowDocument back = ShowSerializer.FromJson(ShowSerializer.ToJson(doc));
            Assert.That(back.Cues[0].Formation.CustomPoints, Is.EqualTo(doc.Cues[0].Formation.CustomPoints));
        }

        [Test]
        public void MissingFieldsTakeDefaultsAndBadValuesAreClamped()
        {
            string json = "{\"format\":\"dronestar-show\",\"version\":1,\"droneCount\":99999,\"limits\":{\"maxSpeed\":-4}," +
                          "\"cues\":[{\"formation\":{\"kind\":\"NotAShape\",\"size\":1e30,\"layers\":\"x\",\"center\":[1,2]}," +
                          "\"light\":{\"effect\":\"7\",\"colorA\":\"#GGGGGG\"},\"motion\":{\"kind\":\"Wave\",\"amount\":1000}}, 5, null]}";
            ShowDocument doc = ShowSerializer.FromJson(json);
            Assert.That(doc.DroneCount, Is.EqualTo(ShowBounds.MaxDrones));
            Assert.That(doc.Limits.MaxSpeed, Is.EqualTo(1f));
            Assert.That(doc.Cues.Count, Is.EqualTo(1));
            Cue c = doc.Cues[0];
            Assert.That(c.Formation.Kind, Is.EqualTo(FormationKind.Sphere));
            Assert.That(c.Formation.Size, Is.EqualTo(ShowBounds.MaxSize));
            Assert.That(c.Formation.Layers, Is.EqualTo(1));
            Assert.That(c.Formation.Center, Is.EqualTo(new Vector3(0, 60, 0)));
            Assert.That(c.Light.Effect, Is.EqualTo(LightEffect.Solid));
            Assert.That(c.Motion.Amount, Is.EqualTo(12f), "wave ripples are capped at 12 m");
        }

        [TestCase("[]")]
        [TestCase("{\"format\":\"other\",\"version\":1}")]
        [TestCase("{\"format\":\"dronestar-show\",\"version\":2}")]
        [TestCase("{\"format\":\"dronestar-show\"}")]
        public void ForeignOrFutureFilesAreRejected(string json)
        {
            Assert.Throws<FormatException>(() => ShowSerializer.FromJson(json));
        }

        [Test]
        public void SanitizerRepairsNonFiniteInput()
        {
            var doc = new ShowDocument { DroneCount = -3, Title = "  \u0001  ", Limits = null, Pad = null };
            doc.Cues.Add(null);
            doc.Cues.Add(new Cue
            {
                Formation = new FormationSpec { Size = float.NaN, Center = new Vector3(float.PositiveInfinity, 0, 0), Text = null },
                Light = new LightSpec { ColorA = new LedColor(float.NaN, 2f, -1f), Speed = float.NegativeInfinity },
                Motion = null,
                HoldSeconds = float.NaN,
            });
            ShowSanitizer.Sanitize(doc);
            Assert.That(doc.DroneCount, Is.EqualTo(1));
            Assert.That(doc.Title, Is.EqualTo("Untitled Show"));
            Assert.That(doc.Limits, Is.Not.Null);
            Assert.That(doc.Pad, Is.Not.Null);
            Assert.That(doc.Cues.Count, Is.EqualTo(1));
            Cue c = doc.Cues[0];
            Assert.That(c.Formation.Size, Is.EqualTo(40f));
            Assert.That(c.Formation.Center, Is.EqualTo(new Vector3(0, 60, 0)));
            Assert.That(c.Formation.Text, Is.EqualTo("MAI"));
            Assert.That(c.Light.ColorA, Is.EqualTo(new LedColor(0f, 1f, 0f)));
            Assert.That(c.Light.Speed, Is.EqualTo(1f));
            Assert.That(c.Motion, Is.Not.Null);
            Assert.That(c.HoldSeconds, Is.EqualTo(8f));
        }

        [Test]
        public void CsvAndReportExport()
        {
            var doc = new ShowDocument { DroneCount = 5 };
            doc.Cues.Add(new Cue { Name = "A|B", HoldSeconds = 1f, Formation = new FormationSpec { Kind = FormationKind.Ring, Size = 20f } });
            CompiledShow show = new ShowCompiler().Compile(doc);
            string csv = ShowExporter.ToCsv(show, 2f);
            string[] lines = csv.TrimEnd('\n').Split('\n');
            Assert.That(lines[0], Is.EqualTo("time_s,drone,x_m,y_m,z_m,red,green,blue"));
            int samples = (int)Math.Floor(show.Duration * 2f) + 1;
            Assert.That(lines.Length, Is.EqualTo(1 + samples * 5));
            Assert.That(lines[1].Split(',').Length, Is.EqualTo(8));

            string report = ShowExporter.ToFlightReport(show, SafetyValidator.Run(show));
            Assert.That(report, Does.Contain("PASSED"));
            Assert.That(report, Does.Contain("A\\|B"));
        }
    }
}
