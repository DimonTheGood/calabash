using System.Collections.Generic;
using UnityEngine;

namespace Calabash.Audio
{
    /// <summary>
    /// Все короткие звуки игры: банки звуков по имени и пул источников.
    /// Звук: CalabashSfx.Play("punch_heavy", точка). Из банка берётся случайный клип (не тот же, что в прошлый раз),
    /// с разбросом высоты и громкости, поэтому серия ударов не звучит одинаково.
    /// Банки заполняет CalabashAudioSetup из файлов Sounds/&lt;банк&gt;_NN.ogg.
    /// Звук не зависит от Time.timeScale, поэтому удары слышно и во время стоп-кадра.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CalabashSfx : MonoBehaviour
    {
        [System.Serializable]
        public class Bank
        {
            public string id;
            public AudioClip[] clips = new AudioClip[0];
            [Range(0f, 2f)] public float volume = 1f;
            public float pitchMin = 0.94f;
            public float pitchMax = 1.06f;
            [Tooltip("0 — звук без положения (интерфейс, герой), 1 — полностью в мире")]
            [Range(0f, 1f)] public float spatial = 1f;
            [Tooltip("Ближе этого расстояния громкость полная, м")]
            public float minDistance = 3f;
            public float maxDistance = 45f;
            [Tooltip("Не чаще, с: удар по пяти целям сразу не звучит пять раз")]
            public float cooldown = 0.03f;

            [System.NonSerialized] public int last = -1;
            [System.NonSerialized] public float lastTime = -10f;
        }

        public static CalabashSfx Instance { get; private set; }

        [Range(0f, 1f)] public float masterVolume = 1f;
        public int voices = 32;
        public List<Bank> banks = new List<Bank>();

        readonly Dictionary<string, Bank> byId = new Dictionary<string, Bank>();
        AudioSource[] pool;
        int next;

        void Awake()
        {
            Instance = this;
            byId.Clear();
            foreach (var b in banks) if (b != null && !string.IsNullOrEmpty(b.id)) byId[b.id] = b;
            pool = new AudioSource[Mathf.Max(4, voices)];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject($"Voice_{i}");
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.dopplerLevel = 0f;
                pool[i] = s;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool Has(string id) => byId.ContainsKey(id);

        /// <summary>Звук в точке мира.</summary>
        public static AudioSource Play(string id, Vector3 position, float volume = 1f, float pitch = 1f)
            => Instance != null ? Instance.PlayAt(id, position, volume, pitch, false) : null;

        /// <summary>Звук «у уха» — для героя и интерфейса.</summary>
        public static AudioSource Play2D(string id, float volume = 1f, float pitch = 1f)
            => Instance != null ? Instance.PlayAt(id, Vector3.zero, volume, pitch, true) : null;

        public AudioSource PlayAt(string id, Vector3 position, float volume, float pitch, bool flat)
        {
            if (!byId.TryGetValue(id, out var b) || b.clips == null || b.clips.Length == 0) return null;
            float now = Time.unscaledTime;
            if (now - b.lastTime < b.cooldown) return null;
            b.lastTime = now;

            int i = Random.Range(0, b.clips.Length);
            if (b.clips.Length > 1 && i == b.last) i = (i + 1 + Random.Range(0, b.clips.Length - 1)) % b.clips.Length;
            b.last = i;

            var s = NextVoice();
            s.transform.position = position;
            s.clip = b.clips[i];
            s.volume = Mathf.Clamp01(b.volume * volume * masterVolume * Random.Range(0.88f, 1f));
            s.pitch = pitch * Random.Range(b.pitchMin, b.pitchMax);
            s.spatialBlend = flat ? 0f : b.spatial;
            s.minDistance = b.minDistance;
            s.maxDistance = b.maxDistance;
            s.Play();
            return s;
        }

        AudioSource NextVoice()
        {
            // свободный голос, иначе самый старый
            for (int k = 0; k < pool.Length; k++)
            {
                var s = pool[(next + k) % pool.Length];
                if (!s.isPlaying) { next = (next + k + 1) % pool.Length; return s; }
            }
            var old = pool[next];
            next = (next + 1) % pool.Length;
            old.Stop();
            return old;
        }
    }
}
