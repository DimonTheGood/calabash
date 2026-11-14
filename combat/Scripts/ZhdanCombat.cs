using System.Collections.Generic;
using Calabash.Movement;
using UnityEngine;
using UnityEngine.Events;

namespace Calabash.Combat
{
    public enum AttackPhase { Idle, Charging, Windup, Active, Recovery }

    /// <summary>Тайминги и сила одного удара. Все поля — в инспекторе ZhdanCombat.</summary>
    [System.Serializable]
    public class StrikeSettings
    {
        public string displayName = "Удар";
        [Tooltip("Урон")] public float damage = 10f;
        [Tooltip("Замах до активной фазы, с")] public float windup = 0.05f;
        [Tooltip("Активная фаза — кулак бьёт, с")] public float active = 0.08f;
        [Tooltip("Откат после удара, с")] public float recovery = 0.2f;
        [Tooltip("Через сколько секунд отката можно начать следующий удар")] public float cancelAfter = 0.05f;
        [Tooltip("Дальность удара от глаз, м")] public float range = 2.2f;
        [Tooltip("Толщина удара (радиус), м")] public float radius = 0.45f;
        [Tooltip("Отбрасывание цели по направлению удара, м/с")] public float knockback = 4f;
        [Tooltip("Подброс цели вверх, м/с")] public float knockUp = 1f;
        [Tooltip("Стоп-кадр при попадании, с")] public float hitstop = 0.03f;
        [Tooltip("Тряска камеры при попадании, м")] public float shake = 0.03f;
        [Tooltip("Шаг вперёд в начале удара, м/с (добавляется к скорости героя)")] public float lunge = 0f;

        public StrikeSettings Clone() => (StrikeSettings)MemberwiseClone();
    }

    /// <summary>
    /// Рукопашная Ждана: комбо из трёх ударов, тяжёлый удар с замахом и спецприёмы от мувмента —
    /// таран (удар во время дэша), апперкот из слайда, ударная волна от граунд-слэма.
    /// Удары подкармливают топливо (ZhdanFuel). Цели — всё, что реализует IDamageable.
    /// </summary>
    [RequireComponent(typeof(ZhdanMovement))]
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    public class ZhdanCombat : MonoBehaviour
    {
        [Header("Ссылки (пусто — найдутся сами)")]
        [Tooltip("Откуда и куда бьём — шарнир камеры. Пусто — от тела на высоте глаз, по взгляду тела")]
        public Transform aimSource;
        public ZhdanInput input;
        public ZhdanFuel fuel;
        public ZhdanCameraFx cameraFx;
        [Tooltip("Что можно ударить и что закрывает цель")]
        public LayerMask hitMask = ~0;
        [Tooltip("Удар не проходит сквозь стены")]
        public bool requireLineOfSight = true;

        [Header("Комбо — ЛКМ: левой, правой, размашистый")]
        public StrikeSettings light1 = new StrikeSettings
        {
            displayName = "Джеб левой", damage = 10f, windup = 0.04f, active = 0.08f, recovery = 0.16f, cancelAfter = 0.04f,
            range = 2.2f, radius = 0.45f, knockback = 3f, knockUp = 0.5f, hitstop = 0.03f, shake = 0.03f
        };
        public StrikeSettings light2 = new StrikeSettings
        {
            displayName = "Правой", damage = 12f, windup = 0.04f, active = 0.08f, recovery = 0.18f, cancelAfter = 0.05f,
            range = 2.2f, radius = 0.45f, knockback = 4f, knockUp = 0.5f, hitstop = 0.035f, shake = 0.035f
        };
        public StrikeSettings light3 = new StrikeSettings
        {
            displayName = "Размах", damage = 22f, windup = 0.1f, active = 0.1f, recovery = 0.32f, cancelAfter = 0.18f,
            range = 2.4f, radius = 0.6f, knockback = 12f, knockUp = 3f, hitstop = 0.07f, shake = 0.08f, lunge = 5f
        };
        [Tooltip("Сколько секунд после удара следующий нажим продолжает комбо, а не начинает заново")]
        public float comboResetTime = 0.45f;
        [Tooltip("Нажатие во время удара запоминается на столько секунд")]
        public float inputBufferTime = 0.2f;

        [Header("Тяжёлый удар — ПКМ: держать — замах, отпустить — удар")]
        [Tooltip("Значения при минимальном замахе")]
        public StrikeSettings heavy = new StrikeSettings
        {
            displayName = "Кувалда", damage = 25f, windup = 0.04f, active = 0.1f, recovery = 0.4f, cancelAfter = 0.25f,
            range = 2.6f, radius = 0.6f, knockback = 8f, knockUp = 2f, hitstop = 0.06f, shake = 0.06f, lunge = 4f
        };
        [Tooltip("Урон при полном замахе")] public float heavyMaxDamage = 60f;
        [Tooltip("Отбрасывание при полном замахе, м/с")] public float heavyMaxKnockback = 22f;
        [Tooltip("Подброс при полном замахе, м/с")] public float heavyMaxKnockUp = 6f;
        [Tooltip("Стоп-кадр при полном замахе, с")] public float heavyMaxHitstop = 0.14f;
        [Tooltip("Замах короче этого — удар минимальной силы, с")] public float heavyMinChargeTime = 0.1f;
        [Tooltip("Время до полного замаха, с")] public float heavyFullChargeTime = 0.9f;
        [Tooltip("Скорость бега во время замаха (доля)")] [Range(0.1f, 1f)] public float chargeMoveSpeedMultiplier = 0.6f;

        [Header("Таран — ЛКМ во время дэша (или сразу после)")]
        public StrikeSettings dashStrike = new StrikeSettings
        {
            displayName = "Таран", damage = 35f, windup = 0f, active = 0.16f, recovery = 0.15f, cancelAfter = 0f,
            range = 1.6f, radius = 0.9f, knockback = 25f, knockUp = 4f, hitstop = 0.1f, shake = 0.12f
        };
        [Tooltip("Скорость рывка тарана, м/с (рывок длится dashStrike.active)")] public float dashStrikeSpeed = 50f;
        [Tooltip("Сколько секунд после конца дэша ещё можно ударить тараном")] public float dashStrikeGrace = 0.12f;
        [Tooltip("На первой же цели рывок останавливается с отдачей")] public bool dashStrikeStopsOnHit = true;
        [Tooltip("Отдача назад при попадании тараном, м/с")] public float dashStrikeRecoilBack = 6f;
        [Tooltip("Отдача вверх при попадании тараном, м/с")] public float dashStrikeRecoilUp = 7f;
        [Tooltip("Сколько зарядов стамины вернуть за попадание тараном")] public float dashStrikeStaminaRefund = 0.5f;

        [Header("Апперкот — ЛКМ в слайде")]
        public StrikeSettings uppercut = new StrikeSettings
        {
            displayName = "Апперкот", damage = 25f, windup = 0.03f, active = 0.1f, recovery = 0.25f, cancelAfter = 0.1f,
            range = 2.3f, radius = 0.7f, knockback = 4f, knockUp = 16f, hitstop = 0.08f, shake = 0.08f
        };
        [Tooltip("Подскок героя при апперкоте, м/с (скорость слайда сохраняется)")] public float uppercutHop = 9f;

        [Header("Удар оземь — ударная волна от граунд-слэма")]
        public bool slamShockwave = true;
        public float slamWaveRadius = 3f;
        [Tooltip("Прибавка радиуса за метр падения")] public float slamWaveRadiusPerMeter = 0.12f;
        public float slamWaveMaxRadius = 7f;
        public float slamWaveDamage = 15f;
        [Tooltip("Прибавка урона за метр падения")] public float slamWaveDamagePerMeter = 1.5f;
        public float slamWaveMaxDamage = 50f;
        public float slamWaveKnockback = 8f;
        public float slamWaveKnockUp = 10f;
        public float slamWaveHitstop = 0.06f;
        public float slamWaveShake = 0.15f;

        [Header("Топливо")]
        [Tooltip("Топлива за единицу нанесённого урона")] public float fuelPerDamage = 0.05f;
        [Tooltip("Топлива за убийство")] public float fuelPerKill = 20f;

        [Header("Стоп-кадр")]
        public bool hitstopEnabled = true;
        [Tooltip("Масштаб времени во время стоп-кадра")] public float hitstopTimeScale = 0.02f;
        public float maxHitstop = 0.2f;

        [Header("Серия")]
        [Tooltip("Серия ударов обнуляется, если столько секунд никого не бил")] public float seriesTimeout = 2f;

        [Header("События")]
        public UnityEvent<AttackKind> onAttackStart = new UnityEvent<AttackKind>();
        public UnityEvent<AttackKind> onHit = new UnityEvent<AttackKind>();
        public UnityEvent onKill = new UnityEvent();

        // ───────────── состояние ─────────────
        ZhdanMovement movement;
        bool initialized;

        AttackKind current;
        AttackPhase phase;
        float phaseTimer, phaseLength;
        StrikeSettings strike;
        Vector3 attackDir;
        int lightStep = -1;
        float comboTimer, bufferTimer, chargeTime, sinceDashEnd = 999f, seriesTimer, slamFlashTimer;
        bool heavyArmed = true, strikeLanded;
        int series;
        float hitstopUntil;
        bool hitstopActive;

        readonly HashSet<IDamageable> hitThisAttack = new HashSet<IDamageable>();
        readonly List<IDamageable> targetBuffer = new List<IDamageable>();
        readonly List<Vector3> pointBuffer = new List<Vector3>();
        static readonly Collider[] overlapBuffer = new Collider[64];

        // ───────────── публичное API ─────────────
        public AttackKind CurrentAttack => phase == AttackPhase.Idle ? AttackKind.None : current;
        public AttackPhase Phase => phase;
        /// <summary>Прогресс текущей фазы 0..1 (для анимации кулаков).</summary>
        public float PhaseProgress => phaseLength > 0f ? Mathf.Clamp01(1f - phaseTimer / phaseLength) : 1f;
        public float Charge01 => heavyFullChargeTime > heavyMinChargeTime
            ? Mathf.Clamp01((chargeTime - heavyMinChargeTime) / (heavyFullChargeTime - heavyMinChargeTime)) : 1f;
        public int Series => series;
        public string CurrentName => strike != null && phase != AttackPhase.Idle ? strike.displayName : "";
        /// <summary>Пока > 0 — только что ударили оземь (для анимации).</summary>
        public float SlamFlash => slamFlashTimer;

        void Awake() => Init();

        public void Init()
        {
            if (initialized) return;
            initialized = true;
            movement = GetComponent<ZhdanMovement>();
            movement.Init();
            if (input == null) input = GetComponent<ZhdanInput>();
            if (fuel == null) fuel = GetComponent<ZhdanFuel>();
            if (cameraFx == null) cameraFx = GetComponentInChildren<ZhdanCameraFx>();
            movement.onSlamLand.AddListener(OnSlamLand);
        }

        void OnDestroy()
        {
            if (movement != null) movement.onSlamLand.RemoveListener(OnSlamLand);
            if (hitstopActive) Time.timeScale = 1f;
        }

        void OnDisable()
        {
            if (hitstopActive) { Time.timeScale = 1f; hitstopActive = false; }
            if (movement != null) movement.speedMultiplier = 1f;
        }

        void Update()
        {
            if (hitstopActive && Time.unscaledTime >= hitstopUntil)
            {
                Time.timeScale = 1f;
                hitstopActive = false;
            }
            if (input != null) Tick(input.ReadCombat(), Time.deltaTime);
        }

        // ───────────── шаг боя ─────────────
        /// <summary>Один шаг боя. Зовётся из Update после мувмента; самотест зовёт напрямую.</summary>
        public void Tick(CombatInput inp, float dt)
        {
            Init();
            if (dt <= 0f) return;

            bool ownLunge = current == AttackKind.DashStrike && phase != AttackPhase.Idle;
            if (movement.IsDashing && !ownLunge) sinceDashEnd = 0f; else sinceDashEnd += dt;
            bufferTimer -= dt;
            comboTimer -= dt;
            slamFlashTimer -= dt;
            seriesTimer -= dt;
            if (seriesTimer <= 0f) series = 0;
            if (inp.attackPressed) bufferTimer = inputBufferTime;
            if (!inp.heavyHeld) heavyArmed = true;

            // тяжёлый удар: замах, пока держим
            if (phase == AttackPhase.Charging)
            {
                chargeTime += dt;
                movement.speedMultiplier = chargeMoveSpeedMultiplier;
                if (!inp.heavyHeld)
                {
                    movement.speedMultiplier = 1f;
                    ReleaseHeavy();
                }
                bufferTimer = 0f;
            }
            else if (inp.heavyHeld && heavyArmed && CanStartNew())
            {
                heavyArmed = false;
                current = AttackKind.Heavy;
                strike = heavy;
                phase = AttackPhase.Charging;
                phaseTimer = phaseLength = 0f;
                chargeTime = 0f;
                bufferTimer = 0f;
                movement.speedMultiplier = chargeMoveSpeedMultiplier;
            }

            // лёгкий удар или спецприём
            if (bufferTimer > 0f && CanStartNew())
            {
                bufferTimer = 0f;
                if (movement.IsDashing || sinceDashEnd <= dashStrikeGrace) StartDashStrike();
                else if (movement.IsSliding) StartUppercut();
                else StartLight();
            }

            AdvancePhase(dt);
        }

        bool CanStartNew()
        {
            if (phase == AttackPhase.Idle) return true;
            if (phase != AttackPhase.Recovery || strike == null) return false;
            return phaseLength - phaseTimer >= strike.cancelAfter;
        }

        void AdvancePhase(float dt)
        {
            switch (phase)
            {
                case AttackPhase.Windup:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0f) EnterActive();
                    break;

                case AttackPhase.Active:
                    if (current == AttackKind.DashStrike) DashStrikeHitCheck();
                    else HitCheck();
                    phaseTimer -= dt;
                    bool lungeOver = current == AttackKind.DashStrike && !movement.IsDashing;
                    if (phase == AttackPhase.Active && (phaseTimer <= 0f || lungeOver)) EnterRecovery();
                    break;

                case AttackPhase.Recovery:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0f)
                    {
                        phase = AttackPhase.Idle;
                        if (IsLight(current)) comboTimer = comboResetTime;
                    }
                    break;
            }
        }

        // ───────────── старт приёмов ─────────────
        void StartLight()
        {
            bool continues = (IsLight(current) && phase != AttackPhase.Idle) || comboTimer > 0f;
            lightStep = continues ? (lightStep + 1) % 3 : 0;
            comboTimer = 0f;
            var s = lightStep == 0 ? light1 : lightStep == 1 ? light2 : light3;
            Begin(lightStep == 0 ? AttackKind.Light1 : lightStep == 1 ? AttackKind.Light2 : AttackKind.Light3, s, AimDirection);
        }

        void ReleaseHeavy()
        {
            float t = Charge01;
            var s = heavy.Clone();
            s.damage = Mathf.Lerp(heavy.damage, heavyMaxDamage, t);
            s.knockback = Mathf.Lerp(heavy.knockback, heavyMaxKnockback, t);
            s.knockUp = Mathf.Lerp(heavy.knockUp, heavyMaxKnockUp, t);
            s.hitstop = Mathf.Lerp(heavy.hitstop, heavyMaxHitstop, t);
            s.shake = heavy.shake * (1f + t);
            lightStep = -1;
            Begin(AttackKind.Heavy, s, AimDirection);
        }

        void StartDashStrike()
        {
            Vector3 dir = movement.IsDashing ? movement.DashDirection : Flat(AimDirection);
            if (dir.sqrMagnitude < 0.0001f) dir = movement.Forward;
            dir.Normalize();
            sinceDashEnd = 999f;
            lightStep = -1;
            movement.Lunge(dir, dashStrikeSpeed, dashStrike.active);
            Begin(AttackKind.DashStrike, dashStrike, dir);
        }

        void StartUppercut()
        {
            Vector3 slideDir = Flat(movement.Velocity);
            Vector3 dir = slideDir.sqrMagnitude > 0.01f ? slideDir.normalized : Flat(AimDirection).normalized;
            lightStep = -1;
            movement.AddImpulse(Vector3.up * uppercutHop);
            Begin(AttackKind.Uppercut, uppercut, dir);
        }

        void Begin(AttackKind kind, StrikeSettings s, Vector3 dir)
        {
            current = kind;
            strike = s;
            attackDir = dir.normalized;
            hitThisAttack.Clear();
            strikeLanded = false;
            if (s.lunge > 0f) movement.AddImpulse(Flat(attackDir).normalized * s.lunge);
            onAttackStart.Invoke(kind);
            if (cameraFx != null) cameraFx.Punch(new Vector3(-1.5f, 0f, kind == AttackKind.Light1 ? 1f : kind == AttackKind.Light2 ? -1f : 0f));

            if (s.windup > 0f)
            {
                phase = AttackPhase.Windup;
                phaseTimer = phaseLength = s.windup;
            }
            else EnterActive();
        }

        void EnterActive()
        {
            phase = AttackPhase.Active;
            phaseTimer = phaseLength = Mathf.Max(0.0001f, strike.active);
            // направление лёгких/тяжёлых — по взгляду в момент удара, а не нажатия
            if (current != AttackKind.DashStrike && current != AttackKind.Uppercut) attackDir = AimDirection;
        }

        void EnterRecovery()
        {
            phase = AttackPhase.Recovery;
            phaseTimer = phaseLength = Mathf.Max(0.0001f, strike.recovery);
        }

        // ───────────── попадания ─────────────
        void HitCheck()
        {
            Vector3 origin = AimOrigin;
            Vector3 dir = current == AttackKind.Uppercut ? attackDir : AimDirection;
            GatherCapsule(origin, dir, strike.range, strike.radius);
            Vector3 kbDir = current == AttackKind.Uppercut ? Flat(attackDir).normalized : dir;
            for (int i = 0; i < targetBuffer.Count; i++)
                Apply(targetBuffer[i], pointBuffer[i], dir, kbDir * strike.knockback + Vector3.up * strike.knockUp, strike.damage, strike);
        }

        void DashStrikeHitCheck()
        {
            Vector3 origin = transform.position + Vector3.up * (movement.CurrentHeight * 0.55f);
            GatherCapsule(origin, attackDir, strike.range, strike.radius);
            for (int i = 0; i < targetBuffer.Count; i++)
            {
                Apply(targetBuffer[i], pointBuffer[i], attackDir,
                      attackDir * strike.knockback + Vector3.up * strike.knockUp, strike.damage, strike);
                if (strikeLanded && dashStrikeStopsOnHit)
                {
                    movement.SetVelocity(-attackDir * dashStrikeRecoilBack + Vector3.up * dashStrikeRecoilUp);
                    movement.RefundStamina(dashStrikeStaminaRefund);
                    EnterRecovery();
                    return;
                }
            }
        }

        void OnSlamLand(float height)
        {
            if (!slamShockwave) return;
            slamFlashTimer = 0.25f;
            onAttackStart.Invoke(AttackKind.SlamWave);
            float radius = Mathf.Min(slamWaveMaxRadius, slamWaveRadius + slamWaveRadiusPerMeter * height);
            float damage = Mathf.Min(slamWaveMaxDamage, slamWaveDamage + slamWaveDamagePerMeter * height);
            Vector3 center = transform.position + Vector3.up * 0.5f;
            if (cameraFx != null) cameraFx.AddShake(slamWaveShake);

            var wave = new StrikeSettings { displayName = "Удар оземь", hitstop = slamWaveHitstop, shake = slamWaveShake };
            hitThisAttack.Clear();
            int n = Physics.OverlapSphereNonAlloc(center, radius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            GatherFromColliders(n, center);
            AttackKind prev = current;
            current = AttackKind.SlamWave;
            for (int i = 0; i < targetBuffer.Count; i++)
            {
                Vector3 away = Flat(pointBuffer[i] - transform.position);
                away = away.sqrMagnitude > 0.0001f ? away.normalized : movement.Forward;
                Apply(targetBuffer[i], pointBuffer[i], away, away * slamWaveKnockback + Vector3.up * slamWaveKnockUp, damage, wave);
            }
            current = prev;
        }

        void GatherCapsule(Vector3 origin, Vector3 dir, float range, float radius)
        {
            Vector3 a = origin + dir * Mathf.Min(radius, range * 0.5f);
            Vector3 b = origin + dir * Mathf.Max(range - radius, range * 0.5f);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, radius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            GatherFromColliders(n, origin);
        }

        void GatherFromColliders(int n, Vector3 origin)
        {
            targetBuffer.Clear();
            pointBuffer.Clear();
            for (int i = 0; i < n; i++)
            {
                Collider col = overlapBuffer[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || hitThisAttack.Contains(target) || targetBuffer.Contains(target)) continue;
                Vector3 point = col.bounds.ClosestPoint(origin);
                if (requireLineOfSight && Blocked(origin, point, target)) continue;
                targetBuffer.Add(target);
                pointBuffer.Add(point);
            }
        }

        bool Blocked(Vector3 from, Vector3 to, IDamageable target)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.05f) return false;
            if (!Physics.Raycast(from, d / dist, out RaycastHit hit, dist, hitMask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.transform.IsChildOf(transform)) return false;
            return hit.collider.GetComponentInParent<IDamageable>() != target;
        }

        void Apply(IDamageable target, Vector3 point, Vector3 dir, Vector3 knockback, float damage, StrikeSettings s)
        {
            hitThisAttack.Add(target);
            var info = new HitInfo
            {
                damage = damage, point = point, direction = dir, knockback = knockback,
                kind = current, attacker = gameObject
            };
            HitResult r = target.TakeHit(info);
            if (r.damageDealt <= 0f) return;

            strikeLanded = true;
            series++;
            seriesTimer = seriesTimeout;
            if (fuel != null) fuel.Add(r.damageDealt * fuelPerDamage);
            Hitstop(s.hitstop);
            if (cameraFx != null) cameraFx.AddShake(s.shake);
            onHit.Invoke(current);
            if (r.killed)
            {
                if (fuel != null) fuel.Add(fuelPerKill);
                onKill.Invoke();
            }
        }

        void Hitstop(float seconds)
        {
            if (!hitstopEnabled || seconds <= 0f || !Application.isPlaying) return;
            hitstopUntil = Mathf.Max(hitstopUntil, Time.unscaledTime + Mathf.Min(seconds, maxHitstop));
            Time.timeScale = hitstopTimeScale;
            hitstopActive = true;
        }

        // ───────────── утилиты ─────────────
        Vector3 AimOrigin => aimSource != null ? aimSource.position
                                               : transform.position + Vector3.up * (movement.CurrentHeight - 0.18f);
        Vector3 AimDirection => aimSource != null ? aimSource.forward : transform.forward;

        static bool IsLight(AttackKind k) => k == AttackKind.Light1 || k == AttackKind.Light2 || k == AttackKind.Light3;
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
