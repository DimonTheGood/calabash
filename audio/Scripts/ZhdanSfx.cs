using Calabash.Combat;
using Calabash.Movement;
using UnityEngine;

namespace Calabash.Audio
{
    /// <summary>
    /// Звуки Ждана: свист замаха, удары (лёгкие, тяжёлые, таран, слэм), шаги по снегу, прыжки, приземления,
    /// треск огня (громче с топливом), боль от урона и всплеск воды.
    /// Подписывается на события ZhdanMovement, ZhdanCombat и ZhdanFuel. Звуки героя играют «у уха» (2D).
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class ZhdanSfx : MonoBehaviour
    {
        public ZhdanMovement movement;
        public ZhdanCombat combat;
        public ZhdanFuel fuel;

        [Header("Шаги")]
        [Tooltip("Шаг каждые N метров пути по земле")] public float stepDistance = 1.9f;
        public float stepVolume = 0.55f;

        [Header("Огонь")]
        public AudioClip fireLoop;
        [Tooltip("Громкость треска при пустом и полном топливе")]
        public Vector2 fireVolume = new Vector2(0.08f, 0.35f);

        [Header("Урон")]
        [Tooltip("Потеря топлива меньше этого — без звука боли")]
        public float hurtThreshold = 1.5f;

        AudioSource fireSource;
        float stepAcc;
        float lastFuel = -1f;
        Vector3 lastPos;

        void Awake()
        {
            if (movement == null) movement = GetComponent<ZhdanMovement>();
            if (combat == null) combat = GetComponent<ZhdanCombat>();
            if (fuel == null) fuel = GetComponent<ZhdanFuel>();
        }

        void OnEnable()
        {
            if (movement != null)
            {
                movement.onJump.AddListener(OnJump);
                movement.onWallJump.AddListener(OnJump);
                movement.onDash.AddListener(OnDash);
                movement.onSlideStart.AddListener(OnSlide);
                movement.onSlamStart.AddListener(OnSlamStart);
                movement.onSlamLand.AddListener(OnSlamLand);
                movement.onLand.AddListener(OnLand);
            }
            if (combat != null)
            {
                combat.onAttackStart.AddListener(OnAttack);
                combat.onHit.AddListener(OnHit);
                combat.onKill.AddListener(OnKill);
            }
            if (fuel != null) fuel.onChanged.AddListener(OnFuel);
        }

        void OnDisable()
        {
            if (movement != null)
            {
                movement.onJump.RemoveListener(OnJump);
                movement.onWallJump.RemoveListener(OnJump);
                movement.onDash.RemoveListener(OnDash);
                movement.onSlideStart.RemoveListener(OnSlide);
                movement.onSlamStart.RemoveListener(OnSlamStart);
                movement.onSlamLand.RemoveListener(OnSlamLand);
                movement.onLand.RemoveListener(OnLand);
            }
            if (combat != null)
            {
                combat.onAttackStart.RemoveListener(OnAttack);
                combat.onHit.RemoveListener(OnHit);
                combat.onKill.RemoveListener(OnKill);
            }
            if (fuel != null) fuel.onChanged.RemoveListener(OnFuel);
        }

        void Start()
        {
            lastPos = transform.position;
            if (fuel != null) lastFuel = fuel.Fuel;
            if (fireLoop != null)
            {
                fireSource = gameObject.AddComponent<AudioSource>();
                fireSource.clip = fireLoop;
                fireSource.loop = true;
                fireSource.spatialBlend = 0f;
                fireSource.volume = 0f;
                fireSource.Play();
            }
        }

        void Update()
        {
            // шаги: по пройденному пути на земле
            Vector3 p = transform.position;
            Vector3 d = p - lastPos; d.y = 0f;
            lastPos = p;
            if (movement != null && movement.IsGrounded && !movement.IsSliding && !movement.IsDashing && d.magnitude < 2f)
            {
                stepAcc += d.magnitude;
                if (stepAcc >= stepDistance)
                {
                    stepAcc = 0f;
                    float speed = movement.HorizontalSpeed;
                    CalabashSfx.Play2D("footstep_snow", stepVolume * Mathf.Clamp(speed / 9f, 0.5f, 1.1f));
                }
            }
            else stepAcc = stepDistance * 0.6f;    // после прыжка первый шаг скоро

            if (fireSource != null)
            {
                float f = fuel != null ? fuel.Fuel01 : 1f;
                float want = Mathf.Lerp(fireVolume.x, fireVolume.y, f);
                if (combat != null && combat.Phase == AttackPhase.Charging) want += 0.15f * combat.Charge01;  // замах разгорается
                fireSource.volume = Mathf.MoveTowards(fireSource.volume, want, Time.unscaledDeltaTime * 0.8f);
                fireSource.pitch = 0.9f + 0.25f * f;
            }
        }

        void OnJump() => CalabashSfx.Play2D("cloth", 0.7f);
        void OnSlide() => CalabashSfx.Play2D("footstep_snow", 0.9f, 0.7f);
        void OnSlamStart() => CalabashSfx.Play2D("swing", 0.8f, 0.6f);

        void OnDash()
        {
            CalabashSfx.Play2D("swing", 0.9f, 0.55f);
            CalabashSfx.Play2D("fire_burst", 0.45f, 1.3f);
        }

        void OnLand(float impactSpeed)
        {
            if (impactSpeed < 6f) { CalabashSfx.Play2D("footstep_snow", stepVolume); return; }
            CalabashSfx.Play2D("land", Mathf.Clamp01(impactSpeed / 18f) + 0.3f);
        }

        void OnSlamLand(float height)
        {
            float k = Mathf.Clamp01(height / 12f);
            CalabashSfx.Play2D("explosion", 0.7f + 0.3f * k, 0.9f - 0.15f * k);
            CalabashSfx.Play2D("punch_heavy", 0.9f, 0.7f);
            CalabashSfx.Play2D("fire_burst", 0.8f, 0.8f);
        }

        void OnAttack(AttackKind kind)
        {
            switch (kind)
            {
                case AttackKind.Light1:
                case AttackKind.Light2: CalabashSfx.Play2D("swing", 0.6f, 1.15f); break;
                case AttackKind.Light3: CalabashSfx.Play2D("swing", 0.8f, 0.95f); break;
                case AttackKind.Heavy: CalabashSfx.Play2D("swing", 1f, 0.75f); CalabashSfx.Play2D("fire_burst", 0.4f, 1.1f); break;
                case AttackKind.Uppercut: CalabashSfx.Play2D("swing", 0.9f, 0.85f); CalabashSfx.Play2D("fire_burst", 0.5f, 1.2f); break;
                case AttackKind.DashStrike: CalabashSfx.Play2D("swing", 1f, 0.7f); break;
            }
        }

        void OnHit(AttackKind kind)
        {
            Vector3 at = combat != null ? combat.LastHitPoint : transform.position;
            switch (kind)
            {
                case AttackKind.Light1:
                case AttackKind.Light2:
                    CalabashSfx.Play("punch_light", at, 0.9f);
                    break;
                case AttackKind.Light3:
                    CalabashSfx.Play("punch_heavy", at, 0.7f, 1.15f);
                    break;
                case AttackKind.Heavy:
                    float c = combat != null ? combat.Charge01 : 1f;
                    CalabashSfx.Play("punch_heavy", at, 0.9f + 0.1f * c, 1.05f - 0.25f * c);
                    if (c > 0.6f) CalabashSfx.Play("explosion", at, 0.5f * c, 1.2f);
                    break;
                case AttackKind.DashStrike:
                case AttackKind.Uppercut:
                    CalabashSfx.Play("punch_heavy", at, 1f, 0.9f);
                    CalabashSfx.Play("fire_burst", at, 0.6f);
                    break;
                case AttackKind.SlamWave:
                    CalabashSfx.Play("punch_light", at, 0.8f, 0.8f);
                    break;
            }
        }

        void OnKill()
        {
            Vector3 at = combat != null ? combat.LastHitPoint : transform.position;
            CalabashSfx.Play("fire_burst", at, 0.9f, 0.85f);
        }

        void OnFuel(float value)
        {
            if (lastFuel >= 0f && lastFuel - value >= hurtThreshold)
            {
                float k = Mathf.Clamp01((lastFuel - value) / 25f);
                CalabashSfx.Play2D("hurt_hit", 0.7f + 0.3f * k, 0.9f);
            }
            lastFuel = value;
        }
    }
}
