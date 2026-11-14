using UnityEngine;
using UnityEngine.Events;

namespace Calabash.Movement
{
    public enum MoveState { Grounded, Airborne, Sliding, Dashing, Slamming }

    /// <summary>
    /// Мувмент Ждана в духе ULTRAKILL на CharacterController:
    /// бег с сильным контролем в воздухе, дэш на стамина-зарядах, слайд, слайд-прыжок,
    /// граунд-слэм с отскоком, прыжки от стен, койот-тайм и буфер прыжка.
    /// Пивот объекта — в ногах. Поворот по рысканию задаёт FirstPersonLook (крутит этот же transform).
    /// Все числа — в метрах и секундах, герой ростом ~1,8 м.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class ZhdanMovement : MonoBehaviour
    {
        // ───────────────────────────── параметры ─────────────────────────────
        [Header("Бег")]
        [Tooltip("Максимальная скорость бега, м/с")]
        public float runSpeed = 12f;
        [Tooltip("Разгон на земле до runSpeed, м/с²")]
        public float groundAcceleration = 120f;
        [Tooltip("Торможение на земле без ввода, м/с²")]
        public float groundDeceleration = 90f;
        [Tooltip("Как быстро на земле гасится скорость сверх runSpeed (после слайд-прыжка), м/с²")]
        public float groundOverspeedDeceleration = 45f;

        [Header("Воздух")]
        [Tooltip("Контроль в воздухе: как быстро вектор скорости поворачивается к вводу, м/с². Больше — резче")]
        public float airAcceleration = 70f;
        [Tooltip("Потеря скорости сверх runSpeed в воздухе, м/с². 0 — разгон от слайда держится до приземления")]
        public float airOverspeedDecay = 1.5f;
        [Tooltip("Торможение в воздухе без ввода, м/с². 0 — инерция полностью сохраняется")]
        public float airNoInputDrag = 0f;
        [Tooltip("Гравитация, м/с² (в ULTRAKILL заметно сильнее земной)")]
        public float gravity = 40f;
        [Tooltip("Предельная скорость падения, м/с")]
        public float maxFallSpeed = 70f;

        [Header("Прыжок")]
        [Tooltip("Вертикальная скорость прыжка, м/с. Высота ≈ v²/(2·gravity)")]
        public float jumpSpeed = 15f;
        [Tooltip("Койот-тайм: сколько секунд после схода с края ещё можно прыгнуть")]
        public float coyoteTime = 0.12f;
        [Tooltip("Буфер прыжка: нажатие за столько секунд до приземления всё равно сработает")]
        public float jumpBufferTime = 0.15f;

        [Header("Дэш и стамина")]
        [Tooltip("Количество зарядов стамины")]
        [Min(1)] public int staminaCharges = 3;
        [Tooltip("Восстановление, зарядов в секунду")]
        public float staminaRegenPerSecond = 0.9f;
        [Tooltip("Сколько зарядов стоит один дэш")]
        public float dashCost = 1f;
        [Tooltip("Скорость дэша, м/с (дэш всегда горизонтальный)")]
        public float dashSpeed = 45f;
        [Tooltip("Длительность дэша, с")]
        public float dashDuration = 0.18f;
        [Tooltip("Горизонтальная скорость, которая остаётся после дэша, м/с")]
        public float dashExitSpeed = 14f;
        [Tooltip("Окно неуязвимости от начала дэша, с (читать через IsInvulnerable)")]
        public float dashInvulnerability = 0.2f;
        [Tooltip("Дэш прерывает граунд-слэм")]
        public bool dashCancelsSlam = true;

        [Header("Дэш-прыжок (прыжок с земли во время дэша)")]
        public bool allowDashJump = true;
        [Tooltip("Горизонтальная скорость после дэш-прыжка, м/с")]
        public float dashJumpSpeed = 30f;
        [Tooltip("Доп. стоимость дэш-прыжка в зарядах. Не хватает — обычный прыжок")]
        public float dashJumpExtraCost = 1f;

        [Header("Слайд")]
        [Tooltip("Минимальная скорость слайда, м/с — даже со стойки")]
        public float slideMinSpeed = 20f;
        [Tooltip("Прибавка к текущей скорости при входе в слайд, м/с")]
        public float slideStartBoost = 4f;
        [Tooltip("Потолок скорости слайда, м/с")]
        public float slideMaxSpeed = 42f;
        [Tooltip("Трение слайда на ровном, м/с². 0 — едет бесконечно, пока зажата кнопка")]
        public float slideFriction = 0f;
        [Tooltip("Множитель разгона на спуске / торможения на подъёме (доля гравитации)")]
        public float slideSlopeFactor = 1.2f;
        [Tooltip("Поворот слайда клавишами A/D, град/с")]
        public float slideSteering = 70f;
        [Tooltip("Ниже этой скорости слайд заканчивается, м/с")]
        public float slideStopSpeed = 5f;
        [Tooltip("Рост хитбокса в слайде, м")]
        public float slideHeight = 0.9f;
        [Tooltip("Если кнопка слайда зажата при приземлении — сразу уехать в слайд")]
        public bool slideOnLandingIfHeld = true;
        [Tooltip("Множитель горизонтальной скорости при прыжке из слайда")]
        public float slideJumpSpeedMultiplier = 1f;

        [Header("Граунд-слэм (слайд в воздухе)")]
        [Tooltip("Скорость удара вниз, м/с")]
        public float slamSpeed = 90f;
        [Tooltip("Слэм не начинается, если до земли меньше этого, м")]
        public float slamMinHeight = 0.8f;
        [Tooltip("Окно после приземления слэма, в которое прыжок даёт отскок, с")]
        public float slamBounceWindow = 0.25f;
        [Tooltip("Базовая вертикальная скорость отскока, м/с")]
        public float slamBounceBaseSpeed = 20f;
        [Tooltip("Прибавка к отскоку за каждый метр падения в слэме, м/с")]
        public float slamBounceSpeedPerMeter = 0.4f;
        [Tooltip("Потолок скорости отскока, м/с")]
        public float slamBounceMaxSpeed = 36f;

        [Header("Стены")]
        [Tooltip("Прыжков от стен до следующего приземления")]
        [Min(0)] public int maxWallJumps = 3;
        [Tooltip("На каком расстоянии от стены ещё можно оттолкнуться, м")]
        public float wallCheckDistance = 0.4f;
        [Tooltip("Вертикальная скорость прыжка от стены, м/с")]
        public float wallJumpUpSpeed = 14f;
        [Tooltip("Скорость отталкивания от стены, м/с")]
        public float wallJumpAwaySpeed = 13f;
        [Tooltip("Какая доля скорости вдоль стены сохраняется при прыжке")]
        [Range(0f, 1f)] public float wallJumpKeepMomentum = 0.6f;
        [Tooltip("Медленное сползание, если в воздухе давить в стену")]
        public bool wallSlide = true;
        [Tooltip("Скорость сползания по стене, м/с")]
        public float wallSlideMaxFallSpeed = 5f;
        [Tooltip("Поверхность считается стеной, если |normal.y| меньше этого")]
        [Range(0f, 0.9f)] public float wallMaxNormalY = 0.35f;

        [Header("Хитбокс и земля")]
        [Tooltip("Рост стоя, м (высота капсулы CharacterController)")]
        public float standingHeight = 1.8f;
        [Tooltip("Прижимная скорость к земле, м/с — чтобы не подпрыгивать на неровностях")]
        public float groundStickSpeed = 3f;
        [Tooltip("Прилипание к земле при сбегании со склонов/ступенек, м")]
        public float groundSnapDistance = 0.5f;
        [Tooltip("С чем сталкивается герой (земля, стены, потолки)")]
        public LayerMask collisionMask = ~0;
        [Tooltip("Максимальный шаг симуляции, с (защита от провалов при лагах)")]
        public float maxStep = 0.05f;

        [Header("Ввод (пусто — возьмётся с этого же объекта)")]
        public ZhdanInput input;

        [Header("События (звуки, VFX, ударная волна слэма)")]
        public UnityEvent onJump = new UnityEvent();
        public UnityEvent onWallJump = new UnityEvent();
        public UnityEvent onDash = new UnityEvent();
        public UnityEvent onSlideStart = new UnityEvent();
        public UnityEvent onSlideStop = new UnityEvent();
        public UnityEvent onSlamStart = new UnityEvent();
        [Tooltip("Аргумент — высота падения в слэме, м")]
        public UnityEvent<float> onSlamLand = new UnityEvent<float>();
        [Tooltip("Аргумент — скорость удара о землю, м/с")]
        public UnityEvent<float> onLand = new UnityEvent<float>();

        // ───────────────────────────── состояние ─────────────────────────────
        CharacterController controller;
        bool initialized;

        Vector3 velocity;
        bool grounded, wasGrounded;
        Vector3 groundNormal = Vector3.up;
        bool isSliding, isDashing, isSlamming, isWallSliding;
        bool jumpedThisFrame, justLanded, justEndedDash;

        float stamina;
        int wallJumpsLeft;
        float coyoteTimer, jumpBufferTimer, dashTimer, invulnTimer;
        float slamBounceTimer, slamBounceSpeed, slamStartY;
        Vector3 dashDir, slideDir;
        float slideSpeed;

        readonly Vector3[] sideHits = new Vector3[8];
        int sideHitCount;
        static readonly Collider[] overlapBuffer = new Collider[16];

        // ───────────────────────────── публичное API ─────────────────────────────
        public Vector3 Velocity => velocity;
        public float HorizontalSpeed => Flat(velocity).magnitude;
        public bool IsGrounded => grounded;
        public bool IsSliding => isSliding;
        public bool IsDashing => isDashing;
        public bool IsSlamming => isSlamming;
        public bool IsWallSliding => isWallSliding;
        public bool IsInvulnerable => invulnTimer > 0f;
        public float Stamina => stamina;
        public int WallJumpsLeft => wallJumpsLeft;
        public float CurrentHeight => controller != null ? controller.height : standingHeight;
        /// <summary>Сколько секунд ещё действует окно отскока после слэма.</summary>
        public float SlamBounceTimeLeft => Mathf.Max(0f, slamBounceTimer);

        public MoveState State =>
            isDashing ? MoveState.Dashing :
            isSlamming ? MoveState.Slamming :
            isSliding ? MoveState.Sliding :
            grounded ? MoveState.Grounded : MoveState.Airborne;

        void Awake() => Init();

        void Update()
        {
            if (input == null) return;
            Simulate(input.ReadMovement(), Time.deltaTime);
        }

        public void Init()
        {
            if (initialized) return;
            controller = GetComponent<CharacterController>();
            if (input == null) input = GetComponent<ZhdanInput>();
            SetHeight(standingHeight);
            stamina = staminaCharges;
            wallJumpsLeft = maxWallJumps;
            initialized = true;
        }

        /// <summary>Мгновенно переставить героя и сбросить всё состояние (респаун, телепорт).</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            Init();
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = true;
            velocity = Vector3.zero;
            isSliding = isDashing = isSlamming = false;
            grounded = wasGrounded = false;
            SetHeight(standingHeight);
            stamina = staminaCharges;
            wallJumpsLeft = maxWallJumps;
            coyoteTimer = jumpBufferTimer = slamBounceTimer = invulnTimer = 0f;
        }

        /// <summary>Внешний толчок (взрыв, удар врага, батут).</summary>
        public void AddImpulse(Vector3 deltaVelocity)
        {
            if (isDashing) EndDash();
            isSlamming = false;
            velocity += deltaVelocity;
            if (deltaVelocity.y > 0f) { grounded = false; coyoteTimer = 0f; jumpedThisFrame = true; }
        }

        // ───────────────────────────── симуляция ─────────────────────────────
        /// <summary>Один шаг мувмента. Вызывается из Update; самотест зовёт его напрямую с синтетическим вводом.</summary>
        public void Simulate(MovementInput inp, float dt)
        {
            Init();
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, maxStep);

            jumpedThisFrame = justLanded = justEndedDash = false;
            isWallSliding = false;

            // таймеры
            if (inp.jumpPressed) jumpBufferTimer = jumpBufferTime; else jumpBufferTimer -= dt;
            coyoteTimer -= dt;
            slamBounceTimer -= dt;
            invulnTimer -= dt;
            if (!isDashing) stamina = Mathf.Min(staminaCharges, stamina + staminaRegenPerSecond * dt);

            Vector3 wish = WishDirection(inp.move);

            // триггеры способностей
            if (inp.dashPressed) TryStartDash(wish);
            if (inp.slidePressed && !grounded && !isDashing && !isSlamming) TryStartSlam();

            // скорость по состоянию
            if (isDashing) UpdateDash(dt);
            else if (isSlamming) velocity = Vector3.down * slamSpeed;
            else if (isSliding) UpdateSlide(inp, dt);
            else if (grounded) UpdateGround(wish, dt);
            else UpdateAir(wish, dt);

            if (jumpBufferTimer > 0f) TryJump(wish);

            if (!grounded && !isDashing && !isSlamming) ApplyGravity(wish, dt);

            MoveController(dt);
            PostMove(inp, wish);
        }

        void UpdateGround(Vector3 wish, float dt)
        {
            Vector3 h = Flat(velocity);
            Vector3 target = wish * runSpeed;
            float rate = h.magnitude > runSpeed + 0.01f ? groundOverspeedDeceleration
                       : wish.sqrMagnitude > 0.0001f ? groundAcceleration : groundDeceleration;
            h = Vector3.MoveTowards(h, target, rate * dt);
            velocity = h + Vector3.down * groundStickSpeed;
        }

        void UpdateAir(Vector3 wish, float dt)
        {
            Vector3 h = Flat(velocity);
            float speed = h.magnitude;
            if (wish.sqrMagnitude > 0.0001f)
            {
                // Контроль как в ULTRAKILL: вектор поворачивается к вводу, но набранная сверх бега скорость не срезается.
                Vector3 target = wish * Mathf.Max(runSpeed, speed);
                h = Vector3.MoveTowards(h, target, airAcceleration * dt);
            }
            else if (airNoInputDrag > 0f)
            {
                h = Vector3.MoveTowards(h, Vector3.zero, airNoInputDrag * dt);
            }
            float m = h.magnitude;
            if (m > runSpeed && airOverspeedDecay > 0f)
                h *= Mathf.Max(runSpeed, m - airOverspeedDecay * dt) / m;
            velocity = new Vector3(h.x, velocity.y, h.z);
        }

        void ApplyGravity(Vector3 wish, float dt)
        {
            velocity.y -= gravity * dt;
            if (wallSlide && velocity.y < -wallSlideMaxFallSpeed && wish.sqrMagnitude > 0.01f
                && FindWall(out Vector3 n) && Vector3.Dot(wish, n) < -0.3f)
            {
                velocity.y = -wallSlideMaxFallSpeed;
                isWallSliding = true;
            }
            velocity.y = Mathf.Max(velocity.y, -maxFallSpeed);
        }

        // ── прыжки ──
        void TryJump(Vector3 wish)
        {
            if (isSlamming) return;

            bool onGround = grounded || coyoteTimer > 0f;
            if (onGround)
            {
                float up = jumpSpeed;
                if (slamBounceTimer > 0f)
                {
                    up = slamBounceSpeed;
                    slamBounceTimer = 0f;
                }

                Vector3 h = Flat(velocity);
                if (isDashing)
                {
                    EndDash();
                    h = Flat(velocity);
                    if (allowDashJump && stamina >= dashJumpExtraCost)
                    {
                        stamina -= dashJumpExtraCost;
                        h = dashDir * dashJumpSpeed;
                    }
                }
                else if (isSliding)
                {
                    h = slideDir * slideSpeed * slideJumpSpeedMultiplier;
                    StopSlide();
                }

                velocity = h + Vector3.up * up;
                onJump.Invoke();
            }
            else if (wallJumpsLeft > 0 && FindWall(out Vector3 n))
            {
                if (isDashing) EndDash();
                Vector3 along = Vector3.ProjectOnPlane(Flat(velocity), n) * wallJumpKeepMomentum;
                velocity = along + n * wallJumpAwaySpeed + Vector3.up * wallJumpUpSpeed;
                wallJumpsLeft--;
                onWallJump.Invoke();
            }
            else return; // прыгнуть некуда — нажатие остаётся в буфере

            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            grounded = false;
            jumpedThisFrame = true;
        }

        // ── дэш ──
        void TryStartDash(Vector3 wish)
        {
            if (isDashing || stamina < dashCost) return;
            if (isSlamming)
            {
                if (!dashCancelsSlam) return;
                isSlamming = false;
            }
            if (isSliding) StopSlide();

            stamina -= dashCost;
            dashDir = wish.sqrMagnitude > 0.01f ? wish.normalized : FlatForward;
            isDashing = true;
            dashTimer = dashDuration;
            invulnTimer = dashInvulnerability;
            onDash.Invoke();
        }

        void UpdateDash(float dt)
        {
            dashTimer -= dt;
            velocity = dashDir * dashSpeed + Vector3.down * (grounded ? groundStickSpeed : 0f);
            if (dashTimer <= 0f) EndDash();
        }

        void EndDash()
        {
            if (!isDashing) return;
            isDashing = false;
            justEndedDash = true;
            velocity = dashDir * Mathf.Min(dashSpeed, dashExitSpeed) + Vector3.up * velocity.y;
        }

        // ── слайд ──
        void StartSlide(Vector3 wish)
        {
            Vector3 h = Flat(velocity);
            Vector3 dir = wish.sqrMagnitude > 0.01f ? wish.normalized
                        : h.sqrMagnitude > 1f ? h.normalized : FlatForward;
            float carried = Mathf.Max(0f, Vector3.Dot(h, dir));
            slideSpeed = Mathf.Min(slideMaxSpeed, Mathf.Max(slideMinSpeed, carried + slideStartBoost));
            slideDir = dir;
            isSliding = true;
            SetHeight(slideHeight);
            onSlideStart.Invoke();
        }

        void UpdateSlide(MovementInput inp, float dt)
        {
            slideDir = Quaternion.AngleAxis(inp.move.x * slideSteering * dt, Vector3.up) * slideDir;
            Vector3 downhill = Flat(Vector3.ProjectOnPlane(Vector3.down, groundNormal));
            slideSpeed += Vector3.Dot(slideDir, downhill) * gravity * slideSlopeFactor * dt;
            slideSpeed -= slideFriction * dt;
            slideSpeed = Mathf.Clamp(slideSpeed, 0f, slideMaxSpeed);
            velocity = slideDir * slideSpeed + Vector3.down * groundStickSpeed;
        }

        void StopSlide()
        {
            if (!isSliding) return;
            isSliding = false;
            if (CanStand()) SetHeight(standingHeight); // иначе встанем, когда над головой освободится
            onSlideStop.Invoke();
        }

        // ── граунд-слэм ──
        void TryStartSlam()
        {
            float r = controller.radius * 0.9f;
            if (Physics.SphereCast(transform.position + Vector3.up * (controller.radius + 0.05f), r, Vector3.down,
                    out RaycastHit hit, slamMinHeight + controller.radius, collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider != controller)
                return; // слишком низко — просто приземлимся (и уедем в слайд, если кнопка зажата)

            if (isSliding) StopSlide();
            isSlamming = true;
            slamStartY = transform.position.y;
            velocity = Vector3.down * slamSpeed;
            onSlamStart.Invoke();
        }

        // ── перемещение и коллизии ──
        void MoveController(float dt)
        {
            sideHitCount = 0;
            Vector3 v = velocity;
            if (grounded && !jumpedThisFrame && velocity.y <= 0f)
            {
                // по склону — вдоль поверхности, чтобы не «взлетать» на спусках
                Vector3 h = Flat(velocity);
                if (h.sqrMagnitude > 0.0001f)
                    v = Vector3.ProjectOnPlane(h, groundNormal).normalized * h.magnitude + Vector3.down * groundStickSpeed;
            }

            float fallSpeed = -velocity.y;
            CollisionFlags flags = controller.Move(v * dt);
            wasGrounded = grounded;

            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;

            // упёрлись в стену — убираем составляющую скорости в стену, чтобы инерция не копилась
            for (int i = 0; i < sideHitCount; i++)
            {
                Vector3 n = sideHits[i];
                Vector3 h = Flat(velocity);
                float into = Vector3.Dot(h, n);
                if (into < 0f) velocity -= n * into;
            }

            grounded = false;
            if (velocity.y <= 0.01f && !jumpedThisFrame)
            {
                if (ProbeGround(controller.skinWidth + 0.06f, out RaycastHit hit))
                {
                    grounded = true;
                    groundNormal = hit.normal;
                }
                else if (wasGrounded && !isSlamming && ProbeGround(groundSnapDistance, out hit))
                {
                    controller.Move(Vector3.down * groundSnapDistance);
                    grounded = true;
                    groundNormal = hit.normal;
                }
            }
            if (!grounded) groundNormal = Vector3.up;

            if (grounded && !wasGrounded)
            {
                justLanded = true;
                lastImpactSpeed = Mathf.Max(0f, fallSpeed);
            }
        }

        float lastImpactSpeed;

        void PostMove(MovementInput inp, Vector3 wish)
        {
            if (grounded) coyoteTimer = coyoteTime;

            if (justLanded)
            {
                wallJumpsLeft = maxWallJumps;
                if (isSlamming)
                {
                    isSlamming = false;
                    float fall = Mathf.Max(0f, slamStartY - transform.position.y);
                    slamBounceSpeed = Mathf.Clamp(slamBounceBaseSpeed + slamBounceSpeedPerMeter * fall,
                                                  slamBounceBaseSpeed, slamBounceMaxSpeed);
                    slamBounceTimer = slamBounceWindow;
                    velocity = Vector3.down * groundStickSpeed;
                    onSlamLand.Invoke(fall);
                }
                else velocity.y = -groundStickSpeed;
                onLand.Invoke(lastImpactSpeed);
            }

            if (isSliding)
            {
                if (!grounded)
                {
                    StopSlide(); // сорвались с края — летим с набранной скоростью
                }
                else
                {
                    // стена срезала скорость — синхронизируем слайд
                    Vector3 h = Flat(velocity);
                    if (h.magnitude < slideSpeed - 0.05f)
                    {
                        slideSpeed = h.magnitude;
                        if (h.sqrMagnitude > 0.01f) slideDir = h.normalized;
                    }
                    bool wantsStop = !inp.slideHeld || slideSpeed < slideStopSpeed;
                    if (wantsStop)
                    {
                        if (CanStand()) StopSlide();
                        else slideSpeed = Mathf.Max(slideSpeed, slideStopSpeed); // под низким потолком едем дальше
                    }
                }
            }
            else if (grounded && !isDashing && !jumpedThisFrame && inp.slideHeld &&
                     (inp.slidePressed || (justLanded && slideOnLandingIfHeld) || justEndedDash))
            {
                StartSlide(wish);
            }

            // присели под потолком и не в слайде — встаём, как только можно
            if (!isSliding && controller.height < standingHeight - 0.001f && CanStand())
                SetHeight(standingHeight);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Vector3 n = hit.normal;
            if (Mathf.Abs(n.y) < wallMaxNormalY && sideHitCount < sideHits.Length)
                sideHits[sideHitCount++] = Flat(n).normalized;
        }

        // ───────────────────────────── запросы к физике ─────────────────────────────
        bool ProbeGround(float maxGap, out RaycastHit hit)
        {
            float r = controller.radius * 0.9f;
            float lift = 0.05f;
            Vector3 origin = transform.position + Vector3.up * (controller.radius + lift);
            float dist = maxGap + (controller.radius - r) + lift;
            if (Physics.SphereCast(origin, r, Vector3.down, out hit, dist, collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider != controller
                && Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit + 1f)
                return true;
            return false;
        }

        /// <summary>Ищет стену рядом (8 горизонтальных направлений). normal — горизонтальная нормаль от стены.</summary>
        public bool FindWall(out Vector3 normal)
        {
            normal = Vector3.zero;
            if (controller == null) return false;
            float r = controller.radius * 0.9f;
            float dist = (controller.radius - r) + wallCheckDistance;
            Vector3 origin = transform.position + controller.center;
            float best = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (Physics.SphereCast(origin, r, dir, out RaycastHit hit, dist, collisionMask, QueryTriggerInteraction.Ignore)
                    && hit.collider != controller
                    && Mathf.Abs(hit.normal.y) < wallMaxNormalY
                    && hit.distance < best)
                {
                    best = hit.distance;
                    normal = Flat(hit.normal).normalized;
                }
            }
            return best < float.MaxValue;
        }

        bool CanStand()
        {
            float r = controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (controller.radius + 0.1f);
            Vector3 top = transform.position + Vector3.up * (standingHeight - controller.radius);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r, overlapBuffer, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (overlapBuffer[i] != controller) return false;
            return true;
        }

        void SetHeight(float h)
        {
            if (controller == null) return;
            h = Mathf.Max(h, controller.radius * 2f + 0.01f);
            controller.height = h;
            controller.center = new Vector3(0f, h * 0.5f, 0f);
        }

        // ───────────────────────────── утилиты ─────────────────────────────
        Vector3 FlatForward
        {
            get
            {
                Vector3 f = Flat(transform.forward);
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }

        Vector3 WishDirection(Vector2 move)
        {
            Vector3 right = Flat(transform.right).normalized;
            return Vector3.ClampMagnitude(right * move.x + FlatForward * move.y, 1f);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

#if UNITY_EDITOR
        void OnValidate()
        {
            staminaCharges = Mathf.Max(1, staminaCharges);
            slideHeight = Mathf.Clamp(slideHeight, 0.5f, standingHeight);
            if (initialized && Application.isPlaying && !isSliding) SetHeight(standingHeight);
        }
#endif
    }
}
