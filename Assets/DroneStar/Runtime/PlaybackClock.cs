using System;

namespace DroneStar.App
{
    /// <summary>Show-time transport: play, pause, seek, speed and loop.</summary>
    public sealed class PlaybackClock
    {
        public static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };

        public float Time { get; private set; }
        public bool Playing { get; private set; }
        public float Speed { get; private set; } = 1f;
        public bool Loop { get; set; }

        /// <summary>Raised when playback reaches the end of a non-looping show.</summary>
        public event Action Finished;

        public void Play(float duration)
        {
            if (Time >= duration - 1e-3f) Time = 0f;
            Playing = true;
        }

        public void Pause() => Playing = false;

        public void Toggle(float duration)
        {
            if (Playing) Pause();
            else Play(duration);
        }

        public void Seek(float t, float duration)
        {
            if (float.IsNaN(t)) return;
            Time = Math.Max(0f, Math.Min(t, Math.Max(duration, 0f)));
        }

        public void SetSpeed(float speed)
        {
            if (float.IsNaN(speed) || speed <= 0f) return;
            Speed = Math.Min(speed, 8f);
        }

        public void Tick(float deltaSeconds, float duration)
        {
            if (!Playing || duration <= 0f || !(deltaSeconds > 0f)) return;
            // Clamp long frames (tab in background) so the show does not leap ahead.
            Time += Math.Min(deltaSeconds, 0.25f) * Speed;
            if (Time < duration) return;
            if (Loop)
            {
                Time %= duration;
                return;
            }
            Time = duration;
            Playing = false;
            Finished?.Invoke();
        }
    }
}
