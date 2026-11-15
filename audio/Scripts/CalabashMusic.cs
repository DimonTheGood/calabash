using System.Collections.Generic;
using UnityEngine;

namespace Calabash.Audio
{
    /// <summary>
    /// Музыка: несколько треков (бой, босс, меню, лимб, рай, ад), между ними плавный переход.
    /// Боевая музыка тише в затишье и громче в бою: CalabashMusic.Combat = true (так делает директор волн).
    /// Трек: CalabashMusic.Play("boss"). Громкость из настроек: CalabashMusic.VolumeScale.
    /// </summary>
    public class CalabashMusic : MonoBehaviour
    {
        [System.Serializable]
        public class Track
        {
            public string id;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 0.4f;
        }

        public static CalabashMusic Instance { get; private set; }
        /// <summary>Идёт бой — музыка громче.</summary>
        public static bool Combat;
        /// <summary>Громкость музыки из настроек, 0..1.</summary>
        public static float VolumeScale = 1f;

        [Tooltip("Трек со старта сцены")] public string startTrack = "battle";
        public List<Track> tracks = new List<Track>();
        [Tooltip("Устарело: трек боя, если список пуст")] public AudioClip music;
        [Tooltip("Боевая музыка в затишье — доля громкости")]
        [Range(0f, 1f)] public float calmShare = 0.3f;
        [Tooltip("Скорость перехода, доля в секунду")] public float fade = 0.5f;

        AudioSource a, b;          // a — текущий, b — уходящий
        Track current;

        void Awake()
        {
            Instance = this;
            Combat = false;
            a = MakeSource();
            b = MakeSource();
            if (tracks.Count == 0 && music != null) tracks.Add(new Track { id = "battle", clip = music, volume = 0.4f });
        }

        AudioSource MakeSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = true;
            s.spatialBlend = 0f;
            s.volume = 0f;
            s.priority = 0;
            s.ignoreListenerPause = true;
            return s;
        }

        void Start()
        {
            if (current == null) PlayTrack(startTrack);
        }

        /// <summary>Сменить трек (если такого нет — ничего).</summary>
        public static void Play(string id)
        {
            if (Instance != null) Instance.PlayTrack(id);
        }

        public void PlayTrack(string id)
        {
            var t = tracks.Find(x => x.id == id && x.clip != null);
            if (t == null || t == current) return;
            current = t;
            (a, b) = (b, a);
            a.clip = t.clip;
            a.volume = 0f;
            a.Play();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float want = 0f;
            if (current != null)
            {
                want = current.volume * VolumeScale;
                if (current.id == "battle" && !Combat) want *= calmShare;
            }
            a.volume = Mathf.MoveTowards(a.volume, want, fade * dt);
            b.volume = Mathf.MoveTowards(b.volume, 0f, fade * dt);
            if (b.isPlaying && b.volume <= 0f) b.Stop();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
