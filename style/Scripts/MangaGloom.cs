using UnityEngine;
using UnityEngine.InputSystem;

namespace Calabash.Style
{
    /// <summary>
    /// Настройки «мрака» — манга-фильтра Calabash/MangaGloom (растр, штриховка, тушь, серое небо).
    /// Вешается на камеру. Сам фильтр — полноэкранный проход URP, его подключает меню
    /// Calabash → Мрак → Подключить к URP. Без этого компонента фильтр выключен.
    /// G — включить/выключить в игре.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Calabash/Manga Gloom")]
    public class MangaGloom : MonoBehaviour
    {
        [Header("Общее")]
        [Tooltip("0 — обычная картинка, 1 — полный мрак")]
        [Range(0f, 1f)] public float intensity = 1f;
        [Tooltip("Клавиша вкл/выкл в Play Mode")]
        public Key toggleKey = Key.G;

        [Header("Тон")]
        [Tooltip("Всё темнее этого — чистая тушь")]
        [Range(0f, 0.5f)] public float blackPoint = 0.08f;
        [Tooltip("Всё светлее этого — чистая бумага")]
        [Range(0.5f, 1f)] public float whitePoint = 0.95f;
        [Tooltip("Больше 1 — средние тона темнее, мрачнее")]
        [Range(0.5f, 3f)] public float gamma = 1.45f;
        [Tooltip("Тон бумаги: самый светлый цвет кадра")]
        [Range(0.6f, 1f)] public float paper = 0.96f;

        [Header("Растр (точки)")]
        [Tooltip("Шаг сетки точек в пикселях при высоте экрана 1080")]
        [Range(2f, 16f)] public float dotPeriod = 5f;
        [Range(0f, 1f)] public float dotStrength = 0.8f;
        [Tooltip("Точки появляются, когда тон темнее этого")]
        [Range(0.5f, 1f)] public float dotStart = 0.9f;
        [Tooltip("Тон самой точки")]
        [Range(0f, 0.8f)] public float dotInk = 0.22f;

        [Header("Штриховка")]
        [Tooltip("Шаг линий в пикселях при высоте экрана 1080")]
        [Range(2f, 16f)] public float hatchPeriod = 4.5f;
        [Range(0f, 1f)] public float hatchStrength = 0.85f;
        [Tooltip("Косые линии появляются, когда тон темнее этого")]
        [Range(0f, 1f)] public float hatchStart = 0.5f;
        [Tooltip("Перекрёстная штриховка, когда тон темнее этого")]
        [Range(0f, 1f)] public float crossHatchStart = 0.25f;

        [Header("Контур тушью")]
        [Tooltip("Толщина линии в пикселях при высоте экрана 1080")]
        [Range(0f, 4f)] public float inkWidth = 1.3f;
        [Tooltip("Чувствительность к разрыву глубины (меньше — больше линий)")]
        [Range(0.005f, 0.5f)] public float depthThreshold = 0.06f;
        [Tooltip("Чувствительность к излому поверхности (меньше — больше линий)")]
        [Range(0.05f, 1f)] public float normalThreshold = 0.3f;
        [Tooltip("Тон туши")]
        [Range(0f, 0.3f)] public float ink = 0.04f;
        [Tooltip("С какого расстояния линии бледнеют, м")]
        public float inkFadeStart = 25f;
        [Tooltip("На каком расстоянии линии бледнее всего, м")]
        public float inkFadeEnd = 110f;
        [Tooltip("Сила линий вдали")]
        [Range(0f, 1f)] public float inkFar = 0.35f;
        [Tooltip("Неровность линии, как от кисти")]
        [Range(0f, 1f)] public float inkWobble = 0.5f;

        [Header("Небо и даль")]
        [Tooltip("Брать начало и конец тумана из Lighting → Fog")]
        public bool fogFromScene = true;
        public float fogStart = 15f;
        public float fogEnd = 120f;
        [Tooltip("Во сколько раз темнее становятся небо и туман: серое растровое небо как в рендерах")]
        [Range(0.3f, 1f)] public float skyDarken = 0.8f;
        [Tooltip("Белые полосы ветра на небе")]
        [Range(0f, 1f)] public float skyStreaks = 0.9f;
        [Tooltip("Сколько полос на всё небо")]
        [Range(2f, 30f)] public float streakCount = 11f;
        [Tooltip("Толщина полосы в пикселях при высоте экрана 1080")]
        [Range(0.5f, 8f)] public float streakWidth = 3f;
        [Range(0f, 1f)] public float streakTone = 0.93f;
        [Tooltip("Волнистость полос")]
        [Range(0f, 1f)] public float streakWave = 0.25f;

        [Header("Снег штрихами")]
        [Range(0f, 1f)] public float snowStreaks = 0.55f;
        [Tooltip("Наклон штрихов, градусы от вертикали")]
        [Range(-60f, 60f)] public float snowAngle = 28f;
        [Tooltip("Скорость, пикселей в секунду")]
        public float snowSpeed = 900f;
        [Tooltip("Доля видимых штрихов")]
        [Range(0f, 1f)] public float snowDensity = 0.12f;

        [Header("Кадр")]
        [Range(0f, 1f)] public float vignette = 0.4f;
        [Range(0f, 0.2f)] public float grain = 0.035f;
        [Tooltip("Пиксели насыщеннее этого остаются цветными (огонь, кровь, вода, контур героя)")]
        [Range(0f, 1f)] public float keepColorSaturation = 0.22f;

        static readonly int IdIntensity = Shader.PropertyToID("_MG_Intensity");
        static readonly int IdTone = Shader.PropertyToID("_MG_Tone");
        static readonly int IdDots = Shader.PropertyToID("_MG_Dots");
        static readonly int IdHatch = Shader.PropertyToID("_MG_Hatch");
        static readonly int IdInk = Shader.PropertyToID("_MG_Ink");
        static readonly int IdInkFade = Shader.PropertyToID("_MG_InkFade");
        static readonly int IdFog = Shader.PropertyToID("_MG_Fog");
        static readonly int IdStreaks = Shader.PropertyToID("_MG_Streaks");
        static readonly int IdSnow = Shader.PropertyToID("_MG_Snow");
        static readonly int IdExtra = Shader.PropertyToID("_MG_Extra");

        bool switchedOff;

        /// <summary>Включён ли фильтр сейчас (с учётом переключения клавишей).</summary>
        public bool IsOn => !switchedOff && intensity > 0f;

        public void Toggle() => switchedOff = !switchedOff;

        void OnEnable() => Push();
        void OnValidate() => Push();

        void OnDisable() => Shader.SetGlobalFloat(IdIntensity, 0f);

        void Update()
        {
            if (Application.isPlaying && Keyboard.current != null && toggleKey != Key.None && Keyboard.current[toggleKey].wasPressedThisFrame)
                Toggle();
            Push();
        }

        public void Push()
        {
            if (!isActiveAndEnabled) return;
            float fs = fogStart, fe = fogEnd;
            if (fogFromScene && RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear)
            {
                fs = RenderSettings.fogStartDistance;
                fe = RenderSettings.fogEndDistance;
            }
            Shader.SetGlobalFloat(IdIntensity, switchedOff ? 0f : intensity);
            Shader.SetGlobalVector(IdTone, new Vector4(blackPoint, whitePoint, gamma, paper));
            Shader.SetGlobalVector(IdDots, new Vector4(dotPeriod, dotStrength, dotStart, dotInk));
            Shader.SetGlobalVector(IdHatch, new Vector4(hatchPeriod, hatchStrength, hatchStart, crossHatchStart));
            Shader.SetGlobalVector(IdInk, new Vector4(inkWidth, depthThreshold, normalThreshold, ink));
            Shader.SetGlobalVector(IdInkFade, new Vector4(inkFadeStart, inkFadeEnd, inkFar, inkWobble));
            Shader.SetGlobalVector(IdFog, new Vector4(fs, fe, skyDarken, skyStreaks));
            Shader.SetGlobalVector(IdStreaks, new Vector4(streakCount, streakWidth, streakTone, streakWave));
            Shader.SetGlobalVector(IdSnow, new Vector4(snowStreaks, snowAngle * Mathf.Deg2Rad, snowSpeed, snowDensity));
            Shader.SetGlobalVector(IdExtra, new Vector4(vignette, grain, keepColorSaturation, Time.frameCount % 64));
        }
    }
}
