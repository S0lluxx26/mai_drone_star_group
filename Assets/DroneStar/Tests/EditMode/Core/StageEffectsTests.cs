using System;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class StageEffectsTests
    {
        /// <summary>A small festival show: one sphere of 64 drones at <paramref name="center"/>.</summary>
        static ShowDocument Festival(Vector3 center, FlameMode flames = FlameMode.Off, bool steam = false)
        {
            var doc = new ShowDocument { Title = "Stage test", DroneCount = 64, Venue = ShowVenue.Festival };
            doc.Limits.GeofenceRadius = 300f;
            doc.Cues.Add(new Cue
            {
                Name = "Sphere",
                HoldSeconds = 6f,
                Formation = new FormationSpec { Kind = FormationKind.Sphere, Center = center, Size = 20f },
                Effects = new StageEffects { Flames = flames, Steam = steam },
            });
            ShowSanitizer.Sanitize(doc);
            return doc;
        }

        static readonly Vector3 OverThePad = new Vector3(0f, 60f, 0f);
        static readonly Vector3 OverTheStage = FestivalStage.Center + new Vector3(0f, 25f, 0f);

        [Test]
        public void EffectsRoundTripAndOlderFilesKeepTheDefaults()
        {
            ShowDocument doc = Festival(OverThePad, FlameMode.Salvo, steam: true);
            doc.Cues[0].Effects.Lasers = LaserMode.Tunnel;
            doc.Cues[0].Effects.Fountains = FountainMode.Arch;
            StageEffects back = ShowSerializer.FromJson(ShowSerializer.ToJson(doc)).Cues[0].Effects;
            Assert.That(back.Lasers, Is.EqualTo(LaserMode.Tunnel));
            Assert.That(back.Fountains, Is.EqualTo(FountainMode.Arch));
            Assert.That(back.Flames, Is.EqualTo(FlameMode.Salvo));
            Assert.That(back.Steam, Is.True);

            // A file from before stage effects existed, and one with values this version does not know.
            string json = ShowSerializer.ToJson(doc);
            int start = json.IndexOf("\"effects\"", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThan(0));
            string old = ShowSerializer.ToJson(new ShowDocument { Cues = { new Cue() } });
            old = old.Replace("\"effects\"", "\"unused\"");
            StageEffects defaults = ShowSerializer.FromJson(old).Cues[0].Effects;
            Assert.That(defaults.Lasers, Is.EqualTo(LaserMode.Auto));
            Assert.That(defaults.Fountains, Is.EqualTo(FountainMode.Auto));
            Assert.That(defaults.Flames, Is.EqualTo(FlameMode.Off), "flames are never on unless a show asks for them");
            Assert.That(defaults.Steam, Is.False);
            string odd = json.Replace("\"Salvo\"", "\"Inferno\"").Replace("\"Tunnel\"", "\"7\"");
            StageEffects repaired = ShowSerializer.FromJson(odd).Cues[0].Effects;
            Assert.That(repaired.Flames, Is.EqualTo(FlameMode.Off));
            Assert.That(repaired.Lasers, Is.EqualTo(LaserMode.Auto));
        }

        [Test]
        public void SanitizerAndCloneLookAfterEffects()
        {
            var cue = new Cue { Effects = null };
            ShowSanitizer.SanitizeCue(cue);
            Assert.That(cue.Effects, Is.Not.Null);
            cue.Effects = new StageEffects { Lasers = (LaserMode)42, Fountains = (FountainMode)(-1), Flames = (FlameMode)9 };
            ShowSanitizer.SanitizeCue(cue);
            Assert.That(cue.Effects.Lasers, Is.EqualTo(LaserMode.Auto));
            Assert.That(cue.Effects.Fountains, Is.EqualTo(FountainMode.Auto));
            Assert.That(cue.Effects.Flames, Is.EqualTo(FlameMode.Off));

            cue.Effects.Flames = FlameMode.Beat;
            Cue copy = cue.Clone();
            copy.Effects.Flames = FlameMode.Off;
            Assert.That(cue.Effects.Flames, Is.EqualTo(FlameMode.Beat), "a duplicated cue owns its own effects");
        }

        [Test]
        public void FlamesFireOnlyWhileTheirSceneHolds()
        {
            ShowDocument doc = Festival(OverThePad, FlameMode.Beat);
            CompiledShow show = new ShowCompiler().Compile(doc);
            CueTiming c = show.CueTimings[0];
            int jets = FestivalStage.FlameNozzles.Count;
            bool fired = false;
            for (float t = 0f; t <= show.Duration; t += 0.02f)
            {
                for (int j = 0; j < jets; j++)
                {
                    float level = FestivalStage.FlameLevel(show, j, t);
                    Assert.That(level, Is.InRange(0f, 1f));
                    if (level <= 0f) continue;
                    fired = true;
                    Assert.That(t, Is.InRange(c.HoldStart, c.HoldEnd + FestivalStage.FlameArmTail), "flames only during the hold and its dying tail");
                    Assert.That(FestivalStage.ArmedFlameCue(show, t), Is.EqualTo(0), "a flame never burns outside its armed window");
                }
            }
            Assert.That(fired, Is.True);
            Assert.That(FestivalStage.ArmedFlameCue(show, c.TransitStart), Is.EqualTo(-1), "not armed while the drones fly in");

            doc.Venue = ShowVenue.Lake;
            CompiledShow lake = new ShowCompiler().Compile(doc);
            Assert.That(FestivalStage.ArmedFlameCue(lake, lake.CueTimings[0].HoldStart + 1f), Is.EqualTo(-1), "the lake has no stage");
            Assert.That(FestivalStage.FlameLevel(lake, 0, lake.CueTimings[0].HoldStart + 0.1f), Is.EqualTo(0f));
        }

        [Test]
        public void BeatKeepsTheDrumRhythmAndSalvoOpensTheScene()
        {
            int front = 2 * FestivalStage.FlamesPerBoat, left = 0, right = FestivalStage.FlamesPerBoat;
            const float hold = 10f;
            // The one: the front row and the left boat (on even bars); the three: the right boat.
            Assert.That(FestivalStage.Burst(FlameMode.Beat, front, 0.2f, hold), Is.GreaterThan(0.9f));
            Assert.That(FestivalStage.Burst(FlameMode.Beat, left, 0.2f, hold), Is.GreaterThan(0.9f));
            Assert.That(FestivalStage.Burst(FlameMode.Beat, right, 0.2f, hold), Is.EqualTo(0f));
            Assert.That(FestivalStage.Burst(FlameMode.Beat, right, 3f * FestivalStage.Beat + 0.2f, hold), Is.GreaterThan(0.8f));
            // The next bar swaps the boats.
            Assert.That(FestivalStage.Burst(FlameMode.Beat, right, FestivalStage.Bar + 0.2f, hold), Is.GreaterThan(0.9f));
            Assert.That(FestivalStage.Burst(FlameMode.Beat, left, FestivalStage.Bar + 0.2f, hold), Is.EqualTo(0f));
            // Between hits the flames are out.
            Assert.That(FestivalStage.Burst(FlameMode.Beat, front, 1.2f, hold), Is.EqualTo(0f));
            // Every projector opens a salvo together and keeps burning for a long moment.
            for (int j = 0; j < FestivalStage.FlameNozzles.Count; j++)
            {
                Assert.That(FestivalStage.Burst(FlameMode.Salvo, j, 0.8f, hold), Is.GreaterThan(0.9f), "jet " + j);
            }
            // No burst starts after the hold: in a one-second hold the three never comes.
            Assert.That(FestivalStage.Burst(FlameMode.Beat, right, 3f * FestivalStage.Beat + 0.1f, 1f), Is.EqualTo(0f));
            Assert.That(FestivalStage.Burst(FlameMode.Off, front, 0.2f, hold), Is.EqualTo(0f));
        }

        [Test]
        public void SteamFadesInWithTheFlightAndOutAfterTheHold()
        {
            CompiledShow show = new ShowCompiler().Compile(Festival(OverThePad, steam: true));
            CueTiming c = show.CueTimings[0];
            Assert.That(FestivalStage.SteamLevel(show, c.TransitStart - 0.1f), Is.EqualTo(0f));
            Assert.That(FestivalStage.SteamLevel(show, c.TransitStart + 0.5f * FestivalStage.SteamFadeIn), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(FestivalStage.SteamLevel(show, 0.5f * (c.HoldStart + c.HoldEnd)), Is.EqualTo(1f));
            Assert.That(FestivalStage.SteamLevel(show, c.HoldEnd + 0.5f * FestivalStage.SteamFadeOut), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(FestivalStage.SteamLevel(show, c.HoldEnd + FestivalStage.SteamFadeOut + 0.1f), Is.EqualTo(0f));
            CompiledShow dry = new ShowCompiler().Compile(Festival(OverThePad));
            Assert.That(FestivalStage.SteamLevel(dry, 0.5f * (c.HoldStart + c.HoldEnd)), Is.EqualTo(0f));
        }

        [Test]
        public void DronesOverTheFlamesFailTheSafetyCheck()
        {
            ValidationReport hot = SafetyValidator.Run(new ShowCompiler().Compile(Festival(OverTheStage, FlameMode.Beat)));
            Assert.That(hot.FlamesArmed, Is.True);
            ValidationIssue issue = hot.Issues.Find(i => i.Kind == IssueKind.FlameZone);
            Assert.That(issue, Is.Not.Null, "a sphere 25 m over the stage is inside the flames' safety zone");
            Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Error));
            Assert.That(issue.CueIndex, Is.EqualTo(0));
            Assert.That(issue.Message, Does.Contain("stage flames"));
            Assert.That(hot.MinFlameClearance, Is.LessThan(FestivalStage.FlameSafetyDistance));
            Assert.That(hot.Passed, Is.False);

            // The same flight with the flames off, or at the lake, is fine.
            ValidationReport cold = SafetyValidator.Run(new ShowCompiler().Compile(Festival(OverTheStage)));
            Assert.That(cold.FlamesArmed, Is.False);
            Assert.That(cold.Issues.Exists(i => i.Kind == IssueKind.FlameZone), Is.False);
            ShowDocument lake = Festival(OverTheStage, FlameMode.Beat);
            lake.Venue = ShowVenue.Lake;
            Assert.That(SafetyValidator.Run(new ShowCompiler().Compile(lake)).Issues.Exists(i => i.Kind == IssueKind.FlameZone), Is.False);

            // Flames with the drones over the pad, 200 m away: armed, and clear.
            ValidationReport far = SafetyValidator.Run(new ShowCompiler().Compile(Festival(OverThePad, FlameMode.Salvo)));
            Assert.That(far.FlamesArmed, Is.True);
            Assert.That(far.Passed, Is.True, string.Join("\n", far.Issues.ConvertAll(i => i.Message)));
            Assert.That(far.MinFlameClearance, Is.EqualTo(float.PositiveInfinity), "further than the search radius");
        }

        [Test]
        public void FlameDistanceMeasuresTheWholeFlame()
        {
            Vector3 nozzle = FestivalStage.FlameNozzles[0];
            Assert.That(FestivalStage.DistanceToFlames(nozzle + new Vector3(0f, 5f, 0f)), Is.EqualTo(0f).Within(1e-4f), "inside the flame");
            Assert.That(FestivalStage.DistanceToFlames(nozzle + new Vector3(0f, FestivalStage.FlameReach + 7f, 0f)), Is.EqualTo(7f).Within(1e-3f), "above its reach");
            Vector3 p = FestivalStage.Center + new Vector3(0f, 80f, 150f);
            Assert.That(FestivalStage.DistanceToFlameBounds(p), Is.LessThanOrEqualTo(FestivalStage.DistanceToFlames(p)), "the bounds are a lower bound");
            Assert.That(FestivalStage.FlameNozzles.Count, Is.EqualTo(2 * FestivalStage.FlamesPerBoat + FestivalStage.FrontFlames));
        }

        [Test]
        public void EffectsNeverChangeTheFlightPlan()
        {
            ShowDocument plain = Festival(OverThePad);
            ShowDocument staged = plain.Clone();
            staged.Cues[0].Effects = new StageEffects { Lasers = LaserMode.Off, Fountains = FountainMode.Tall, Flames = FlameMode.Salvo, Steam = true };
            CompiledShow a = new ShowCompiler().Compile(plain), b = new ShowCompiler().Compile(staged);
            Assert.That(b.Duration, Is.EqualTo(a.Duration));
            var pa = new Vector3[a.DroneCount];
            var pb = new Vector3[b.DroneCount];
            for (float t = 0f; t <= a.Duration; t += 1.7f)
            {
                a.SamplePositions(t, pa);
                b.SamplePositions(t, pb);
                Assert.That(pb, Is.EqualTo(pa), "t = " + t);
            }
        }

        [Test]
        public void StageCueSheetListsEveryScene()
        {
            ShowDocument doc = Festival(OverThePad, FlameMode.Beat, steam: true);
            doc.Cues[0].Name = "Fire, \"drum\"";
            CompiledShow show = new ShowCompiler().Compile(doc);
            CueTiming c = show.CueTimings[0];
            string[] lines = ShowExporter.ToStageCueSheet(show).TrimEnd('\n').Split('\n');
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.StartWith("cue,name,flight_in_s,hold_start_s,hold_end_s,hold_start_tc,lasers,fountains,flames"));
            string expected = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "1,\"Fire, \"\"drum\"\"\",{0:0.00},{1:0.00},{2:0.00},{3},auto,auto,beat,{4:0.00},{5:0.00},on",
                c.TransitStart, c.HoldStart, c.HoldEnd, ShowExporter.Timecode(c.HoldStart),
                c.HoldStart - FestivalStage.FlameArmLead, c.HoldEnd + FestivalStage.FlameArmTail);
            Assert.That(lines[1], Is.EqualTo(expected));

            doc.Venue = ShowVenue.Lake;
            string lake = ShowExporter.ToStageCueSheet(new ShowCompiler().Compile(doc));
            Assert.That(lake, Does.Contain(",none,none,none,,,none"));

            Assert.That(ShowExporter.Timecode(61.04f), Is.EqualTo("00:01:01:01"));
            Assert.That(ShowExporter.Timecode(3725.5f), Is.EqualTo("01:02:05:12").Or.EqualTo("01:02:05:13"));
        }

        [Test]
        public void FlightReportShowsTheStage()
        {
            CompiledShow show = new ShowCompiler().Compile(Festival(OverThePad, FlameMode.Salvo, steam: true));
            string report = ShowExporter.ToFlightReport(show, SafetyValidator.Run(show));
            Assert.That(report, Does.Contain("Closest to the stage flames: more than 60 m"));
            Assert.That(report, Does.Contain("Venue: festival stage"));
            Assert.That(report, Does.Contain("lasers auto · fountains auto · flames salvo · steam"));
        }

        [Test]
        public void SecondDemoCuesEveryEffect()
        {
            ShowDocument doc = DemoShows.LacBirdFestival();
            Assert.That(doc.Cues.Exists(c => c.Effects.Flames == FlameMode.Beat), "fire on the drum rhythm");
            Assert.That(doc.Cues.Exists(c => c.Effects.Flames == FlameMode.Salvo), "a salvo for the bird");
            Assert.That(doc.Cues.Exists(c => c.Effects.Steam));
            foreach (LaserMode mode in new[] { LaserMode.Off, LaserMode.Fans, LaserMode.Sweep, LaserMode.Tunnel })
            {
                Assert.That(doc.Cues.Exists(c => c.Effects.Lasers == mode), "lasers " + mode);
            }
            foreach (FountainMode mode in new[] { FountainMode.Dance, FountainMode.Arch, FountainMode.Tall })
            {
                Assert.That(doc.Cues.Exists(c => c.Effects.Fountains == mode), "fountains " + mode);
            }
            // The first demo stays on the lake with no stage.
            Assert.That(DemoShows.StarGroupNight().Venue, Is.EqualTo(ShowVenue.Lake));
        }
    }
}
