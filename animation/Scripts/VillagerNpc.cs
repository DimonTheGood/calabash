using Calabash.Combat;
using UnityEngine;

namespace Calabash.Animation
{
    /// <summary>
    /// Житель деревни с анимациями (SK_Char_*): бродит по точкам, болтает, молится, стоит на посту.
    /// Его можно бить (IDamageable): вздрагивает, отлетает от удара, жители после удара убегают,
    /// стража разворачивается к Ждану. При нуле здоровья падает, лежит и встаёт с полным здоровьем.
    /// Движение кинематическое: своя гравитация, луч к земле, проверка стены впереди.
    /// Состояния Animator — имена клипов: Idle, Walk, Run, Talk, Pray, Cower, Hit, Death, GetUp.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class VillagerNpc : MonoBehaviour, IDamageable
    {
        public enum Role
        {
            [Tooltip("ходит между точками")] Wanderer,
            [Tooltip("стоит и разговаривает")] Talker,
            [Tooltip("молится на месте")] Prayer,
            [Tooltip("стоит на посту, не убегает")] Guard,
            [Tooltip("ходит дозором между точками, не убегает")] Patrol,
            [Tooltip("просто стоит")] Stander,
        }

        public Role role = Role.Wanderer;
        public Animator animator;
        [Tooltip("Точки маршрута (мир). Пусто — бродит вокруг места появления")]
        public Vector3[] waypoints = new Vector3[0];
        [Tooltip("Радиус прогулки без точек, м")]
        public float wanderRadius = 6f;

        [Header("Движение")]
        public float walkSpeed = 1.3f;
        public float runSpeed = 4.2f;
        public float turnSpeed = 240f;
        public float idleTimeMin = 2f;
        public float idleTimeMax = 6f;
        public float gravity = 25f;
        [Tooltip("Гашение скорости отлёта на земле, 1/с")]
        public float groundFriction = 6f;

        [Header("Бой")]
        public float maxHealth = 60f;
        [Tooltip("Масса: отлёт от удара делится на неё")]
        public float mass = 1f;
        public float hitStunTime = 0.35f;
        public float downTime = 4.5f;
        public float fleeTime = 3.5f;
        [Tooltip("Жители пугаются, если Ждан бьёт ближе этого, м")]
        public float fearRadius = 4f;

        enum State { Idle, Walk, Act, Flee, Cower, Hit, Down, GetUp }

        public float Health => health;
        public bool IsAlive => health > 0f;
        public string AnimState => anim;

        float health;
        State state;
        float timer;
        Vector3 velocity;
        bool grounded = true;
        int waypoint;
        Vector3 target;
        Vector3 home;
        Quaternion actRotation;
        string anim;
        Transform hero;
        ZhdanCombat heroCombat;
        CapsuleCollider capsule;
        static readonly RaycastHit[] hits = new RaycastHit[12];

        bool Fearful => role == Role.Wanderer || role == Role.Talker || role == Role.Prayer || role == Role.Stander;

        void Awake()
        {
            health = maxHealth;
            home = transform.position;
            actRotation = transform.rotation;
            capsule = GetComponent<CapsuleCollider>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        void Start()
        {
            heroCombat = FindAnyObjectByType<ZhdanCombat>();
            if (heroCombat != null) hero = heroCombat.transform;
            waypoint = Random.Range(0, Mathf.Max(1, waypoints.Length));
            EnterCalm(Random.Range(0f, 2f));
        }

        // ------------------------------------------------------------------ бой
        public HitResult TakeHit(in HitInfo hit)
        {
            if (!IsAlive) return default;
            float dealt = Mathf.Min(health, hit.damage);
            health -= dealt;
            velocity += hit.knockback / Mathf.Max(0.1f, mass);
            if (velocity.y > 0.5f) grounded = false;
            Vector3 from = -hit.direction; from.y = 0f;
            if (from.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(from.normalized);   // лицом к удару
            if (health <= 0f)
            {
                state = State.Down; timer = downTime;
                Play("Death", 0.08f);
                return new HitResult { damageDealt = dealt, killed = true };
            }
            state = State.Hit; timer = hitStunTime;
            Play("Hit", 0.05f, restart: true);
            return new HitResult { damageDealt = dealt, killed = false };
        }

        // ------------------------------------------------------------------ поведение
        void Update()
        {
            float dt = Time.deltaTime;
            timer -= dt;
            Vector3 move = Vector3.zero;

            switch (state)
            {
                case State.Idle:
                case State.Act:
                    if (Scared()) { EnterCower(); break; }
                    if (state == State.Act) transform.rotation = Quaternion.RotateTowards(transform.rotation, actRotation, turnSpeed * dt);
                    if (timer <= 0f) NextLeg();
                    break;
                case State.Walk:
                    if (Scared()) { EnterCower(); break; }
                    move = Steer(target, walkSpeed, dt);
                    if (Flat(target - transform.position).magnitude < 0.4f || timer <= 0f) EnterCalm(Random.Range(idleTimeMin, idleTimeMax));
                    break;
                case State.Flee:
                    if (hero != null)
                    {
                        Vector3 away = Flat(transform.position - hero.position);
                        if (away.sqrMagnitude < 0.01f) away = transform.forward;
                        move = Steer(transform.position + away.normalized * 4f, runSpeed, dt);
                    }
                    if (timer <= 0f) EnterCalm(Random.Range(idleTimeMin, idleTimeMax));
                    break;
                case State.Cower:
                    if (timer <= 0f) EnterCalm(1f);
                    break;
                case State.Hit:
                    if (timer <= 0f)
                    {
                        if (Fearful) { state = State.Flee; timer = fleeTime; Play("Run"); }
                        else { FaceHero(); EnterCalm(Random.Range(1f, 2.5f)); }
                    }
                    break;
                case State.Down:
                    if (timer <= 0f) { state = State.GetUp; timer = 1.25f; Play("GetUp", 0.05f, restart: true); }
                    break;
                case State.GetUp:
                    if (timer <= 0f) { health = maxHealth; EnterCalm(Random.Range(0.5f, 1.5f)); }
                    break;
            }
            Simulate(move, dt);
        }

        bool Scared()
        {
            if (!Fearful || heroCombat == null || heroCombat.CurrentAttack == AttackKind.None) return false;
            return (hero.position - transform.position).sqrMagnitude < fearRadius * fearRadius;
        }

        void EnterCower()
        {
            state = State.Cower; timer = Random.Range(1.5f, 2.5f);
            FaceHero();
            Play("Cower");
        }

        void EnterCalm(float wait)
        {
            switch (role)
            {
                case Role.Talker: state = State.Act; timer = Random.Range(5f, 10f); Play("Talk"); break;
                case Role.Prayer: state = State.Act; timer = Random.Range(6f, 12f); Play("Pray"); break;
                case Role.Guard:
                case Role.Stander: state = State.Act; timer = Random.Range(4f, 8f); Play("Idle"); break;
                default: state = State.Idle; timer = wait; Play("Idle"); break;
            }
        }

        void NextLeg()
        {
            if (role == Role.Talker || role == Role.Prayer || role == Role.Guard || role == Role.Stander)
            {
                // постовые иногда возвращаются на место, если их сбили
                if (Flat(home - transform.position).magnitude > 0.6f) { WalkTo(home); return; }
                EnterCalm(0f);
                return;
            }
            if (waypoints != null && waypoints.Length > 0)
            {
                waypoint = (waypoint + 1) % waypoints.Length;
                WalkTo(waypoints[waypoint]);
            }
            else
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                WalkTo(home + new Vector3(r.x, 0f, r.y));
            }
        }

        void WalkTo(Vector3 p)
        {
            target = p;
            state = State.Walk;
            timer = Flat(p - transform.position).magnitude / Mathf.Max(0.1f, walkSpeed) + 3f;
            Play("Walk");
        }

        void FaceHero()
        {
            if (hero == null) return;
            Vector3 d = Flat(hero.position - transform.position);
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d.normalized);
        }

        Vector3 Steer(Vector3 to, float speed, float dt)
        {
            Vector3 d = Flat(to - transform.position);
            if (d.sqrMagnitude < 0.0001f) return Vector3.zero;
            Quaternion want = Quaternion.LookRotation(d.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
            if (Blocked(transform.forward, 0.7f))
            {
                if (state == State.Walk) EnterCalm(Random.Range(0.5f, 1.5f));    // упёрся — постоит и пойдёт к другой точке
                return Vector3.zero;
            }
            return transform.forward * speed;
        }

        // ------------------------------------------------------------------ физика
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

            // земля: луч сверху вниз, свои коллайдеры пропускаем
            float ground = float.NegativeInfinity;
            Vector3 from = new Vector3(pos.x, Mathf.Max(pos.y, transform.position.y) + 1.2f, pos.z);
            int n = UnityEngine.Physics.RaycastNonAlloc(from, Vector3.down, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].collider.GetComponentInParent<VillagerNpc>() != null) continue;
                if (hits[i].point.y > from.y - 0.5f) continue;                   // над головой не земля
                ground = Mathf.Max(ground, hits[i].point.y);
            }
            if (ground > float.NegativeInfinity && pos.y <= ground + 0.02f && velocity.y <= 0f)
            {
                pos.y = ground;
                velocity.y = 0f;
                grounded = true;
            }
            else grounded = ground > float.NegativeInfinity && pos.y - ground < 0.05f;
            if (pos.y < -50f) { pos = home; velocity = Vector3.zero; }
            transform.position = pos;
        }

        bool Blocked(Vector3 dir, float dist)
        {
            if (dist <= 0f) return false;
            Vector3 c = transform.position + Vector3.up * 1.0f;
            int n = UnityEngine.Physics.SphereCastNonAlloc(c, 0.3f, dir, hits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance <= 0f) continue;
                return true;
            }
            return false;
        }

        void Play(string clip, float fade = 0.2f, bool restart = false)
        {
            if (animator == null || animator.runtimeAnimatorController == null) { anim = clip; return; }
            if (!restart && anim == clip) return;
            anim = clip;
            animator.CrossFadeInFixedTime(clip, fade, 0, 0f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (waypoints == null) return;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Gizmos.DrawSphere(waypoints[i], 0.2f);
                Gizmos.DrawLine(waypoints[i], waypoints[(i + 1) % waypoints.Length]);
            }
        }
    }
}
