using Calabash.Combat;
using UnityEngine;

namespace Calabash.Vfx
{
    /// <summary>
    /// Ждан светит сам: точечный свет от его огня. Ночью это главный источник света в деревне.
    /// Чем больше топлива, тем ярче и дальше; пламя дрожит; удары, убийства и удар оземь дают вспышку.
    /// Огонь погас — свет гаснет.
    /// </summary>
    public class ZhdanFireLight : MonoBehaviour
    {
        public ZhdanFuel fuel;
        public ZhdanFireVfx fx;

        public Color color = new Color(1f, 0.74f, 0.5f);
        [Tooltip("Где свет относительно Ждана, м: над головой и чуть впереди, чтобы тело не заслоняло")] public Vector3 offset = new Vector3(0f, 2.3f, 0.35f);

        [Header("Сила по топливу (мало → много)")]
        [Tooltip("Свет в URP гаснет как 1/d², поэтому сила большая: на 5 м остаётся ~1")]
        public float minIntensity = 10f;
        public float maxIntensity = 26f;
        public float minRange = 9f;
        public float maxRange = 20f;

        [Header("Дрожание и вспышки")]
        [Range(0f, 0.6f)] public float flicker = 0.22f;
        [Tooltip("Добавка яркости на вспышке. Больше 15 — руки от первого лица пересвечены добела")] public float flareIntensity = 14f;
        public float flareRange = 9f;
        [Tooltip("Как быстро гаснет вспышка, 1/с")] public float flareDecay = 3.5f;

        [Header("Тени")]
        public bool shadows = true;
        [Tooltip("Ближе этого тень не отбрасывается: тело и руки самого Ждана не загораживают свет, м")]
        public float shadowNearPlane = 0.8f;

        Light lamp;
        float flare;
        float seed;

        public Light Lamp => lamp;

        void Awake()
        {
            if (fuel == null) fuel = GetComponentInParent<ZhdanFuel>();
            if (fx == null) fx = GetComponentInParent<ZhdanFireVfx>();
            seed = Random.value * 100f;
            var go = new GameObject("FireLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            lamp = go.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = color;
            lamp.renderMode = LightRenderMode.ForcePixel;
            lamp.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            lamp.shadowStrength = 0.9f;
            lamp.shadowNearPlane = shadowNearPlane;
            lamp.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Low;   // точечный свет — 6 карт теней, иначе не влезают в атлас
            lamp.range = maxRange;
            lamp.intensity = maxIntensity;
        }

        /// <summary>Вспышка: k ≈ 1 — удар, больше — сильнее.</summary>
        public void Flare(float k) => flare = Mathf.Max(flare, Mathf.Clamp(k, 0f, 2f));

        void Update()
        {
            float dt = Time.deltaTime;
            flare = Mathf.MoveTowards(flare, 0f, dt * flareDecay);
            float heat = fx != null && fx.heatOverride >= 0f ? fx.heatOverride : fuel != null ? fuel.Fuel01 : 1f;
            bool lit = heat > 0.001f;
            float t = Time.time + seed;
            float n = Mathf.PerlinNoise(t * 7f, 0.3f) * 0.6f + Mathf.PerlinNoise(t * 19f, 4.1f) * 0.4f;   // 0..1
            float shake = 1f + flicker * (n - 0.5f) * 2f;
            float h = Mathf.Sqrt(heat);                                         // мало топлива — ещё светит заметно
            float target = lit ? Mathf.Lerp(minIntensity, maxIntensity, h) * shake + flare * flareIntensity : 0f;
            lamp.intensity = Mathf.MoveTowards(lamp.intensity, target, dt * (lit ? 40f : 3f));
            lamp.range = Mathf.Lerp(minRange, maxRange, h) * (0.95f + 0.05f * shake) + flare * flareRange;
            lamp.color = color;
            lamp.enabled = lamp.intensity > 0.01f;
        }
    }
}
