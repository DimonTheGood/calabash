using UnityEngine;
using UnityEngine.Events;

namespace Calabash.Combat
{
    /// <summary>
    /// Топливо = здоровье (docs/characters/01-protagonist.md). Удары и убийства подкармливают огонь,
    /// урон и вода — гасят. Пока без визуала тела: состояние читается из Fuel01.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZhdanFuel : MonoBehaviour
    {
        public float maxFuel = 100f;
        [Tooltip("Топливо на старте")]
        public float startFuel = 100f;
        [Tooltip("Огонь сам прогорает, ед./с. 0 — не прогорает")]
        public float decayPerSecond = 0f;
        [Tooltip("Множитель урона от воды (style bible: вода бьёт больнее всего)")]
        public float waterDamageMultiplier = 2f;

        [Tooltip("Аргумент — новое значение топлива")]
        public UnityEvent<float> onChanged = new UnityEvent<float>();
        public UnityEvent onEmpty = new UnityEvent();

        float fuel;
        bool initialized;

        public float Fuel { get { Init(); return fuel; } }
        public float Fuel01 => maxFuel > 0f ? Fuel / maxFuel : 0f;
        public bool IsEmpty => Fuel <= 0f;

        void Awake() => Init();

        void Init()
        {
            if (initialized) return;
            initialized = true;
            fuel = Mathf.Clamp(startFuel, 0f, maxFuel);
        }

        void Update()
        {
            if (decayPerSecond > 0f) Drain(decayPerSecond * Time.deltaTime);
        }

        /// <summary>Подкормить огонь.</summary>
        public void Add(float amount)
        {
            if (amount <= 0f) return;
            Set(Fuel + amount);
        }

        /// <summary>Урон по герою. water — удар водой, бьёт сильнее.</summary>
        public void Drain(float amount, bool water = false)
        {
            if (amount <= 0f) return;
            Set(Fuel - amount * (water ? waterDamageMultiplier : 1f));
        }

        public void Set(float value)
        {
            Init();
            float old = fuel;
            fuel = Mathf.Clamp(value, 0f, maxFuel);
            if (Mathf.Approximately(old, fuel)) return;
            onChanged.Invoke(fuel);
            if (fuel <= 0f && old > 0f) onEmpty.Invoke();
        }
    }
}
