using Calabash.Combat;
using Calabash.Movement;
using UnityEngine;

namespace Calabash.Vfx
{
    /// <summary>
    /// Огонь Ждана. Сила огня растёт с топливом, как в доке: стадии «Тлеющий» → «Костёр».
    /// - Тело (в третьем лице): языки пламени и угли с груди, плеч, рук и головы, дым на большом топливе,
    ///   искры следом на бегу.
    /// - Кулаки (в обоих видах): пламя на кулаках, на замахе «Кувалды» разгорается, в ударе — огненный след.
    /// - Попадание: вспышка, языки пламени, искры, клуб дыма (играют и во время стоп-кадра).
    /// - Приёмы: таран — вспышка с кулаков и огненный хвост за телом, апперкот — столб огня,
    ///   удар оземь — огненное кольцо по радиусу волны, языки по земле и выжженное пятно.
    /// Текстуры — Kenney Particle Pack и флипбуки Unity Labs (обе CC0, см. vfx/README.md).
    /// Системы частиц собираются в коде при старте; материалы приходят из ZhdanFireSetup.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public class ZhdanFireVfx : MonoBehaviour
    {
        [Header("Связи")]
        public ZhdanMovement movement;
        public ZhdanCombat combat;
        public ZhdanFuel fuel;
        public ZhdanCameraMode cameraMode;

        [Header("Кулаки от первого лица (куски FP_Fist или кубы)")]
        public Transform fpFistLeft;
        public Transform fpFistRight;
        [Tooltip("Смещение центра кулака от сустава вдоль руки, м (для FP_Fist: кусок висит вдоль −Y)")]
        public float fpFistCenter = 0.09f;

        [Header("Кости модели (третье лицо)")]
        public Transform handLeft;
        public Transform handRight;
        [Tooltip("Откуда идёт огонь с тела: грудь, плечи, предплечья, голова")]
        public Transform[] bodyPoints = new Transform[0];
        [Tooltip("Вес каждой точки тела (сколько огня с неё)")]
        public float[] bodyWeights = new float[0];
        public Transform head;

        [Header("Материалы")]
        public Material flameFlipbook;       // T_Flame03_16x4
        public Material smallFlameFlipbook;  // T_SmallFlame01_16x4
        public Material tongues;             // T_Flames_3x2
        public Material sparks;              // T_Sparks_4x2
        public Material smoke;               // T_WispySmoke01_8x8
        public Material burst;               // T_Burst_3x2
        public Material trace;               // T_Trail (след удара)
        public Material rings;               // T_Rings_2x2
        public Material scorch;              // T_Scorch_2x2

        [Header("Цвет (палитра style bible)")]
        public Color hot = new Color32(0xFF, 0xD1, 0x66, 0xFF);
        public Color ember = new Color32(0xFF, 0x7A, 0x18, 0xFF);
        public Color deep = new Color32(0xC2, 0x4E, 0x00, 0xFF);
        public Color soot = new Color32(0x3A, 0x34, 0x30, 0xFF);

        [Header("Сила")]
        [Tooltip("Огонь при пустом топливе (доля от полного)")]
        [Range(0f, 1f)] public float minHeat = 0.2f;
        [Tooltip("Если ≥ 0 — топливо для эффектов задано вручную (проверка стадий)")]
        [Range(-1f, 1f)] public float heatOverride = -1f;
        [Tooltip("Языков пламени с тела в секунду при полном топливе")]
        public float bodyFlameRate = 90f;
        [Tooltip("Углей с тела в секунду при полном топливе")]
        public float bodyEmberRate = 45f;
        [Tooltip("Пламени с кулака в секунду")]
        public float fistFlameRate = 70f;
        [Tooltip("Искр за метр бега (от стадии «Головня»)")]
        public float runSparksPerMeter = 3f;
        [Tooltip("Размер пламени на кулаках в первом и третьем лице")]
        public float fistSizeFirst = 2.8f;
        public float fistSizeThird = 3.4f;
        [Tooltip("Общий размер огненных шаров попаданий, тарана, слэма")]
        public float blastScale = 1f;

        public float Heat { get; private set; }

        ParticleSystem flames, fistFlames, embers, smokeSys;
        ParticleSystem hitBurst, hitTongues, hitSparks, hitSmoke, hitFire, ring, scorchSys;
        TrailRenderer trailL, trailR;
        float accFlame, accEmber, accSmoke, accFist, accRun, accDash, accFistEmber, accHead, accLick, accFistLick;
        Vector3 lastPos;
        bool built;
        ZhdanFireLight fireLight;
        Transform fxRoot;
        public Transform FxRoot => fxRoot;

        // ------------------------------------------------------------------ сборка
        void Awake()
        {
            if (movement == null) movement = GetComponentInParent<ZhdanMovement>();
            if (combat == null) combat = GetComponentInParent<ZhdanCombat>();
            if (fuel == null) fuel = GetComponentInParent<ZhdanFuel>();
            if (cameraMode == null) cameraMode = GetComponentInParent<ZhdanCameraMode>();
            if (fireLight == null) fireLight = GetComponent<ZhdanFireLight>();
            Build();
        }

        void OnEnable()
        {
            if (combat != null)
            {
                combat.onHit.AddListener(OnHit);
                combat.onAttackStart.AddListener(OnAttackStart);
                combat.onKill.AddListener(OnKill);
            }
            if (movement != null)
            {
                movement.onSlamLand.AddListener(OnSlamLand);
                movement.onLand.AddListener(OnLand);
                movement.onDash.AddListener(OnDash);
            }
            lastPos = transform.position;
        }

        void OnDisable()
        {
            if (combat != null)
            {
                combat.onHit.RemoveListener(OnHit);
                combat.onAttackStart.RemoveListener(OnAttackStart);
                combat.onKill.RemoveListener(OnKill);
            }
            if (movement != null)
            {
                movement.onSlamLand.RemoveListener(OnSlamLand);
                movement.onLand.RemoveListener(OnLand);
                movement.onDash.RemoveListener(OnDash);
            }
        }

        /// <summary>Собрать системы частиц (в игре — само в Awake).</summary>
        public void Build()
        {
            if (built) return;
            built = true;
            var root = new GameObject("FireFX").transform;          // в мире: частицы не тянутся за героем
            root.SetParent(null, false);
            root.gameObject.hideFlags = HideFlags.DontSave;
            fxRoot = root;
            const ParticleSystemSimulationSpace W = ParticleSystemSimulationSpace.World;
            Color[] fireCols = { hot, ember, deep };
            float[] fireT = { 0f, 0.2f, 1f };

            // языки пламени с тела — флипбук
            flames = FireFx.Create("BodyFlames", root, flameFlipbook, 400);
            FireFx.Main(flames, new Vector2(0.45f, 0.8f), Vector2.zero, new Vector2(0.4f, 0.75f), -0.05f, W);
            FireFx.Sheet(flames, 16, 4, true);
            FireFx.Colors(flames, fireCols, fireT, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, 0.1f, 0.6f, 1f });
            FireFx.Size(flames, 0.55f, 1.1f, 0.6f);
            FireFx.Rise(flames, 1.5f);
            FireFx.Noise(flames, 0.35f);

            // пламя на кулаках — мелкий флипбук
            fistFlames = FireFx.Create("FistFlames", root, smallFlameFlipbook, 300);
            FireFx.Main(fistFlames, new Vector2(0.18f, 0.32f), Vector2.zero, new Vector2(0.07f, 0.13f), 0f, W);
            FireFx.Sheet(fistFlames, 16, 4, true);
            FireFx.Colors(fistFlames, fireCols, fireT, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, 0.1f, 0.6f, 1f });
            FireFx.Size(fistFlames, 0.7f, 1f, 0.3f);
            FireFx.Rise(fistFlames, 0.9f);

            // угли и искры
            embers = FireFx.Create("Embers", root, sparks, 400);
            FireFx.Main(embers, new Vector2(0.5f, 1.2f), Vector2.zero, new Vector2(0.025f, 0.06f), -0.1f, W);
            FireFx.RandomRotation(embers);
            FireFx.Sheet(embers, 4, 2, false);
            FireFx.Colors(embers, new[] { hot, ember, deep }, new[] { 0f, 0.5f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, 0.7f, 1f });
            FireFx.Noise(embers, 0.6f, 2f);
            FireFx.Drag(embers, 0.6f);

            // дым
            smokeSys = FireFx.Create("Smoke", root, smoke, 160);
            FireFx.Main(smokeSys, new Vector2(0.9f, 1.4f), Vector2.zero, new Vector2(0.5f, 1f), -0.03f, W);
            FireFx.RandomRotation(smokeSys);
            FireFx.Sheet(smokeSys, 8, 8, true);
            FireFx.Colors(smokeSys, new[] { soot, soot }, new[] { 0f, 1f }, new[] { 0f, 0.35f, 0f }, new[] { 0f, 0.2f, 1f });
            FireFx.Size(smokeSys, 0.6f, 1.4f, 2f);
            FireFx.Rise(smokeSys, 0.8f);
            FireFx.Drag(smokeSys, 1.5f);

            // попадание: играет в реальном времени, чтобы было видно во время стоп-кадра
            hitBurst = FireFx.Create("HitBurst", root, burst, 30);
            FireFx.Main(hitBurst, new Vector2(0.09f, 0.14f), Vector2.zero, new Vector2(1.1f, 1.5f), 0f, W, null, true);
            FireFx.RandomRotation(hitBurst);
            FireFx.Sheet(hitBurst, 3, 2, false);
            FireFx.Colors(hitBurst, new[] { hot, ember }, new[] { 0f, 0.3f }, new[] { 1f, 0f }, new[] { 0.4f, 1f });
            FireFx.Size(hitBurst, 0.6f, 1.3f);

            hitTongues = FireFx.Create("HitTongues", root, tongues, 300);
            FireFx.Main(hitTongues, new Vector2(0.25f, 0.45f), Vector2.zero, new Vector2(0.35f, 0.65f), -0.2f, W, null, true);
            FireFx.Sheet(hitTongues, 3, 2, false);
            FireFx.Colors(hitTongues, fireCols, fireT, new[] { 1f, 0.9f, 0f }, new[] { 0f, 0.5f, 1f });
            FireFx.Size(hitTongues, 0.8f, 1.2f, 0.4f);
            FireFx.Drag(hitTongues, 4f);

            // огненный шар: крупные флипбуки пламени, разлетаются и тормозят
            hitFire = FireFx.Create("HitFire", root, flameFlipbook, 400);
            FireFx.Main(hitFire, new Vector2(0.45f, 0.75f), Vector2.zero, new Vector2(0.7f, 1.2f), -0.1f, W, null, true);
            FireFx.Sheet(hitFire, 16, 4, true);
            FireFx.Colors(hitFire, fireCols, fireT, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, 0.06f, 0.6f, 1f });
            FireFx.Size(hitFire, 0.5f, 1.15f, 0.8f);
            FireFx.Rise(hitFire, 2f);
            FireFx.SlowDown(hitFire);

            hitSparks = FireFx.Create("HitSparks", root, sparks, 400);
            FireFx.Main(hitSparks, new Vector2(0.25f, 0.6f), Vector2.zero, new Vector2(0.03f, 0.07f), 1.2f, W, null, true);
            FireFx.Sheet(hitSparks, 4, 2, false);
            FireFx.Colors(hitSparks, new[] { hot, ember }, new[] { 0f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, 0.6f, 1f });
            FireFx.Stretch(hitSparks, 0.04f, 2f);
            FireFx.Drag(hitSparks, 1.5f);

            hitSmoke = FireFx.Create("HitSmoke", root, smoke, 80);
            FireFx.Main(hitSmoke, new Vector2(0.8f, 1.4f), Vector2.zero, new Vector2(0.6f, 1f), -0.02f, W, null, true);
            FireFx.RandomRotation(hitSmoke);
            FireFx.Sheet(hitSmoke, 8, 8, true);
            FireFx.Colors(hitSmoke, new[] { soot, soot }, new[] { 0f, 1f }, new[] { 0f, 0.45f, 0f }, new[] { 0f, 0.15f, 1f });
            FireFx.Size(hitSmoke, 0.5f, 1.3f, 1.8f);
            FireFx.Drag(hitSmoke, 3f);

            // удар оземь: кольцо и выжженное пятно лежат на земле
            ring = FireFx.Create("SlamRing", root, rings, 10);
            FireFx.Main(ring, new Vector2(0.4f, 0.4f), Vector2.zero, Vector2.one, 0f, W, null, true);
            FireFx.RandomRotation(ring);
            FireFx.Sheet(ring, 2, 2, false);
            FireFx.Colors(ring, new[] { hot, ember, deep }, fireT, new[] { 1f, 0.8f, 0f }, new[] { 0f, 0.5f, 1f });
            FireFx.Size(ring, 0.15f, 0.85f, 1f);
            FireFx.Flat(ring);
            var rs = ring.textureSheetAnimation; rs.startFrame = new ParticleSystem.MinMaxCurve(0f, 2.99f);

            scorchSys = FireFx.Create("Scorch", root, scorch, 12);
            FireFx.Main(scorchSys, new Vector2(7f, 7f), Vector2.zero, Vector2.one, 0f, W);
            FireFx.Sheet(scorchSys, 2, 2, false);
            var ss = scorchSys.textureSheetAnimation; ss.startFrame = new ParticleSystem.MinMaxCurve(0f, 2.99f);
            FireFx.Colors(scorchSys, new[] { soot, soot }, new[] { 0f, 1f }, new[] { 0f, 0.9f, 0.9f, 0f }, new[] { 0f, 0.03f, 0.7f, 1f });
            FireFx.Flat(scorchSys);
            scorchSys.GetComponent<ParticleSystemRenderer>().sortingFudge = 10f;   // позади огня

            foreach (var ps in new[] { flames, fistFlames, embers, smokeSys, hitBurst, hitTongues, hitFire, hitSparks, hitSmoke, ring, scorchSys })
                ps.Play();

            trailL = MakeTrail("TrailL", root);
            trailR = MakeTrail("TrailR", root);
        }

        TrailRenderer MakeTrail(string name, Transform root)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var t = go.AddComponent<TrailRenderer>();
            t.sharedMaterial = trace;
            t.time = 0.12f;
            t.minVertexDistance = 0.02f;
            t.textureMode = LineTextureMode.Stretch;
            t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.receiveShadows = false;
            t.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(hot, 0f), new GradientColorKey(ember, 0.4f), new GradientColorKey(deep, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            t.colorGradient = g;
            t.emitting = false;
            return t;
        }

        void OnDestroy()
        {
            if (fxRoot == null) return;
            if (Application.isPlaying) Destroy(fxRoot.gameObject);
            else DestroyImmediate(fxRoot.gameObject);
        }

        // ------------------------------------------------------------------ каждый кадр
        bool ThirdPerson => cameraMode != null && cameraMode.Blend > 0.5f;

        Vector3 FistPos(bool right, out Vector3 along)
        {
            along = transform.forward;
            if (ThirdPerson)
            {
                Transform h = right ? handRight : handLeft;
                if (h != null)
                {
                    along = -h.up;
                    return h.position;
                }
            }
            Transform f = right ? fpFistRight : fpFistLeft;
            if (f == null) return transform.position + Vector3.up;
            along = -f.up;
            return f.position + along * fpFistCenter * Mathf.Abs(f.lossyScale.y);
        }

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Шаг эффектов: топливо, тело, кулаки, следы.</summary>
        public void Tick(float dt)
        {
            if (!built) return;
            float f01 = heatOverride >= 0f ? heatOverride : fuel != null ? fuel.Fuel01 : 1f;
            Heat = Mathf.Lerp(minHeat, 1f, f01);
            float tp = cameraMode != null ? cameraMode.Blend : 1f;
            Vector3 vel = movement != null ? movement.Velocity : Vector3.zero;

            AttackKind atk = combat != null ? combat.CurrentAttack : AttackKind.None;
            AttackPhase phase = combat != null ? combat.Phase : AttackPhase.Idle;
            float charge = combat != null && phase == AttackPhase.Charging ? combat.Charge01 : 0f;
            bool dashing = movement != null && movement.IsDashing;

            // --- тело (третье лицо)
            if (tp > 0.01f && bodyPoints.Length > 0)
            {
                float h2 = Heat * Heat;
                Emit(flames, ref accFlame, bodyFlameRate * h2 * tp * dt, () => BodyPoint(0.12f), vel * 0.9f, 0.4f, 1f);
                Emit(hitTongues, ref accLick, bodyFlameRate * 0.35f * h2 * tp * dt, () => BodyPoint(0.12f), vel * 0.9f + Vector3.up * 2.5f, 0.6f, 0.9f);
                Emit(embers, ref accEmber, bodyEmberRate * Heat * tp * dt, () => BodyPoint(0.1f), vel * 0.6f + Vector3.up * 1.2f, 1.2f, 1f);
                if (Heat > 0.75f)                                                         // «Костёр»: силуэт дымит
                    Emit(smokeSys, ref accSmoke, 7f * (Heat - 0.7f) / 0.3f * tp * dt, () => BodyPoint(0.15f) + Vector3.up * 0.25f, vel * 0.5f, 0.3f, 1f);
                if (head != null && Heat > 0.6f)                                          // огонь в глазницах и во рту
                    Emit(flames, ref accHead, 18f * Heat * tp * dt, () => head.position + head.up * 0.12f + Random.insideUnitSphere * 0.06f, vel * 0.95f, 0.2f, 0.7f);
            }

            // --- искры следом на бегу (стадия «Головня» и выше)
            Vector3 moved = transform.position - lastPos;
            lastPos = transform.position;
            float dist = moved.magnitude;
            if (dist > 5f) dist = 0f;                                                     // телепорт / респаун
            if (Heat > 0.35f && movement != null && movement.IsGrounded && tp > 0.5f)
                Emit(embers, ref accRun, runSparksPerMeter * (Heat - 0.3f) * dist, () => transform.position + Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 0.2f,
                     Vector3.up * 1.5f, 1.5f, 1f);

            // --- огненный хвост в дэше и таране
            if ((dashing || atk == AttackKind.DashStrike) && tp > 0.3f)
                Emit(hitFire, ref accDash, 5f * dist, () => transform.position + Vector3.up * Random.Range(0.4f, 1.5f) + Random.insideUnitSphere * 0.3f,
                     -vel.normalized * 2f, 1.5f, blastScale);

            // --- кулаки
            float fistRate = fistFlameRate * Mathf.Lerp(0.5f, 1f, Heat) * (1f + 4f * charge) * (dashing ? 2.5f : 1f);
            float fistSize = (ThirdPerson ? fistSizeThird : fistSizeFirst) * (1f + 0.9f * charge);
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;
                Vector3 p = FistPos(right, out Vector3 along);
                bool striking = phase == AttackPhase.Active && UsesFist(atk, right);
                float r = fistRate * (striking ? 3f : 1f) * dt;
                // флипбук стоит на нижней части кадра: опускаем точку, чтобы пламя обнимало кулак, а не висело над ним
                Vector3 baseP = p - Vector3.up * 0.035f * fistSize;
                Emit(fistFlames, ref accFist, r, () => baseP + Random.insideUnitSphere * 0.03f * fistSize, vel, 0.25f, fistSize);
                Emit(hitTongues, ref accFistLick, r * 0.3f, () => baseP + Random.insideUnitSphere * 0.03f * fistSize, vel + Vector3.up * 1.8f, 0.4f, 0.3f * fistSize);
                if (charge > 0.2f || striking)
                    Emit(embers, ref accFistEmber, (20f + 60f * charge) * dt, () => p + Random.insideUnitSphere * 0.06f, vel + Vector3.up * 0.8f, 1.5f, 0.8f);

                TrailRenderer tr = right ? trailR : trailL;
                tr.widthMultiplier = ThirdPerson ? 0.5f : 0.24f;
                bool wasEmitting = tr.emitting;
                tr.emitting = striking || (atk == AttackKind.Heavy && phase == AttackPhase.Recovery && combat.PhaseProgress < 0.25f);
                if (!wasEmitting && tr.emitting) tr.Clear();
                tr.transform.position = p;
            }
        }

        static bool UsesFist(AttackKind k, bool right)
        {
            switch (k)
            {
                case AttackKind.Light1: return !right;
                case AttackKind.DashStrike: return true;
                case AttackKind.None: return false;
                default: return right;
            }
        }

        Vector3 BodyPoint(float jitter)
        {
            float total = 0f;
            for (int i = 0; i < bodyPoints.Length; i++) total += W(i);
            float pick = Random.value * total;
            for (int i = 0; i < bodyPoints.Length; i++)
            {
                pick -= W(i);
                if (pick <= 0f && bodyPoints[i] != null) return bodyPoints[i].position + Random.insideUnitSphere * jitter;
            }
            return transform.position + Vector3.up * 1.2f;
        }

        float W(int i) => i < bodyWeights.Length ? bodyWeights[i] : 1f;

        /// <summary>Накопитель: выпускает целые частицы по дробной частоте.</summary>
        static void Emit(ParticleSystem ps, ref float acc, float amount, System.Func<Vector3> at, Vector3 vel, float spread, float sizeMul)
        {
            acc += amount;
            if (acc < 1f) return;
            int n = Mathf.Min(64, (int)acc);
            acc -= n;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            var size = ps.main.startSize;
            for (int i = 0; i < n; i++)
            {
                p.position = at();
                p.velocity = vel + Random.insideUnitSphere * spread;
                p.startSize = size.Evaluate(0f, Random.value) * sizeMul;
                ps.Emit(p, 1);
            }
        }

        // ------------------------------------------------------------------ события
        void OnHit(AttackKind kind)
        {
            Vector3 p = combat.LastHitPoint;
            Vector3 d = combat.LastHitDirection;
            float k = Strength(kind) * Mathf.Lerp(0.7f, 1f, Heat);
            Impact(p, d, k);
        }

        void OnKill()
        {
            if (fireLight != null) fireLight.Flare(1.3f);
            Vector3 p = combat.LastHitPoint;
            FireFx.Burst(hitFire, p, 18, Vector3.up * 6f, 5f, 0.8f, 1.4f * blastScale);                 // столб огня
            FireFx.Burst(hitTongues, p, 20, Vector3.up * 5f, 6f, 0.7f, 1.5f);
            FireFx.Burst(hitSparks, p, 50, Vector3.up * 3f, 10f, 0.5f);
            FireFx.Burst(hitSmoke, p + Vector3.up * 0.5f, 6, Vector3.up * 1.5f, 1.2f, 1f, 1.6f);
        }

        static float Strength(AttackKind k)
        {
            switch (k)
            {
                case AttackKind.Light1: case AttackKind.Light2: return 0.7f;
                case AttackKind.Light3: return 1f;
                case AttackKind.Heavy: return 1.6f;
                case AttackKind.DashStrike: return 1.5f;
                case AttackKind.Uppercut: return 1.2f;
                case AttackKind.SlamWave: return 0.6f;
                default: return 1f;
            }
        }

        /// <summary>Вспышка огня в точке: годится и для своих врагов (например, огненный снаряд).</summary>
        public void Impact(Vector3 p, Vector3 dir, float k)
        {
            if (!built) return;
            if (fireLight != null) fireLight.Flare(k);
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
            float b = k * blastScale;
            // у самой камеры (кулак от первого лица) вспышка не должна закрывать полэкрана
            var cam = Camera.main;
            if (cam != null)
            {
                float cd = Vector3.Distance(cam.transform.position, p);
                if (cd < 2.2f) b *= Mathf.Lerp(0.35f, 1f, cd / 2.2f);
            }
            FireFx.Burst(hitBurst, p, 1, Vector3.zero, 0f, 0f, b);
            FireFx.Burst(hitFire, p, Mathf.RoundToInt(10 * k), dir * 4f * k, 6f * b, 0.4f, b);           // огненный шар
            FireFx.Burst(hitTongues, p, Mathf.RoundToInt(14 * k), dir * 3f * k, 8f * b, 0.3f, Mathf.Sqrt(b) * 1.3f);
            FireFx.Burst(hitSparks, p, Mathf.RoundToInt(30 * k), dir * 6f, 11f * k, 0.4f);
            FireFx.Burst(hitSmoke, p, Mathf.Max(1, Mathf.RoundToInt(3 * k)), dir * 0.8f + Vector3.up * 0.8f, 0.8f, 1f, Mathf.Sqrt(b) * 1.4f);
        }

        void OnAttackStart(AttackKind kind)
        {
            if (kind == AttackKind.Uppercut)
            {
                Vector3 p = FistPos(true, out _);
                FireFx.Burst(hitFire, p, 14, Vector3.up * 12f, 2.5f, 0.9f, 1.1f * blastScale);            // огненный столб
                FireFx.Burst(hitTongues, p, 16, Vector3.up * 10f, 3f, 0.9f, 1.4f);
                FireFx.Burst(hitSparks, p, 26, Vector3.up * 9f, 4f, 0.8f);
            }
            else if (kind == AttackKind.DashStrike)
            {
                for (int s = 0; s < 2; s++)
                {
                    Vector3 p = FistPos(s == 1, out _);
                    FireFx.Burst(hitBurst, p, 1, Vector3.zero, 0f, 0f, ThirdPerson ? 1f : 0.45f);
                    FireFx.Burst(hitSparks, p, 10, (movement != null ? movement.Velocity : Vector3.zero), 3f, 0.3f);
                }
            }
        }

        void OnDash()
        {
            if (!ThirdPerson) return;
            Vector3 p = transform.position + Vector3.up * 1f;
            FireFx.Burst(hitFire, p, 8, -transform.forward * 3f, 2.5f, 0.2f, blastScale);
        }

        void OnLand(float height)
        {
            if (height < 3f) return;
            float k = Mathf.Clamp01((height - 3f) / 10f);
            FireFx.Burst(hitSparks, transform.position + Vector3.up * 0.1f, Mathf.RoundToInt(6 + 20 * k), Vector3.up * 2f, 4f, 0.8f);
        }

        void OnSlamLand(float height) => SlamFx(height);

        /// <summary>Огонь удара оземь: кольцо по радиусу волны, языки по земле, искры, дым, выжженное пятно.</summary>
        public void SlamFx(float height)
        {
            if (fireLight != null) fireLight.Flare(1.6f);
            float radius = combat != null ? combat.SlamWaveRadius(height) : 3f;
            Vector3 c = transform.position;
            // кольцо по радиусу волны
            var p = new ParticleSystem.EmitParams { position = c + Vector3.up * 0.08f, velocity = Vector3.zero, startSize = radius * 2.2f, applyShapeToPosition = false };
            ring.Emit(p, 1);
            // огненная волна бежит по земле до края удара и встаёт стеной
            int n = Mathf.RoundToInt(24 + radius * 7f);
            var t = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.7f) / n * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                t.position = c + d * 0.4f + Vector3.up * 0.3f;
                t.velocity = d * radius * Random.Range(3f, 3.6f) + Vector3.up * Random.Range(0.5f, 2f);    // встаёт у края волны
                t.startSize = Random.Range(0.9f, 1.5f) * blastScale;
                hitFire.Emit(t, 1);
                t.position = c + d * 0.3f + Vector3.up * 0.2f;
                t.velocity = d * radius * Random.Range(2.5f, 3.5f) + Vector3.up * Random.Range(1f, 3f);
                t.startSize = Random.Range(0.5f, 0.9f);
                hitTongues.Emit(t, 1);
            }
            FireFx.Burst(hitFire, c + Vector3.up * 0.5f, 10, Vector3.up * 7f, 2f, 0.9f, 1.3f * blastScale);  // столб в центре
            FireFx.Burst(hitSparks, c + Vector3.up * 0.2f, Mathf.RoundToInt(30 + radius * 6f), Vector3.up * 4f, 9f, 0.7f);
            FireFx.Burst(hitSmoke, c + Vector3.up * 0.4f, 6, Vector3.up * 1.5f, 2.5f, 0.6f, 1.5f);
            // выжженное пятно
            var s = new ParticleSystem.EmitParams { position = c + Vector3.up * 0.03f, velocity = Vector3.zero, startSize = radius * 1.3f, applyShapeToPosition = false };
            s.rotation = Random.Range(0f, 360f);
            scorchSys.Emit(s, 1);
        }
    }
}
