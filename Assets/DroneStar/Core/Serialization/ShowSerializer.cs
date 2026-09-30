using System;
using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>Reads and writes <c>.dronestar.json</c> show files.</summary>
    public static class ShowSerializer
    {
        public const string FormatTag = "dronestar-show";
        public const string FileExtension = ".dronestar.json";

        public static string ToJson(ShowDocument doc, bool pretty = true)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            JsonValue root = JsonValue.NewObject()
                .Set("format", FormatTag)
                .Set("version", ShowDocument.CurrentFormatVersion)
                .Set("title", doc.Title)
                .Set("author", doc.Author)
                .Set("droneCount", doc.DroneCount)
                .Set("preShowSeconds", doc.PreShowSeconds)
                .Set("postShowSeconds", doc.PostShowSeconds)
                .Set("venue", doc.Venue.ToString());

            SafetyLimits l = doc.Limits;
            root.Set("limits", JsonValue.NewObject()
                .Set("minSeparation", l.MinSeparation)
                .Set("maxSpeed", l.MaxSpeed)
                .Set("maxAcceleration", l.MaxAcceleration)
                .Set("maxAltitude", l.MaxAltitude)
                .Set("geofenceRadius", l.GeofenceRadius)
                .Set("maxFlightSeconds", l.MaxFlightSeconds));

            root.Set("pad", JsonValue.NewObject()
                .Set("center", Vec(doc.Pad.Center))
                .Set("spacing", doc.Pad.Spacing)
                .Set("hoverAltitude", doc.Pad.HoverAltitude)
                .Set("columns", doc.Pad.Columns));

            JsonValue cues = JsonValue.NewArray();
            foreach (Cue cue in doc.Cues)
            {
                FormationSpec f = cue.Formation;
                JsonValue formation = JsonValue.NewObject()
                    .Set("kind", f.Kind.ToString())
                    .Set("style", f.Style.ToString())
                    .Set("center", Vec(f.Center))
                    .Set("size", f.Size)
                    .Set("yaw", f.YawDegrees)
                    .Set("pitch", f.PitchDegrees)
                    .Set("layers", f.Layers)
                    .Set("text", f.Text)
                    .Set("model", f.Model)
                    .Set("points", f.Points)
                    .Set("turns", f.Turns);
                // Written whenever present (not only for Custom) so switching shapes and back loses nothing.
                if (f.CustomPoints.Count > 0)
                {
                    JsonValue pts = JsonValue.NewArray();
                    foreach (Vector3 p in f.CustomPoints) pts.Add(Vec(p));
                    formation.Set("customPoints", pts);
                }

                cues.Add(JsonValue.NewObject()
                    .Set("name", cue.Name)
                    .Set("autoTransition", cue.AutoTransition)
                    .Set("transitionSeconds", cue.TransitionSeconds)
                    .Set("holdSeconds", cue.HoldSeconds)
                    .Set("formation", formation)
                    .Set("light", JsonValue.NewObject()
                        .Set("effect", cue.Light.Effect.ToString())
                        .Set("colorA", cue.Light.ColorA.ToHex())
                        .Set("colorB", cue.Light.ColorB.ToHex())
                        .Set("speed", cue.Light.Speed)
                        .Set("brightness", cue.Light.Brightness)
                        .Set("angle", cue.Light.AngleDegrees))
                    .Set("motion", JsonValue.NewObject()
                        .Set("kind", cue.Motion.Kind.ToString())
                        .Set("degreesPerSecond", cue.Motion.DegreesPerSecond)
                        .Set("frequencyHz", cue.Motion.FrequencyHz)
                        .Set("amount", cue.Motion.Amount))
                    .Set("effects", JsonValue.NewObject()
                        .Set("lasers", cue.Effects.Lasers.ToString())
                        .Set("fountains", cue.Effects.Fountains.ToString())
                        .Set("flames", cue.Effects.Flames.ToString())
                        .Set("steam", cue.Effects.Steam)));
            }
            root.Set("cues", cues);
            return root.ToJson(pretty);
        }

        /// <summary>
        /// Parses a show file. Missing fields take their defaults and out-of-range values are clamped;
        /// structural problems (not JSON, wrong format tag, newer version) throw <see cref="FormatException"/>.
        /// </summary>
        public static ShowDocument FromJson(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            if (root.Type != JsonType.Object) throw new FormatException("A show file must be a JSON object.");
            string format = root.GetString("format", null);
            if (format != FormatTag) throw new FormatException("This is not a Drone Star show file.");
            double rawVersion = root.GetNumber("version", 0);
            int version = (int)rawVersion;
            if (version != rawVersion || version < 1 || version > ShowDocument.CurrentFormatVersion)
            {
                throw new FormatException("Show file version " + version + " is not supported by this version of Drone Star Studio.");
            }

            var doc = new ShowDocument
            {
                Title = root.GetString("title", "Untitled Show"),
                Author = root.GetString("author", ""),
                DroneCount = root.GetInt("droneCount", 200),
                PreShowSeconds = root.GetFloat("preShowSeconds", 3f),
                PostShowSeconds = root.GetFloat("postShowSeconds", 3f),
                Venue = ReadEnum(root.GetString("venue", nameof(ShowVenue.Lake)), ShowVenue.Lake),
            };

            JsonValue limits = root.Get("limits");
            if (limits != null)
            {
                SafetyLimits l = doc.Limits;
                l.MinSeparation = limits.GetFloat("minSeparation", l.MinSeparation);
                l.MaxSpeed = limits.GetFloat("maxSpeed", l.MaxSpeed);
                l.MaxAcceleration = limits.GetFloat("maxAcceleration", l.MaxAcceleration);
                l.MaxAltitude = limits.GetFloat("maxAltitude", l.MaxAltitude);
                l.GeofenceRadius = limits.GetFloat("geofenceRadius", l.GeofenceRadius);
                l.MaxFlightSeconds = limits.GetFloat("maxFlightSeconds", l.MaxFlightSeconds);
            }

            JsonValue pad = root.Get("pad");
            if (pad != null)
            {
                doc.Pad.Center = ReadVec(pad.Get("center"), doc.Pad.Center);
                doc.Pad.Spacing = pad.GetFloat("spacing", doc.Pad.Spacing);
                doc.Pad.HoverAltitude = pad.GetFloat("hoverAltitude", doc.Pad.HoverAltitude);
                doc.Pad.Columns = pad.GetInt("columns", doc.Pad.Columns);
            }

            JsonValue cues = root.Get("cues");
            if (cues != null && cues.Type == JsonType.Array)
            {
                foreach (JsonValue c in cues.Items)
                {
                    if (c.Type != JsonType.Object) continue;
                    doc.Cues.Add(ReadCue(c));
                }
            }

            ShowSanitizer.Sanitize(doc);
            return doc;
        }

        static Cue ReadCue(JsonValue c)
        {
            var cue = new Cue
            {
                Name = c.GetString("name", "Cue"),
                AutoTransition = c.GetBool("autoTransition", true),
                TransitionSeconds = c.GetFloat("transitionSeconds", 8f),
                HoldSeconds = c.GetFloat("holdSeconds", 8f),
            };

            JsonValue f = c.Get("formation");
            if (f != null)
            {
                FormationSpec spec = cue.Formation;
                spec.Kind = ReadEnum(f.GetString("kind", null), spec.Kind);
                spec.Style = ReadEnum(f.GetString("style", null), spec.Style);
                spec.Center = ReadVec(f.Get("center"), spec.Center);
                spec.Size = f.GetFloat("size", spec.Size);
                spec.YawDegrees = f.GetFloat("yaw", spec.YawDegrees);
                spec.PitchDegrees = f.GetFloat("pitch", spec.PitchDegrees);
                spec.Layers = f.GetInt("layers", spec.Layers);
                spec.Text = f.GetString("text", spec.Text);
                spec.Model = f.GetString("model", spec.Model);
                spec.Points = f.GetInt("points", spec.Points);
                spec.Turns = f.GetFloat("turns", spec.Turns);
                JsonValue pts = f.Get("customPoints");
                if (pts != null && pts.Type == JsonType.Array)
                {
                    foreach (JsonValue p in pts.Items)
                    {
                        if (spec.CustomPoints.Count >= ShowBounds.MaxDrones) break;
                        if (TryReadVec(p, out Vector3 v)) spec.CustomPoints.Add(v);
                    }
                }
            }

            JsonValue light = c.Get("light");
            if (light != null)
            {
                LightSpec l = cue.Light;
                l.Effect = ReadEnum(light.GetString("effect", null), l.Effect);
                if (LedColor.TryParseHex(light.GetString("colorA", null), out LedColor a)) l.ColorA = a;
                if (LedColor.TryParseHex(light.GetString("colorB", null), out LedColor b)) l.ColorB = b;
                l.Speed = light.GetFloat("speed", l.Speed);
                l.Brightness = light.GetFloat("brightness", l.Brightness);
                l.AngleDegrees = light.GetFloat("angle", l.AngleDegrees);
            }

            JsonValue motion = c.Get("motion");
            if (motion != null)
            {
                MotionSpec m = cue.Motion;
                m.Kind = ReadEnum(motion.GetString("kind", null), m.Kind);
                m.DegreesPerSecond = motion.GetFloat("degreesPerSecond", m.DegreesPerSecond);
                m.FrequencyHz = motion.GetFloat("frequencyHz", m.FrequencyHz);
                m.Amount = motion.GetFloat("amount", m.Amount);
            }

            // Shows saved before stage effects existed keep the automatic choreography and no flames or steam.
            JsonValue effects = c.Get("effects");
            if (effects != null)
            {
                StageEffects fx = cue.Effects;
                fx.Lasers = ReadEnum(effects.GetString("lasers", null), fx.Lasers);
                fx.Fountains = ReadEnum(effects.GetString("fountains", null), fx.Fountains);
                fx.Flames = ReadEnum(effects.GetString("flames", null), fx.Flames);
                fx.Steam = effects.GetBool("steam", fx.Steam);
            }
            return cue;
        }

        static T ReadEnum<T>(string text, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            // Only accept names, never numbers, so "7" cannot smuggle in an undefined value.
            if (char.IsDigit(text[0]) || text[0] == '-' || text.IndexOf(',') >= 0) return fallback;
            return Enum.TryParse(text, true, out T value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
        }

        static JsonValue Vec(Vector3 v)
        {
            return JsonValue.NewArray().Add(JsonValue.From(v.X)).Add(JsonValue.From(v.Y)).Add(JsonValue.From(v.Z));
        }

        static Vector3 ReadVec(JsonValue v, Vector3 fallback) => TryReadVec(v, out Vector3 result) ? result : fallback;

        static bool TryReadVec(JsonValue v, out Vector3 result)
        {
            result = default;
            if (v == null || v.Type != JsonType.Array || v.Items.Count != 3) return false;
            IReadOnlyList<JsonValue> items = v.Items;
            for (int i = 0; i < 3; i++)
            {
                if (items[i].Type != JsonType.Number) return false;
            }
            result = new Vector3((float)items[0].NumberValue, (float)items[1].NumberValue, (float)items[2].NumberValue);
            return ShowMath.IsFinite(result);
        }
    }
}
