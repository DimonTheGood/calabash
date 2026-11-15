using System.Collections.Generic;
using Calabash.Audio;
using Calabash.Combat;
using UnityEngine;

namespace Calabash.Enemies
{
    public enum EnemyKind
    {
        [Tooltip("стражник: удар копьём сверху, с шагом вперёд")] Spearman,
        [Tooltip("крестьянин с вилами: быстрый прыжок-выпад, слабый, их много")] Pitchfork,
        [Tooltip("стражник с вёдрами: держит дистанцию, кидает воду дугой")] Bucket,
        [Tooltip("лучник: держит дистанцию, целится, стреляет")] Archer,
        [Tooltip("палач Корней: удар оземь кругом и таран, не сбивается лёгкими ударами")] Executioner,
    }

    /// <summary>
    /// Враг: бежит к Ждану, обходит дома, телеграфирует удар и бьёт. У каждого вида своя атака (EnemyKind).
    /// Ближний бой — по жетонам WaveDirector: одновременно бьют не все, остальные кружат рядом.
    /// Принимает удары (IDamageable): вздрагивает и прерывает замах (у палача — только от сильного удара),
    /// отлетает, умирает, уходит в снег. Движение кинематическое, как у VillagerNpc.
    /// Клипы Animator: Idle, Walk, Run, Hit, Death + Chop, Thrust, Throw, Shoot (animation/).
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class Enemy : MonoBehaviour, IDamageable
    {
        public static readonly List<Enemy> All = new List<Enemy>();
        public static event System.Action<Enemy> Died;

        public EnemyKind kind;
        public Animator animator;

        [Header("Тело")]
        public float maxHealth = 80f;
        public float mass = 1.5f;
        [Tooltip("Удар слабее этого не прерывает атаку (палач). 0 — прерывает любой")]
        public float poise = 0f;
        public float hitStun = 0.45f;

        [Header("Движение")]
        public float walkSpeed = 1.8f;
        public float runSpeed = 4.6f;
        public float turnSpeed = 480f;
        public float gravity = 25f;
        public float groundFriction = 7f;

        [Header("Атака")]
        public string attackClip = "Chop";
        [Tooltip("Доля клипа, на которой удар (конец замаха)")] public float clipStrikeAt = 0.4f;
        public float attackRange = 2.6f;
        [Tooltip("Дальние: держатся в этом коридоре, м")] public Vector2 keepDistance = new Vector2(7f, 16f);
        public float windup = 0.6f;
        public float recovery = 0.6f;
        public Vector2 cooldown = new Vector2(1.2f, 2.2f);
        public float damage = 12f;
        public float knockback = 6f;
        [Tooltip("Рывок вперёд в момент удара, м/с")] public float lunge = 4f;
        [Tooltip("Ширина удара, м")] public float hitWidth = 1.2f;

        [Header("Палач: таран")]
        public float chargeSpeed = 13f;
        public float chargeTime = 1.1f;
        public float chargeDamage = 22f;
        public float chargeMinDistance = 8f;

        [Header("Снаряды")]
        public float projectileSpeed = 38f;
        public float bucketFlightTime = 0.9f;
        public float splashRadius = 1.8f;

        [Header("Поджог")]
        [Tooltip("Сколько горит после поджога, с")] public float burnTime = 3.5f;
        [Tooltip("Урон огнём в секунду")] public float burnDps = 9f;

        [Header("Материалы (ставит EnemySetup)")]
        public Material burnMaterial;
        public Material telegraphMaterial;
        public Material arrowMaterial;
        public Material waterMaterial;

        enum State { Spawn, Chase, Hold, Windup, Strike, Charge, Recover, Stun, Dead }

        public float Health => health;
        public bool IsAlive => health > 0f;
        public bool IsMelee => kind == EnemyKind.Spearman || kind == EnemyKind.Pitchfork || kind == EnemyKind.Executioner;
        public string StateName => state.ToString();
        /// <summary>Сколько секунд назад враг получил удар (для полоски здоровья в HUD).</summary>
        public float SinceHit => Time.time - lastHitTime;

        float health;
        State state;
        float timer, cooldownTimer, strafeSign = 1f, repathTimer, chargeCooldown;
        bool hasToken, charging, smash;
        Vector3 velocity, aimPoint, lungeDir;
        bool grounded = true;
        float lastHitTime = -10f;
        float burnTimer;
        ParticleSystem burnFx;
        /// <summary>Горит ли сейчас.</summary>
        public bool Burning => burnTimer > 0f;
        string anim;
        Telegraph telegraph;
        CapsuleCollider capsule;
        Vector3 avoidDir;
        float avoidTimer;
        float stuckCheck, stuckTime, stuckDist = 999f;
        readonly Dictionary<string, float> clipLength = new Dictionary<string, float>();
        static readonly RaycastHit[] rayHits = new RaycastHit[12];

        void Awake()
        {
            health = maxHealth;
            capsule = GetComponent<CapsuleCollider>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
                foreach (var c in animator.runtimeAnimatorController.animationClips) clipLength[c.name] = c.length;
            telegraph = Telegraph.Create(transform, telegraphMaterial);
            strafeSign = Random.value < 0.5f ? -1f : 1f;
        }

        void OnEnable() => All.Add(this);
        void OnDisable()
        {
            All.Remove(this);
            ReleaseToken();
        }

        void Start()
        {
            PlayerTarget.Find();
            state = State.Spawn;
            timer = Random.Range(0.3f, 0.8f);
            cooldownTimer = Random.Range(0.5f, 1.5f);
            chargeCooldown = 3f;
            Play("Idle");
        }

        // ------------------------------------------------------------------ удары по врагу
        public HitResult TakeHit(in HitInfo hit)
        {
            if (!IsAlive) return default;
            float dealt = Mathf.Min(health, hit.damage);
            health -= dealt;
            lastHitTime = Time.time;
            bool heavyHit = poise <= 0f || hit.damage >= poise || hit.kind == AttackKind.Uppercut;
            velocity += hit.knockback / Mathf.Max(0.2f, mass) * (heavyHit ? 1f : 0.25f);
            if (velocity.y > 0.5f) grounded = false;

            if (health <= 0f)
            {
                Die(hit);
                return new HitResult { damageDealt = dealt, killed = true };
            }
            bool fiery = hit.kind == AttackKind.DashStrike || hit.kind == AttackKind.Uppercut || hit.kind == AttackKind.SlamWave
                         || (hit.kind == AttackKind.Heavy && hit.damage >= 35f);
            if (fiery || PlayerTarget.igniteAll) Ignite(burnTime);
            if (heavyHit)
            {
                CancelAttack();
                state = State.Stun; timer = hitStun;
                Vector3 from = -hit.direction; from.y = 0f;
                if (from.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(from.normalized);
                Play("Hit", 0.05f, restart: true);
                if (Random.value < 0.45f) CalabashSfx.Play("grunt", transform.position + Vector3.up * 1.5f, 0.8f, kind == EnemyKind.Executioner ? 0.75f : 1f);
            }
            else CalabashSfx.Play("metal_clank", transform.position + Vector3.up * 1.2f, 0.5f, 0.8f);   // палач держит удар
            return new HitResult { damageDealt = dealt, killed = false };
        }

        /// <summary>Поджечь: горит seconds секунд, теряет здоровье, на теле пламя.</summary>
        public void Ignite(float seconds)
        {
            if (!IsAlive) return;
            if (burnTimer <= 0f) CalabashSfx.Play("fire_burst", transform.position + Vector3.up, 0.6f, 1.2f);
            burnTimer = Mathf.Max(burnTimer, seconds);
            if (burnFx == null) burnFx = MakeBurnFx();
            if (burnFx != null && !burnFx.isPlaying) burnFx.Play();
        }

        void BurnTick(float dt)
        {
            if (burnTimer <= 0f) return;
            burnTimer -= dt;
            if (burnTimer <= 0f && burnFx != null) burnFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (!IsAlive) return;
            health -= burnDps * dt;
            if (health <= 0f)
            {
                health = 0f;
                if (PlayerTarget.Fuel != null) PlayerTarget.Fuel.Add(PlayerTarget.fuelPerBurnKill);
                Die(default);
            }
        }

        ParticleSystem MakeBurnFx()
        {
            if (burnMaterial == null) return null;
            var go = new GameObject("Burn");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.9f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0xFF, 0xD1, 0x66, 0xFF), new Color32(0xFF, 0x7A, 0x18, 0xFF));
            main.gravityModifier = -0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var em = ps.emission; em.rateOverTime = 40f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.5f, 1.4f, 0.4f);
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true; tsa.numTilesX = 16; tsa.numTilesY = 4;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = burnMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        void KillBlast()
        {
            float dmg = PlayerTarget.killBlastDamage;
            if (dmg <= 0f) return;
            Vector3 c = transform.position + Vector3.up;
            if (PlayerTarget.Fx != null) PlayerTarget.Fx.Impact(c, Vector3.up, 1.4f);
            CalabashSfx.Play("explosion", c, 0.7f, 1.1f);
            foreach (var e in All.ToArray())
            {
                if (e == this || !e.IsAlive) continue;
                Vector3 d = e.transform.position - transform.position;
                if (d.sqrMagnitude > 9f) continue;
                Vector3 dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
                e.TakeHit(new HitInfo { damage = dmg, point = e.transform.position + Vector3.up, direction = dir,
                                        knockback = dir * 8f + Vector3.up * 4f, kind = AttackKind.SlamWave, attacker = gameObject });
            }
        }

        void Die(in HitInfo hit)
        {
            if (state == State.Dead) return;
            CancelAttack();
            state = State.Dead;
            timer = 4f;
            Play("Death", 0.06f);
            capsule.enabled = false;
            CalabashSfx.Play("death", transform.position + Vector3.up * 1.5f, 1f, kind == EnemyKind.Executioner ? 0.7f : Random.Range(0.92f, 1.08f));
            All.Remove(this);
            Died?.Invoke(this);
            KillBlast();
        }

        // ------------------------------------------------------------------ поведение
        void Update()
        {
            float dt = Time.deltaTime;
            timer -= dt;
            cooldownTimer -= dt;
            chargeCooldown -= dt;
            repathTimer -= dt;
            Vector3 move = Vector3.zero;
            BurnTick(dt);
            if (state == State.Dead && burnFx != null && burnTimer <= 0f && timer < 2f) burnFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            bool playerOk = PlayerTarget.Alive;
            Vector3 toPlayer = Flat(PlayerTarget.Position - transform.position);
            float dist = toPlayer.magnitude;

            switch (state)
            {
                case State.Spawn:
                    if (timer <= 0f) state = State.Chase;
                    break;

                case State.Chase:
                case State.Hold:
                    if (!playerOk) { Play("Idle"); break; }
                    move = Think(toPlayer, dist, dt);
                    Unstick(dist, dt);
                    break;

                case State.Windup:
                    WindupTick(toPlayer, dt);
                    break;

                case State.Strike:
                    if (lunge > 0f && timer > 0f) move = lungeDir * lunge;
                    if (timer <= 0f) { state = State.Recover; timer = recovery; }
                    break;

                case State.Charge:
                    move = lungeDir * chargeSpeed;
                    ChargeTick();
                    if (timer <= 0f || Blocked(lungeDir, 0.8f)) EndCharge();
                    break;

                case State.Recover:
                    if (timer <= 0f)
                    {
                        ReleaseToken();
                        cooldownTimer = Random.Range(cooldown.x, cooldown.y);
                        state = State.Chase;
                    }
                    break;

                case State.Stun:
                    if (timer <= 0f && grounded) state = State.Chase;
                    break;

                case State.Dead:
                    if (timer <= 0f)
                    {
                        transform.position += Vector3.down * dt * 0.6f;          // уходит в снег
                        if (timer < -2.5f) Destroy(gameObject);
                    }
                    break;
            }
            if (state != State.Dead) Simulate(move, dt);
        }

        /// <summary>
        /// Застрял за домом: сначала пробует обойти в случайную сторону, потом выходит из-за угла
        /// на другой улице (WaveDirector.Relocate), если Ждан этого не видит.
        /// </summary>
        void Unstick(float dist, float dt)
        {
            stuckCheck -= dt;
            if (stuckCheck > 0f) return;
            stuckCheck = 1f;
            // застрял = далеко и за секунду не стал ближе к Ждану хотя бы на полметра
            bool far = dist > attackRange + 1.5f && (IsMelee || dist > keepDistance.y);
            if (far && dist > stuckDist - 0.5f) stuckTime += 1f;
            else stuckTime = 0f;
            stuckDist = dist;
            if (stuckTime >= 2f && avoidTimer <= 0f)
            {
                Vector3 to = Flat(PlayerTarget.Position - transform.position).normalized;
                avoidDir = Quaternion.Euler(0f, Random.Range(60f, 120f) * (Random.value < 0.5f ? -1f : 1f), 0f) * to;
                avoidTimer = 1.5f;
            }
            if (stuckTime >= 5f && WaveDirector.Instance != null && WaveDirector.Instance.Relocate(this)) { stuckTime = 0f; stuckDist = 999f; }
        }

        /// <summary>Выбор: подойти, держать дистанцию, кружить или начать атаку.</summary>
        Vector3 Think(Vector3 toPlayer, float dist, float dt)
        {
            Vector3 dir = dist > 0.01f ? toPlayer / dist : transform.forward;

            if (kind == EnemyKind.Executioner && dist > chargeMinDistance && dist < 22f && chargeCooldown <= 0f && SeesPlayer())
            {
                BeginWindup(charge: true);
                return Vector3.zero;
            }

            if (IsMelee)
            {
                if (dist <= attackRange && cooldownTimer <= 0f && TryToken())
                {
                    BeginWindup(charge: false);
                    return Vector3.zero;
                }
                // нет жетона — кружит рядом и ждёт, иначе бежит к Ждану
                if (!hasToken && dist < 5f && WaveDirector.MeleeSlotsFull)
                {
                    state = State.Hold;
                    Play("Walk");
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * strafeSign;
                    Vector3 want = side + dir * (dist - 3.8f) * 0.6f;
                    if (Random.value < dt * 0.3f) strafeSign = -strafeSign;
                    return Steer(want, walkSpeed * 1.2f, dt, face: dir);
                }
                state = State.Chase;
                Play("Run");
                return Steer(dir, runSpeed, dt) + Separation();
            }

            // дальние: держат коридор дистанции, стреляют, когда видят
            bool sees = SeesPlayer();
            if (cooldownTimer <= 0f && sees && dist > keepDistance.x * 0.6f && dist < keepDistance.y + 4f)
            {
                BeginWindup(charge: false);
                return Vector3.zero;
            }
            if (dist > keepDistance.y || !sees)
            {
                Play("Run");
                return Steer(dir, runSpeed * 0.9f, dt) + Separation();
            }
            if (dist < keepDistance.x)
            {
                Play("Walk");
                return Steer(-dir, walkSpeed * 1.6f, dt, face: dir) + Separation();
            }
            Play("Walk");
            Vector3 strafe = Vector3.Cross(Vector3.up, dir) * strafeSign;
            if (Random.value < dt * 0.4f) strafeSign = -strafeSign;
            return Steer(strafe, walkSpeed, dt, face: dir) + Separation();
        }

        void BeginWindup(bool charge)
        {
            charging = charge;
            smash = kind == EnemyKind.Executioner && !charge;
            state = State.Windup;
            float w = charge ? 0.7f : windup;
            timer = w;
            string clip = charge ? "Run" : attackClip;
            if (charge) { Play("Hit", 0.1f, restart: true); }                  // палач пригибается перед тараном
            else PlayAttack(clip, w);
            aimPoint = PlayerTarget.Chest;
            if (kind == EnemyKind.Bucket) aimPoint = LeadPoint(bucketFlightTime * 0.6f);
            CalabashSfx.Play("cloth", transform.position + Vector3.up, 0.6f);
        }

        void WindupTick(Vector3 toPlayer, float dt)
        {
            float w = charging ? 0.7f : windup;
            float progress = 1f - Mathf.Clamp01(timer / w);
            // поворот к цели; в конце замаха прицел замирает — успей уйти
            bool locked = progress > (kind == EnemyKind.Archer ? 0.8f : 0.7f);
            if (!locked)
            {
                if (toPlayer.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toPlayer.normalized), turnSpeed * dt);
                aimPoint = kind == EnemyKind.Bucket ? LeadPoint(bucketFlightTime * 0.6f) : PlayerTarget.Chest;
            }
            Vector3 fwd = transform.forward;
            Vector3 feet = transform.position;
            switch (kind)
            {
                case EnemyKind.Spearman:
                case EnemyKind.Pitchfork:
                    telegraph.Strip(feet + fwd * 0.4f, feet + fwd * (attackRange + lunge * 0.15f), hitWidth, progress);
                    break;
                case EnemyKind.Executioner:
                    if (charging) telegraph.Strip(feet, feet + fwd * chargeSpeed * chargeTime, 1.6f, progress);
                    else telegraph.Circle(SmashCenter(), SmashRadius, progress);
                    break;
                case EnemyKind.Archer:
                    telegraph.Strip(feet + fwd * 0.5f, new Vector3(aimPoint.x, feet.y, aimPoint.z), 0.25f, progress);
                    break;
                case EnemyKind.Bucket:
                    telegraph.Circle(Ground(aimPoint), splashRadius, progress);
                    break;
            }
            if (timer <= 0f) Release();
        }

        const float SmashRadius = 3.2f;
        Vector3 SmashCenter() => transform.position + transform.forward * 1.9f;

        void Release()
        {
            telegraph.Hide();
            Vector3 fwd = transform.forward;
            if (charging)
            {
                state = State.Charge;
                timer = chargeTime;
                lungeDir = fwd;
                Play("Run", 0.05f);
                CalabashSfx.Play("swing", transform.position + Vector3.up, 1f, 0.5f);
                return;
            }
            state = State.Strike;
            timer = 0.15f;
            lungeDir = fwd;
            switch (kind)
            {
                case EnemyKind.Spearman:
                case EnemyKind.Pitchfork:
                    CalabashSfx.Play("swing", transform.position + Vector3.up * 1.4f, 0.9f, kind == EnemyKind.Pitchfork ? 1.2f : 0.9f);
                    MeleeHit(transform.position + fwd * (attackRange * 0.55f), attackRange * 0.6f + 0.3f);
                    break;
                case EnemyKind.Executioner:
                    Vector3 c = SmashCenter();
                    CalabashSfx.Play("explosion", c, 1f, 0.7f);
                    CalabashSfx.Play("punch_heavy", c, 1f, 0.6f);
                    if (PlayerTarget.Find() && PlayerTarget.CameraFx != null)
                    {
                        float near = 1f - Mathf.Clamp01((PlayerTarget.Position - c).magnitude / 15f);
                        PlayerTarget.CameraFx.AddShake(0.25f * near);
                    }
                    if (Flat(PlayerTarget.Position - c).magnitude < SmashRadius && PlayerTarget.Movement.IsGrounded)
                        PlayerTarget.Damage(damage, false, transform.position, knockback, 7f);    // в прыжке волна проходит под ногами
                    break;
                case EnemyKind.Archer:
                    Shoot();
                    break;
                case EnemyKind.Bucket:
                    Throw();
                    break;
            }
            if (!IsMelee) timer = 0.1f;
        }

        void MeleeHit(Vector3 center, float reach)
        {
            Vector3 p = PlayerTarget.Position;
            Vector3 d = Flat(p - transform.position);
            Vector3 local = transform.InverseTransformDirection(d);
            bool inFront = local.z > -0.3f && local.z < attackRange + lunge * 0.15f + 0.4f && Mathf.Abs(local.x) < hitWidth * 0.5f + 0.4f;
            bool height = Mathf.Abs(p.y - transform.position.y) < 1.6f;
            if (inFront && height && PlayerTarget.Damage(damage, false, transform.position, knockback))
                CalabashSfx.Play(kind == EnemyKind.Spearman ? "blade" : "hurt_hit", p + Vector3.up, 0.8f);
        }

        void ChargeTick()
        {
            Vector3 d = PlayerTarget.Position - transform.position;
            if (Flat(d).magnitude < 1.4f && Mathf.Abs(d.y) < 1.6f)
            {
                if (PlayerTarget.Damage(chargeDamage, false, transform.position - lungeDir, 16f, 5f))
                    CalabashSfx.Play("punch_heavy", PlayerTarget.Chest, 1f, 0.7f);
                EndCharge();
            }
        }

        void EndCharge()
        {
            charging = false;
            state = State.Recover;
            timer = 1.1f;                                   // после тарана палач открыт — бей
            chargeCooldown = Random.Range(5f, 8f);
            Play("Idle", 0.1f);
            CalabashSfx.Play("land", transform.position, 1f, 0.7f);
        }

        void Shoot()
        {
            Vector3 from = transform.position + Vector3.up * 1.45f + transform.forward * 0.5f;
            Vector3 dir = (aimPoint - from).normalized;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Arrow";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(0.035f, 0.035f, 0.8f);
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            if (arrowMaterial != null) go.GetComponent<Renderer>().sharedMaterial = arrowMaterial;
            var p = go.AddComponent<EnemyProjectile>();
            p.damage = damage; p.gravity = 1.5f; p.radius = 0.12f; p.knockback = knockback; p.owner = gameObject;
            p.Launch(dir * projectileSpeed);
            CalabashSfx.Play("bow_release", from, 1f);
            CalabashSfx.Play("swing", from, 0.5f, 1.4f);
        }

        void Throw()
        {
            Vector3 from = transform.position + Vector3.up * 1.9f + transform.forward * 0.3f;
            Vector3 to = Ground(aimPoint) + Vector3.up * 0.2f;
            float T = bucketFlightTime;
            float g = 18f;
            Vector3 v = (to - from) / T + Vector3.up * (0.5f * g * T);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Water";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.45f;
            go.transform.position = from;
            if (waterMaterial != null) go.GetComponent<Renderer>().sharedMaterial = waterMaterial;
            var p = go.AddComponent<EnemyProjectile>();
            p.damage = damage; p.water = true; p.gravity = g; p.radius = 0.2f; p.splashRadius = splashRadius;
            p.knockback = knockback; p.owner = gameObject; p.splashMaterial = waterMaterial;
            p.Launch(v);
            CalabashSfx.Play("swing", from, 0.8f, 0.8f);
        }

        Vector3 LeadPoint(float t)
        {
            Vector3 v = PlayerTarget.Velocity; v.y = 0f;
            return PlayerTarget.Position + v * t;
        }

        Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var h, 10f, ~0, QueryTriggerInteraction.Ignore) && !PlayerTarget.Is(h.collider))
                return h.point;
            return new Vector3(p.x, PlayerTarget.Position.y, p.z);
        }

        bool SeesPlayer()
        {
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 d = PlayerTarget.Chest - eye;
            int n = Physics.RaycastNonAlloc(eye, d.normalized, rayHits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = rayHits[i].collider;
                if (c.transform.IsChildOf(transform) || PlayerTarget.Is(c) || c.GetComponentInParent<Enemy>() != null) continue;
                if (c.GetComponentInParent<IDamageable>() != null) continue;
                return false;
            }
            return true;
        }

        void CancelAttack()
        {
            telegraph.Hide();
            if (state == State.Windup || state == State.Strike || state == State.Charge) cooldownTimer = Random.Range(cooldown.x, cooldown.y);
            charging = false;
            ReleaseToken();
            if (animator != null) animator.speed = 1f;
        }

        bool TryToken()
        {
            if (hasToken) return true;
            hasToken = WaveDirector.TakeMeleeSlot();
            return hasToken;
        }

        void ReleaseToken()
        {
            if (!hasToken) return;
            hasToken = false;
            WaveDirector.ReturnMeleeSlot();
        }

        // ------------------------------------------------------------------ движение
        Vector3 Steer(Vector3 dir, float speed, float dt, Vector3? face = null)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;
            dir.Normalize();
            // обход препятствий: если впереди дом, ищем свободное направление и держим его немного
            if (avoidTimer > 0f) { avoidTimer -= dt; dir = avoidDir; }
            else if (grounded && Blocked(dir, 1.0f) && !Blocked(dir, 1.6f, 2.1f) && !Blocked(Vector3.up, 1.2f))
            {
                velocity.y = 7.5f;                                  // забор, ящик, телега — перепрыгнуть
                grounded = false;
            }
            else if (grounded && Blocked(dir, 1.2f))
            {
                foreach (float a in new[] { 40f, -40f, 75f, -75f, 110f, -110f })
                {
                    Vector3 alt = Quaternion.Euler(0f, a * strafeSign, 0f) * dir;
                    if (!Blocked(alt, 1.4f)) { avoidDir = alt; avoidTimer = 0.6f; dir = alt; break; }
                }
            }
            Vector3 look = face ?? dir;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look.normalized), turnSpeed * dt);
            return dir * speed;
        }

        Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            foreach (var e in All)
            {
                if (e == this) continue;
                Vector3 d = Flat(transform.position - e.transform.position);
                float m = d.magnitude;
                if (m < 1.3f && m > 0.001f) push += d / m * (1.3f - m) * 3f;
            }
            return push;
        }

        void Simulate(Vector3 walk, float dt)
        {
            if (grounded)
            {
                float k = Mathf.Exp(-groundFriction * dt);
                velocity.x *= k; velocity.z *= k;
            }
            velocity.y -= gravity * dt;
            Vector3 step = (walk + new Vector3(velocity.x, 0f, velocity.z)) * dt;
            if (step.sqrMagnitude > 1e-8f && Blocked(step.normalized, step.magnitude + 0.05f))
            {
                step = Vector3.zero;
                velocity.x = velocity.z = 0f;
            }
            Vector3 pos = transform.position + step + Vector3.up * (velocity.y * dt);

            float ground = float.NegativeInfinity;
            Vector3 from = new Vector3(pos.x, Mathf.Max(pos.y, transform.position.y) + 1.2f, pos.z);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, rayHits, 6f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = rayHits[i].collider;
                if (c.transform.IsChildOf(transform) || PlayerTarget.Is(c)) continue;
                if (c.GetComponentInParent<Enemy>() != null || c.GetComponentInParent<IDamageable>() != null) continue;
                if (rayHits[i].point.y > from.y - 0.5f) continue;
                ground = Mathf.Max(ground, rayHits[i].point.y);
            }
            if (ground > float.NegativeInfinity && pos.y <= ground + 0.02f && velocity.y <= 0f)
            {
                pos.y = ground;
                velocity.y = 0f;
                grounded = true;
            }
            else grounded = ground > float.NegativeInfinity && pos.y - ground < 0.05f;
            if (pos.y < -50f) { Die(default); return; }
            transform.position = pos;
        }

        bool Blocked(Vector3 dir, float dist, float height = 1.0f)
        {
            if (dist <= 0f) return false;
            Vector3 c = transform.position + Vector3.up * height;
            int n = Physics.SphereCastNonAlloc(c, 0.3f, dir, rayHits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = rayHits[i].collider;
                if (col.transform.IsChildOf(transform) || rayHits[i].distance <= 0f) continue;
                if (PlayerTarget.Is(col) || col.GetComponentInParent<Enemy>() != null) continue;
                if (col.GetComponentInParent<IDamageable>() != null) continue;      // жители не стена
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ анимация
        void Play(string clip, float fade = 0.2f, bool restart = false)
        {
            if (animator == null || animator.runtimeAnimatorController == null) { anim = clip; return; }
            if (!restart && anim == clip) return;
            anim = clip;
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(clip, fade, 0, 0f);
        }

        /// <summary>Клип атаки растянут так, чтобы удар в клипе пришёлся на конец замаха.</summary>
        void PlayAttack(string clip, float windupTime)
        {
            Play(clip, 0.08f, restart: true);
            if (animator != null && clipLength.TryGetValue(clip, out float len) && windupTime > 0.05f)
                animator.speed = Mathf.Clamp(len * clipStrikeAt / windupTime, 0.3f, 3f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
