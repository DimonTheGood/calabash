using UnityEngine;

namespace Calabash.Vfx
{
    /// <summary>
    /// Короткие помощники для сборки ParticleSystem в коде: эффекты огня собираются при старте,
    /// а не хранятся тяжёлыми префабами. Все системы создаются остановленными.
    /// </summary>
    public static class FireFx
    {
        public static ParticleSystem Create(string name, Transform parent, Material mat, int maxParticles = 200)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.maxParticles = maxParticles;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.YoungestInFront;
            r.minParticleSize = 0f;
            r.maxParticleSize = 4f;
            return ps;
        }

        public static void Main(ParticleSystem ps, Vector2 life, Vector2 speed, Vector2 size, float gravity,
                                ParticleSystemSimulationSpace space, Transform custom = null, bool unscaled = false)
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            m.gravityModifier = gravity;
            m.simulationSpace = space;
            if (space == ParticleSystemSimulationSpace.Custom) m.customSimulationSpace = custom;
            m.useUnscaledTime = unscaled;
            m.startRotation = 0f;
        }

        /// <summary>Случайный поворот спрайта (искры, дым, вспышки; языки пламени должны стоять вверх).</summary>
        public static void RandomRotation(ParticleSystem ps)
        {
            var m = ps.main;
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        /// <summary>Цвет и прозрачность за жизнь частицы. Ключи цвета и альфы — пары (значение, время 0–1).</summary>
        public static void Colors(ParticleSystem ps, Color[] colors, float[] colorTimes, float[] alphas, float[] alphaTimes)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i], colorTimes[i]);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], alphaTimes[i]);
            g.SetKeys(ck, ak);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        public static void Size(ParticleSystem ps, params float[] keys)
        {
            var c = new AnimationCurve();
            for (int i = 0; i < keys.Length; i++) c.AddKey(keys.Length > 1 ? i / (keys.Length - 1f) : 0f, keys[i]);
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, c);
        }

        /// <summary>Атлас tilesX × tilesY. animated — флипбук на всю жизнь частицы, иначе случайный кадр.</summary>
        public static void Sheet(ParticleSystem ps, int tilesX, int tilesY, bool animated, int cycles = 1)
        {
            var t = ps.textureSheetAnimation;
            t.enabled = true;
            t.mode = ParticleSystemAnimationMode.Grid;
            t.numTilesX = tilesX;
            t.numTilesY = tilesY;
            t.animation = ParticleSystemAnimationType.WholeSheet;
            int frames = tilesX * tilesY;
            if (animated)
            {
                t.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                t.startFrame = new ParticleSystem.MinMaxCurve(0f, frames - 1);
                t.cycleCount = cycles;
            }
            else
            {
                t.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                t.startFrame = new ParticleSystem.MinMaxCurve(0f, frames - 0.01f);
            }
        }

        public static void Spin(ParticleSystem ps, float degPerSec)
        {
            var r = ps.rotationOverLifetime;
            r.enabled = true;
            r.z = new ParticleSystem.MinMaxCurve(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }

        /// <summary>Торможение. quadratic — сильнее на большой скорости (искры), иначе линейное (огненные волны долетают).</summary>
        public static void Drag(ParticleSystem ps, float drag, bool quadratic = true)
        {
            var l = ps.limitVelocityOverLifetime;
            l.enabled = true;
            l.drag = drag;
            l.multiplyDragByParticleSize = false;
            l.multiplyDragByParticleVelocity = quadratic;
        }

        /// <summary>Скорость гаснет к концу жизни: частица пролетает примерно startSpeed × жизнь / 2 и встаёт.</summary>
        public static void SlowDown(ParticleSystem ps)
        {
            var v = ps.velocityOverLifetime;
            v.enabled = true;
            v.speedModifier = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f, 0f, -2f), new Keyframe(1f, 0f, 0f, 0f)));
        }

        public static void Rise(ParticleSystem ps, float up, ParticleSystemSimulationSpace space = ParticleSystemSimulationSpace.World)
        {
            var v = ps.velocityOverLifetime;
            v.enabled = true;
            v.space = space;
            v.x = new ParticleSystem.MinMaxCurve(0f);
            v.y = new ParticleSystem.MinMaxCurve(up);
            v.z = new ParticleSystem.MinMaxCurve(0f);
        }

        public static void Noise(ParticleSystem ps, float strength, float frequency = 1.2f)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = strength;
            n.frequency = frequency;
            n.scrollSpeed = 0.6f;
            n.quality = ParticleSystemNoiseQuality.Medium;
        }

        public static void Sphere(ParticleSystem ps, float radius, float arc = 360f)
        {
            var s = ps.shape;
            s.enabled = true;
            s.shapeType = ParticleSystemShapeType.Sphere;
            s.radius = radius;
            s.arc = arc;
        }

        public static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var s = ps.shape;
            s.enabled = true;
            s.shapeType = ParticleSystemShapeType.Cone;
            s.angle = angle;
            s.radius = radius;
        }

        public static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.lengthScale = lengthScale;
        }

        public static void Flat(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sortMode = ParticleSystemSortMode.OldestInFront;
        }

        /// <summary>Выпустить count частиц в точке с разбросом скоростей.</summary>
        public static void Burst(ParticleSystem ps, Vector3 pos, int count, Vector3 baseVelocity, float spread, float upBias = 0f,
                                 float sizeMul = 1f, Color? tint = null)
        {
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            var main = ps.main;
            for (int i = 0; i < count; i++)
            {
                Vector3 r = Random.onUnitSphere;
                r.y = Mathf.Abs(r.y) * upBias + r.y * (1f - upBias);
                p.position = pos;
                p.velocity = baseVelocity + r * spread * Random.Range(0.4f, 1f);
                p.startSize = main.startSize.Evaluate(0f, Random.value) * sizeMul;
                if (tint.HasValue) p.startColor = tint.Value;
                else p.ResetStartColor();
                ps.Emit(p, 1);
            }
        }
    }
}
