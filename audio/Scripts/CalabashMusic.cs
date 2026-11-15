using UnityEngine;

namespace Calabash.Audio
{
    /// <summary>
    /// Музыка боя: играет по кругу, громче в бою, тише в затишье. Бой включает кто угодно:
    /// CalabashMusic.Combat = true (так делает директор волн из enemies/).
    /// </summary>
    public class CalabashMusic : MonoBehaviour
    {
        public static CalabashMusic Instance { get; private set; }
        /// <summary>Идёт бой — музыка громче.</summary>
        public static bool Combat;

        public AudioClip music;
        [Range(0f, 1f)] public float calmVolume = 0.12f;
        [Range(0f, 1f)] public float combatVolume = 0.4f;
        [Tooltip("Скорость смены громкости, доля в секунду")] public float fade = 0.25f;

        AudioSource source;

        void Awake()
        {
            Instance = this;
            Combat = false;
            source = gameObject.AddComponent<AudioSource>();
            source.clip = music;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 0;
        }

        void Start()
        {
            if (music != null) source.Play();
        }

        void Update()
        {
            float want = Combat ? combatVolume : calmVolume;
            source.volume = Mathf.MoveTowards(source.volume, want, fade * Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
