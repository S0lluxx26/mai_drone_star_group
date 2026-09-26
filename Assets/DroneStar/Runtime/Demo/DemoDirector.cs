using System;
using DroneStar.Core;
using UnityEngine;

namespace DroneStar.App
{
    /// <summary>
    /// Runs the show as a cinematic presentation: title card, a camera shot per scene (cut on each new
    /// formation), scene captions, soundtrack and an end card. Shots frame the lit drones' centroid, so they
    /// work for any show, not just the built-in demo.
    /// </summary>
    public sealed class DemoDirector : MonoBehaviour
    {
        public const float TitleSeconds = 7f;
        public const float EndCardSeconds = 6f;

        // Shot types: 0 orbit, 1 shore push-in, 2 lake level, 3 high three-quarter, 4 side dolly.
        // Every fifth scene (from the third) instead opens close beside one drone and pulls back to reveal it.
        static readonly int[] AllShots = { 0, 1, 2, 3, 4 };
        static readonly int[] FrontalShots = { 1, 0, 2 };

        ShowStudioApp app;
        ShowCameraRig rig;
        AmbientScore score;
        CameraMode previousMode = CameraMode.Orbit;
        int lastShot = int.MinValue;
        int lastChimedCue = -1;
        float shotStart;
        float endCardTime = -1f;
        bool previousLoop;
        int captionCue = -1;
        Vector3 smoothedFocus;
        float smoothedRadius = 30f;
        int revealCue = -1;
        Slot revealSlot;
        Vector3 padMin;
        Vector3 padMax;

        public bool IsRunning { get; private set; }
        public event Action<bool> RunningChanged;

        public string Title { get; private set; } = "";
        public float TitleAlpha { get; private set; }
        public string Caption { get; private set; } = "";
        public string CaptionDetail { get; private set; } = "";
        public float CaptionAlpha { get; private set; }
        public float EndCardAlpha { get; private set; }

        public void Initialize(ShowStudioApp owner, ShowCameraRig cameraRig, AmbientScore soundtrack)
        {
            app = owner;
            rig = cameraRig;
            score = soundtrack;
        }

        public void Begin()
        {
            if (IsRunning || app == null || app.Show == null || app.ShowIsStale) return;
            IsRunning = true;
            previousMode = rig.Mode == CameraMode.Cinematic ? CameraMode.Orbit : rig.Mode;
            previousLoop = app.Clock.Loop;
            rig.SetMode(CameraMode.Cinematic);
            app.Clock.Loop = false;
            app.Clock.SetSpeed(1f);
            app.Clock.Seek(0f, app.Show.Duration);
            app.Clock.Play(app.Show.Duration);
            if (score != null) score.Play();
            lastShot = int.MinValue;
            lastChimedCue = -1;
            captionCue = -1;
            revealCue = -1;
            endCardTime = -1f;
            smoothedFocus = app.Focus;
            smoothedRadius = Mathf.Max(app.FocusRadius, 10f);
            Title = app.Show.Document.Title;
            RunningChanged?.Invoke(true);
        }

        public void End()
        {
            if (!IsRunning) return;
            IsRunning = false;
            TitleAlpha = CaptionAlpha = EndCardAlpha = 0f;
            rig.SetMode(previousMode);
            if (score != null) score.Stop();
            app.Clock.Pause();
            app.Clock.Loop = previousLoop;
            RunningChanged?.Invoke(false);
        }

        void Update()
        {
            if (!IsRunning) return;
            CompiledShow show = app.Show;
            if (show == null)
            {
                End();
                return;
            }

            float t = app.Clock.Time;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            ShowSegment seg = show.SegmentAt(t);
            int cue = seg != null ? seg.CueIndex : -1;
            // The pre-show ground segment belongs to the launch shot; only the ground after landing ends the show.
            bool afterLanding = seg != null && seg.Kind == SegmentKind.Ground && show.SegmentIndexAt(t) > 0;
            int shot = cue >= 0 ? cue : (seg != null && (seg.Kind == SegmentKind.Return || seg.Kind == SegmentKind.Landing || afterLanding) ? -2 : -1);

            float follow = 1f - Mathf.Exp(-dt * 1.6f);
            smoothedFocus = Vector3.Lerp(smoothedFocus, app.Focus, follow);
            smoothedRadius = Mathf.Lerp(smoothedRadius, Mathf.Max(app.FocusRadius, 8f), follow);

            bool cut = shot != lastShot;
            if (cut)
            {
                shotStart = t;
                lastShot = shot;
                if (shot == -1) MeasurePads(show);
                smoothedFocus = app.Focus;
                smoothedRadius = Mathf.Max(app.FocusRadius, 8f);
            }
            ComputeShot(shot, t - shotStart, show, out Vector3 position, out Vector3 target, out float fov);
            rig.SetCinematicPose(position, target, fov, cut);

            if (seg != null && seg.Kind == SegmentKind.Hold && cue != lastChimedCue)
            {
                lastChimedCue = cue;
                if (score != null) score.Chime(cue);
            }

            UpdateOverlays(t, show, seg, dt);

            if (app.Clock.Playing) endCardTime = -1f;
            if (!app.Clock.Playing && t >= show.Duration - 1e-3f)
            {
                if (endCardTime < 0f) endCardTime = 0f;
                endCardTime += dt;
                if (endCardTime > EndCardSeconds) End();
            }
        }

        void ComputeShot(int shot, float tau, CompiledShow show, out Vector3 position, out Vector3 target, out float fov)
        {
            Vector3 c = smoothedFocus;
            float r = smoothedRadius;
            float dist = r * 2.6f + 45f;
            target = c;
            fov = 45f;
            if (shot == -1)
            {
                // Launch: at the front corner of the pad grid, eye level with the first rows, so the nearest
                // drones show their airframes and bulbs; the camera eases back and up as the swarm climbs.
                float back = Mathf.Clamp01(tau / 24f);
                back = back * back * (3f - 2f * back);
                float w = Mathf.Max(padMax.x - padMin.x, padMax.z - padMin.z);
                Vector3 corner = new Vector3(padMin.x, padMin.y, padMin.z);
                position = corner + new Vector3(-2.2f, 1.1f, -3.4f) + new Vector3(-0.25f * w, 0.35f * w + 6f, -0.6f * w - 12f) * back;
                Vector3 across = corner + new Vector3(0.35f * w, 1.2f + tau * 0.4f, 0.22f * w);
                target = Vector3.Lerp(across, new Vector3(c.x, Mathf.Max(c.y, 6f), c.z), back);
                fov = Mathf.Lerp(58f, 50f, back);
                return;
            }
            if (shot == -2)
            {
                position = new Vector3(0f, 24f, NightEnvironment.ShoreZ + 20f) + new Vector3(0f, 0f, tau * 1.2f);
                target = c;
                fov = 42f;
                return;
            }
            if (IsReveal(shot, show))
            {
                Reveal(shot, show, out position, out target, out fov);
                return;
            }
            // Flat shapes face the audience, so film them from the front; 3D shapes get the full shot list.
            FormationSpec spec = show.Document.Cues[shot].Formation;
            bool facesAudience = FormationGenerator.IsPlanar(spec) && Mathf.Abs(spec.YawDegrees) < 30f && Mathf.Abs(spec.PitchDegrees) < 40f;
            int[] shots = facesAudience ? FrontalShots : AllShots;
            switch (shots[shot % shots.Length])
            {
                case 0:
                {
                    float angle = (-25f + tau * 4.5f) * Mathf.Deg2Rad;
                    position = c + new Vector3(Mathf.Sin(angle) * dist, dist * 0.12f, -Mathf.Cos(angle) * dist);
                    fov = 44f;
                    break;
                }
                case 1:
                {
                    // From just off the shore, drifting out over the water toward the show.
                    float push = Mathf.Clamp01(tau / 22f);
                    position = Vector3.Lerp(new Vector3(c.x * 0.3f, 3.5f, NightEnvironment.ShoreZ + 12f), new Vector3(c.x * 0.3f, 8f, NightEnvironment.ShoreZ + 80f), push * push * (3f - 2f * push));
                    fov = 40f;
                    break;
                }
                case 2:
                    position = new Vector3(c.x - dist * 0.45f + tau * 1.5f, NightEnvironment.WaterLevel + 2.2f, c.z - dist * 0.95f);
                    fov = 52f;
                    break;
                case 3:
                {
                    float angle = (35f - tau * 3f) * Mathf.Deg2Rad;
                    position = c + new Vector3(Mathf.Sin(angle) * dist * 0.8f, dist * 0.34f, -Mathf.Cos(angle) * dist * 0.8f);
                    fov = 46f;
                    break;
                }
                default:
                    position = c + new Vector3(-dist * 0.45f + tau * 3f, 8f - c.y * 0.6f, -dist * 0.8f);
                    fov = 48f;
                    break;
            }
            // Shots placed near the shore or the water keep big shapes (big fleets) whole with a wider lens.
            float reach = Mathf.Max((target - position).magnitude, 1f);
            fov = Mathf.Clamp(Mathf.Max(fov, 2f * Mathf.Atan(r * 0.85f / reach) * Mathf.Rad2Deg * 1.1f), 30f, 80f);
        }

        /// <summary>Bounds of the launch grid, for the launch shot.</summary>
        void MeasurePads(CompiledShow show)
        {
            padMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            padMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (System.Numerics.Vector3 p in show.PadPositions)
            {
                padMin = Vector3.Min(padMin, p.ToUnity());
                padMax = Vector3.Max(padMax, p.ToUnity());
            }
            if (show.PadPositions.Length == 0) padMin = padMax = show.Document.Pad.Center.ToUnity();
        }

        static bool IsReveal(int cue, CompiledShow show) =>
            cue % 5 == 2 && show.DroneCount >= 200 && cue < show.CueTimings.Count && show.CueTimings[cue].HoldSeconds >= 5f;

        /// <summary>
        /// Opens a few metres from a drone on the audience side of the incoming formation: the swarm flies in and
        /// settles around the camera, then the camera pulls straight back to reveal the whole shape.
        /// </summary>
        void Reveal(int cue, CompiledShow show, out Vector3 position, out Vector3 target, out float fov)
        {
            CueTiming timing = show.CueTimings[cue];
            FormationResult f = timing.Formation;
            Vector3 center = f.Center.ToUnity();
            float h = Mathf.Max(f.HalfSize, 5f);
            if (revealCue != cue)
            {
                revealCue = cue;
                Vector3 aim = center + new Vector3(0.3f * h, -0.2f * h, -1.5f * h);
                float best = float.MaxValue;
                revealSlot = f.Slots.Length > 0 ? f.Slots[0] : default;
                foreach (Slot slot in f.Slots)
                {
                    if (slot.Dark) continue;
                    float d = (slot.Position.ToUnity() - aim).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        revealSlot = slot;
                    }
                }
            }
            float t = app.Clock.Time;
            float hold = timing.HoldSeconds;
            // The anchor drone turns with the formation during the hold, so track where the motion takes it.
            Cue source = show.Document.Cues[cue];
            float local = Mathf.Clamp(t - timing.HoldStart, 0f, hold);
            Vector3 revealAnchor = HoldMotion.Evaluate(source.Motion, f, revealSlot, local, source.HoldSeconds).ToUnity();
            float u = Mathf.Clamp01((t - timing.HoldStart - hold * 0.25f) / Mathf.Max(hold * 0.7f, 1f));
            u = u * u * (3f - 2f * u);
            Vector3 near = revealAnchor + new Vector3(0.9f, 0.35f, -4.5f);
            Vector3 far = center + new Vector3(0f, h * 0.12f, -(h * 2.6f + 45f));
            position = Vector3.Lerp(near, far, u);
            target = Vector3.Lerp(revealAnchor + (center - revealAnchor) * 0.12f, center, u);
            fov = Mathf.Lerp(52f, 44f, u);
        }

        void UpdateOverlays(float t, CompiledShow show, ShowSegment seg, float dt)
        {
            float titleTarget = t < TitleSeconds ? 1f : 0f;
            TitleAlpha = Mathf.MoveTowards(TitleAlpha, titleTarget, dt * 0.8f);

            float captionTarget = 0f;
            if (seg != null && seg.Kind == SegmentKind.Hold)
            {
                if (seg.CueIndex != captionCue)
                {
                    captionCue = seg.CueIndex;
                    Caption = show.Document.Cues[seg.CueIndex].Name;
                    CaptionDetail = string.Format("Scene {0} of {1}", seg.CueIndex + 1, show.Document.Cues.Count);
                }
                float intoHold = t - seg.Start, leftInHold = seg.End - t;
                captionTarget = intoHold > 0.4f && leftInHold > 0.9f ? 1f : 0f;
            }
            CaptionAlpha = Mathf.MoveTowards(CaptionAlpha, captionTarget, dt * 1.6f);

            float endTarget = endCardTime >= 0f && endCardTime < EndCardSeconds - 1f ? 1f : 0f;
            EndCardAlpha = Mathf.MoveTowards(EndCardAlpha, endTarget, dt * 0.9f);
        }
    }
}
