using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>
    /// 효과음 (임시, art.md 소리): 녹음 파일 없이 코드로 합성한 소리. 진짜 소리 파일이 생기면
    /// Resources/Audio/&lt;id&gt; 에 넣으면 그것을 먼저 쓴다 (같은 id).
    /// 화면은 id로만 부른다 (북 drum, 칼 clash, 화살 arrow, 나팔 horn, 징 gong, 불 fire, 함성 cheer, 경고 alert, 누름 click, 전장 소음 battle).
    /// 소리 켜기/끄기는 AudioListener.volume (설정 E26).
    /// </summary>
    public static class Sfx
    {
        private const int Rate = 22050;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        private static AudioSource _oneShot;
        private static AudioSource _loop;
        private static System.Random _rng = new System.Random(7);

        public static void Init(GameObject host)
        {
            if (_oneShot != null) return;
            _oneShot = host.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _loop = host.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.volume = 0f;
        }

        /// <summary>한 번 울린다. minGap(초) 안에 같은 소리가 또 오면 건너뛴다 (겹쳐서 시끄럽지 않게).</summary>
        public static void Play(string id, float volume = 1f, float minGap = 0.05f)
        {
            if (_oneShot == null) return;
            float now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(id, out var last) && now - last < minGap) return;
            LastPlayed[id] = now;
            var clip = Get(id);
            if (clip == null) return;
            _oneShot.pitch = 1f;
            _oneShot.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>계속 깔리는 전장 소음의 크기 (0이면 멈춤). 매 프레임 불러도 된다 (부드럽게 따라간다).</summary>
        public static void Ambience(float target, float deltaTime)
        {
            if (_loop == null) return;
            if (_loop.clip == null) _loop.clip = Get("battle");
            float v = Mathf.MoveTowards(_loop.volume, Mathf.Clamp01(target), deltaTime * 0.8f);
            _loop.volume = v;
            if (v > 0.001f && !_loop.isPlaying) _loop.Play();
            else if (v <= 0.001f && _loop.isPlaying) _loop.Stop();
        }

        public static void StopAmbience()
        {
            if (_loop == null) return;
            _loop.volume = 0f;
            _loop.Stop();
        }

        private static AudioClip Get(string id)
        {
            if (Clips.TryGetValue(id, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + id) ?? Synth(id);
            Clips[id] = c;
            return c;
        }

        // ---- 합성 ----

        private static AudioClip Synth(string id)
        {
            switch (id)
            {
                case "click": return Make(id, 0.05f, (t, n) => Mathf.Sin(2 * Mathf.PI * 1800f * t) * Env(t, 0.002f, 0.04f) * 0.5f);
                case "drum": return Make(id, 0.5f, Drum);
                case "duel": return Make(id, 1.2f, (t, n) => DrumRoll(t));
                case "clash": return Make(id, 0.35f, Clash);
                case "arrow": return Make(id, 0.35f, Arrow);
                case "horn": return Make(id, 1.0f, Horn);
                case "gong": return Make(id, 1.8f, Gong);
                case "fire": return Make(id, 0.9f, Fire);
                case "cheer": return Make(id, 1.6f, Cheer);
                case "alert": return Make(id, 0.35f, (t, n) => Mathf.Sin(2 * Mathf.PI * (t < 0.15f ? 880f : 660f) * t) * Env(t % 0.15f, 0.005f, 0.13f) * 0.45f);
                case "battle": return Make(id, 3.0f, Battle, loop: true);
                default: return null;
            }
        }

        private delegate float Wave(float t, float noise);

        private static AudioClip Make(string id, float seconds, Wave wave, bool loop = false)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)(_rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.2f; // 거친 소리를 조금 누그러뜨린 잡음
                data[i] = Mathf.Clamp(wave(t, lp), -1f, 1f);
            }
            if (loop)
            {
                // 끝과 처음을 겹쳐 이음매가 들리지 않게
                int fade = Rate / 4;
                for (int i = 0; i < fade; i++)
                {
                    float k = (float)i / fade;
                    data[i] = data[i] * k + data[n - fade + i] * (1 - k);
                }
                Array.Resize(ref data, n - fade);
                n -= fade;
            }
            var clip = AudioClip.Create("sfx." + id, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>attack초에 올라가 decay초 동안 지수적으로 줄어드는 크기.</summary>
        private static float Env(float t, float attack, float decay) =>
            t < attack ? t / attack : Mathf.Exp(-(t - attack) / Mathf.Max(0.001f, decay) * 3f);

        private static float Drum(float t, float n)
        {
            float f = 55f + 60f * Mathf.Exp(-t * 18f); // 치는 순간 높았다 내려감
            return (Mathf.Sin(2 * Mathf.PI * f * t) * 0.9f + n * 0.4f * Mathf.Exp(-t * 40f)) * Env(t, 0.003f, 0.35f);
        }

        private static float DrumRoll(float t)
        {
            // 점점 빨라지는 북 (일기토)
            float[] hits = { 0f, 0.28f, 0.5f, 0.66f, 0.78f, 0.88f, 0.96f };
            float s = 0f;
            foreach (var h in hits)
                if (t >= h) s += Drum(t - h, 0f) * (0.6f + 0.4f * h);
            return s * 0.7f;
        }

        private static float Clash(float t, float n)
        {
            float metal = Mathf.Sin(2 * Mathf.PI * 2150f * t) + 0.7f * Mathf.Sin(2 * Mathf.PI * 3380f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 5230f * t);
            return (metal * 0.28f * Env(t, 0.001f, 0.18f)) + n * 0.6f * Env(t, 0.001f, 0.03f);
        }

        private static float Arrow(float t, float n)
        {
            // 높아졌다 낮아지는 바람 소리
            float sweep = Mathf.Sin(2 * Mathf.PI * (900f - 1500f * t) * t);
            return (n * 0.5f + sweep * 0.1f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.33f)) * 0.7f;
        }

        private static float Horn(float t, float n)
        {
            float f = 196f * (1f + 0.006f * Mathf.Sin(2 * Mathf.PI * 5f * t));
            float saw = 0f;
            for (int k = 1; k <= 6; k++) saw += Mathf.Sin(2 * Mathf.PI * f * k * t) / k;
            float env = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((1.0f - t) / 0.3f);
            return saw * 0.32f * env;
        }

        private static float Gong(float t, float n)
        {
            float s = Mathf.Sin(2 * Mathf.PI * 110f * t) + 0.6f * Mathf.Sin(2 * Mathf.PI * 167f * t) + 0.4f * Mathf.Sin(2 * Mathf.PI * 293f * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * 431f * t);
            return s * 0.3f * Env(t, 0.004f, 1.2f);
        }

        private static float Fire(float t, float n)
        {
            float crackle = _rng.NextDouble() < 0.004 ? 1f : 0f;
            return (n * 0.35f + crackle * 0.8f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.9f));
        }

        private static float Cheer(float t, float n)
        {
            float wobble = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 3.3f * t) * Mathf.Sin(2 * Mathf.PI * 1.7f * t);
            return n * 0.9f * wobble * Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((1.6f - t) / 0.6f);
        }

        private static float Battle(float t, float n)
        {
            // 멀리서 웅성이는 함성과 발소리 (낮은 잡음 + 느린 출렁임)
            float swell = 0.55f + 0.25f * Mathf.Sin(2 * Mathf.PI * 0.33f * t) + 0.2f * Mathf.Sin(2 * Mathf.PI * 0.71f * t + 1f);
            float thump = Mathf.Sin(2 * Mathf.PI * 60f * t) * Mathf.Max(0f, Mathf.Sin(2 * Mathf.PI * 1.6f * t)) * 0.15f;
            return n * 0.55f * swell + thump;
        }
    }
}
