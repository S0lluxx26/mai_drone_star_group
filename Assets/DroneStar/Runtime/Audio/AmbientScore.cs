using System.Collections;
using UnityEngine;

namespace DroneStar.App
{
    /// <summary>
    /// A generated soundtrack for demo runs: a warm 32-second pad loop (D – A – Bm – G) with a soft bell
    /// arpeggio, plus a chime for each new scene. Synthesised in small chunks across frames so neither
    /// the editor nor a WebGL build hitches, and no audio files ship with the app.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AmbientScore : MonoBehaviour
    {
        const int SampleRate = 22050;
        const float LoopSeconds = 32f;
        const string MuteKey = "dronestar.muted";

        static readonly float[][] Chords =
        {
            new[] { 146.83f, 220.00f, 293.66f, 369.99f }, // D
            new[] { 110.00f, 220.00f, 277.18f, 329.63f }, // A
            new[] { 123.47f, 246.94f, 293.66f, 369.99f }, // Bm
            new[] { 98.00f, 196.00f, 246.94f, 293.66f },  // G
        };

        static readonly float[] Pentatonic = { 587.33f, 659.25f, 739.99f, 880.00f, 987.77f, 1174.66f };

        AudioSource music;
        AudioSource chimes;
        AudioClip loop;
        AudioClip festivalLoop;
        AudioClip chime;
        AudioClip drum;
        bool building;
        float targetVolume;

        public bool Muted { get; private set; }

        /// <summary>
        /// Festival shows: the loop gains a bronze-drum rhythm (a strong beat on one, a lighter one on the "and" of
        /// three, as the lasers and fountains pulse) and each new scene is struck on the drum instead of a bell.
        /// </summary>
        public bool Festival
        {
            get => festival;
            set
            {
                if (festival == value) return;
                festival = value;
                AudioClip clip = festival ? festivalLoop : loop;
                if (clip != null && music.clip != clip)
                {
                    bool playing = music.isPlaying;
                    music.clip = clip;
                    if (playing) music.Play();
                }
            }
        }

        bool festival;
        public bool Ready => loop != null;

        void Awake()
        {
            music = GetComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.volume = 0f;
            music.spatialBlend = 0f;
            chimes = gameObject.AddComponent<AudioSource>();
            chimes.playOnAwake = false;
            chimes.spatialBlend = 0f;
            chimes.volume = 0.35f;
            Muted = PlayerPrefs.GetInt(MuteKey, 0) == 1;
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            if (music != null) music.mute = muted;
            if (chimes != null) chimes.mute = muted;
        }

        /// <summary>Starts (or resumes) the loop with a fade-in; builds the clips on first use.</summary>
        public void Play()
        {
            targetVolume = 0.55f;
            music.mute = Muted;
            chimes.mute = Muted;
            if (loop == null)
            {
                if (!building) StartCoroutine(Build());
                return;
            }
            if (!music.isPlaying) music.Play();
        }

        public void Stop() => targetVolume = 0f;

        public void Chime(int index)
        {
            if (chime == null || Muted) return;
            if (festival && drum != null)
            {
                chimes.pitch = 1f;
                chimes.PlayOneShot(drum, 1f);
                return;
            }
            chimes.pitch = Pentatonic[Mathf.Abs(index) % Pentatonic.Length] / Pentatonic[0];
            chimes.PlayOneShot(chime, 0.8f);
        }

        void Update()
        {
            if (music == null) return;
            music.volume = Mathf.MoveTowards(music.volume, targetVolume, Time.unscaledDeltaTime * 0.35f);
            if (targetVolume <= 0f && music.volume <= 0f && music.isPlaying) music.Pause();
        }

        IEnumerator Build()
        {
            building = true;
            int total = (int)(SampleRate * LoopSeconds);
            var data = new float[total];
            const int chunk = SampleRate;
            for (int start = 0; start < total; start += chunk)
            {
                SynthesizeLoop(data, start, Mathf.Min(total, start + chunk));
                yield return null;
            }
            loop = AudioClip.Create("Night of Stars Loop", total, 1, SampleRate, false);
            loop.SetData(data, 0);

            // The festival loop is the same music with the drum rhythm laid over it.
            var festivalData = (float[])data.Clone();
            for (int start = 0; start < total; start += chunk)
            {
                AddDrumRhythm(festivalData, start, Mathf.Min(total, start + chunk));
                yield return null;
            }
            festivalLoop = AudioClip.Create("Festival Loop", total, 1, SampleRate, false);
            festivalLoop.SetData(festivalData, 0);
            music.clip = festival ? festivalLoop : loop;

            int drumLength = SampleRate * 3;
            var drumData = new float[drumLength];
            for (int i = 0; i < drumLength; i++) drumData[i] = DrumHit((float)i / SampleRate) * 0.9f;
            drum = AudioClip.Create("Bronze Drum", drumLength, 1, SampleRate, false);
            drum.SetData(drumData, 0);

            int chimeLength = SampleRate * 2;
            var chimeData = new float[chimeLength];
            for (int i = 0; i < chimeLength; i++)
            {
                float t = (float)i / SampleRate;
                float f = Pentatonic[0];
                float env = Mathf.Exp(-t * 2.6f) * Mathf.Min(1f, t * 200f);
                chimeData[i] = env * (0.5f * Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * f * 2.76f * t) * Mathf.Exp(-t * 4f));
            }
            chime = AudioClip.Create("Scene Chime", chimeLength, 1, SampleRate, false);
            chime.SetData(chimeData, 0);
            building = false;
            if (targetVolume > 0f) music.Play();
        }

        static void SynthesizeLoop(float[] data, int from, int to)
        {
            float chordSeconds = LoopSeconds / Chords.Length;
            for (int i = from; i < to; i++)
            {
                float t = (float)i / SampleRate;
                int chordIndex = Mathf.Min((int)(t / chordSeconds), Chords.Length - 1);
                float local = t - chordIndex * chordSeconds;
                float[] chord = Chords[chordIndex];
                float[] previous = Chords[(chordIndex + Chords.Length - 1) % Chords.Length];

                // Crossfade chords over 1.5 s so the loop has no seams (the last chord leads into the first).
                float blend = Mathf.SmoothStep(0f, 1f, local / 1.5f);
                float pad = 0f;
                for (int v = 0; v < 4; v++)
                {
                    pad += Voice(chord[v], t, v) * blend + Voice(previous[v], t, v) * (1f - blend);
                }
                pad *= 0.09f;

                // Bell arpeggio: an eighth note every 0.5 s cycling through the chord an octave up.
                float step = 0.5f;
                int note = (int)(local / step);
                float noteTime = local - note * step;
                float bellFreq = chord[1 + note % 3] * 2f;
                float bell = Mathf.Sin(2f * Mathf.PI * bellFreq * t) * Mathf.Exp(-noteTime * 5f) * Mathf.Min(1f, noteTime * 400f) * 0.045f;

                // Gentle loop-point fade avoids a click where the clip wraps.
                float edge = Mathf.Min(1f, Mathf.Min(t, LoopSeconds - t) * 40f);
                data[i] = (pad + bell) * edge;
            }
        }

        /// <summary>A struck bronze drum: a falling low thump under a cluster of inharmonic bronze partials.</summary>
        static float DrumHit(float t)
        {
            if (t < 0f) return 0f;
            float attack = Mathf.Min(1f, t * 600f);
            float pitch = 62f + 40f * Mathf.Exp(-t * 18f);
            float thump = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 5.5f) * 0.8f;
            float ring = 0f;
            float[] partials = { 1f, 1.59f, 2.14f, 2.65f, 3.51f };
            for (int k = 0; k < partials.Length; k++)
            {
                ring += Mathf.Sin(2f * Mathf.PI * 176f * partials[k] * t + k) * Mathf.Exp(-t * (1.6f + k * 0.9f)) / (1f + k);
            }
            return attack * (thump + ring * 0.35f);
        }

        static void AddDrumRhythm(float[] data, int from, int to)
        {
            const float bar = 2f;
            for (int i = from; i < to; i++)
            {
                float t = (float)i / SampleRate;
                float inBar = t % bar;
                // Beat on one, a lighter stroke on the "and" of three (1.5 s), matching FestivalVenue.Beat.
                data[i] += DrumHit(inBar) * 0.16f + DrumHit(inBar - 1.5f) * 0.09f + DrumHit(inBar + bar - 1.5f) * 0.09f;
            }
        }

        static float Voice(float freq, float t, int voice)
        {
            float detune = 1f + 0.0025f * (voice - 1.5f);
            float phase = 2f * Mathf.PI * freq * detune * t;
            float wobble = 0.85f + 0.15f * Mathf.Sin(t * (0.3f + voice * 0.07f));
            return (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 2f) + 0.12f * Mathf.Sin(phase * 3f)) * wobble;
        }
    }
}
