using System.Collections.Generic;
using UnityEngine;

namespace ZombieSLG.Battle
{
    public enum SfxId
    {
        HeroShot,
        TowerShot,
        ZombieDeath,
        Explosion,
        HqHit,
        HeroHurt,
        Siren,
        Victory,
        Defeat,
        Click,
    }

    /// <summary>
    /// Procedurally synthesised placeholder sounds (no audio assets yet), played through a small
    /// voice pool with per-sound rate limiting so 200 zombies dying don't clip the mixer.
    /// </summary>
    public sealed class Sfx
    {
        private const int SampleRate = 22050;
        private readonly Dictionary<SfxId, AudioClip> _clips = new Dictionary<SfxId, AudioClip>();
        private readonly Dictionary<SfxId, float> _lastPlayed = new Dictionary<SfxId, float>();
        private readonly Dictionary<SfxId, float> _minInterval = new Dictionary<SfxId, float>();
        private readonly Dictionary<SfxId, float> _volume = new Dictionary<SfxId, float>();
        private readonly AudioSource[] _voices;
        private int _cursor;

        public float MasterVolume = 0.8f;

        public Sfx(Transform root)
        {
            var go = new GameObject("Sfx");
            go.transform.SetParent(root, false);
            _voices = new AudioSource[12];
            for (int i = 0; i < _voices.Length; i++)
            {
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _voices[i] = src;
            }

            Register(SfxId.HeroShot, Noise("HeroShot", 0.09f, 45f, 0.35f, 180f, 0.25f), 0.03f, 0.35f);
            Register(SfxId.TowerShot, Noise("TowerShot", 0.06f, 70f, 0.55f, 260f, 0.15f), 0.05f, 0.18f);
            Register(SfxId.ZombieDeath, Noise("ZombieDeath", 0.22f, 18f, 0.08f, 90f, 0.55f), 0.06f, 0.45f);
            Register(SfxId.Explosion, Noise("Explosion", 1.1f, 4.5f, 0.04f, 45f, 0.35f), 0.1f, 1f);
            Register(SfxId.HqHit, Noise("HqHit", 0.15f, 25f, 0.1f, 70f, 0.6f), 0.25f, 0.5f);
            Register(SfxId.HeroHurt, Noise("HeroHurt", 0.12f, 30f, 0.2f, 220f, 0.5f), 0.3f, 0.4f);
            Register(SfxId.Siren, Sweep("Siren", 1.4f, 420f, 780f, 2f), 1f, 0.35f);
            Register(SfxId.Victory, Notes("Victory", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.14f), 1f, 0.5f);
            Register(SfxId.Defeat, Notes("Defeat", new[] { 392f, 311.13f, 261.63f, 196f }, 0.2f), 1f, 0.5f);
            Register(SfxId.Click, Notes("Click", new[] { 880f }, 0.05f), 0.05f, 0.3f);
        }

        private void Register(SfxId id, AudioClip clip, float minInterval, float volume)
        {
            _clips[id] = clip;
            _minInterval[id] = minInterval;
            _volume[id] = volume;
            _lastPlayed[id] = -999f;
        }

        public void Play(SfxId id, float pitchJitter = 0.08f)
        {
            float now = Time.unscaledTime;
            if (now - _lastPlayed[id] < _minInterval[id]) return;
            _lastPlayed[id] = now;

            var src = _voices[_cursor];
            _cursor = (_cursor + 1) % _voices.Length;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.PlayOneShot(_clips[id], _volume[id] * MasterVolume);
        }

        /// <summary>Filtered noise burst with an exponential envelope, optionally mixed with a sine thump.</summary>
        private static AudioClip Noise(string name, float duration, float decay, float brightness, float toneHz, float toneMix)
        {
            int n = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t * 800f);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * brightness;
                float tone = Mathf.Sin(2f * Mathf.PI * toneHz * t * (1f - t * 0.5f));
                data[i] = Mathf.Clamp((lp * (1f - toneMix) * 2.2f + tone * toneMix) * env, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip Sweep(string name, float duration, float fromHz, float toHz, float cycles)
        {
            int n = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float u = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * cycles * t / duration);
                float hz = Mathf.Lerp(fromHz, toHz, u);
                phase += 2f * Mathf.PI * hz / SampleRate;
                float env = Mathf.Clamp01(t * 20f) * Mathf.Clamp01((duration - t) * 6f);
                data[i] = Mathf.Sin(phase) * 0.6f * env;
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip Notes(string name, float[] freqs, float noteLength)
        {
            int perNote = Mathf.CeilToInt(noteLength * SampleRate);
            int n = perNote * freqs.Length + SampleRate / 4;
            var data = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = k * perNote;
                int len = Mathf.Min(n - start, perNote * 3);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-t * 6f) * Mathf.Clamp01(t * 300f);
                    float s = Mathf.Sin(2f * Mathf.PI * freqs[k] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * freqs[k] * t);
                    data[start + i] += s * 0.35f * env;
                }
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
