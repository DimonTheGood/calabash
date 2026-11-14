using UnityEngine;
using UnityEngine.Rendering;

namespace Calabash.Movement
{
    /// <summary>
    /// Вид от первого / третьего лица по кнопке (V, R3). В третьем лице камера уходит за плечо
    /// и не проходит сквозь стены, тело Ждана становится видимым, кулаки переезжают к груди.
    /// Управление и прицел не меняются: тело всё так же поворачивается за мышью.
    /// </summary>
    [DefaultExecutionOrder(40)]
    [DisallowMultipleComponent]
    public class ZhdanCameraMode : MonoBehaviour
    {
        [Tooltip("Сейчас вид от третьего лица")]
        public bool thirdPerson = false;
        public ZhdanInput input;
        [Tooltip("Шарнир взгляда (CameraPivot) — вокруг него крутится камера")]
        public Transform cameraPivot;
        public ZhdanCameraFx cameraFx;

        [Header("Камера от третьего лица")]
        [Tooltip("Расстояние за спиной, м")]
        public float distance = 3.8f;
        [Tooltip("Сдвиг за плечо: x — вправо, y — вверх, м")]
        public Vector2 shoulderOffset = new Vector2(0.55f, 0.35f);
        [Tooltip("Радиус проверки столкновения камеры со стенами, м")]
        public float collisionRadius = 0.25f;
        [Tooltip("Ближе этого к голове камера не подходит, м")]
        public float minDistance = 0.6f;
        [Tooltip("Во что камера упирается")]
        public LayerMask collisionMask = ~0;
        [Tooltip("Время переключения вида, с")]
        public float switchTime = 0.25f;
        [Tooltip("Скорость, с которой камера отъезжает обратно после стены, м/с")]
        public float recoverSpeed = 8f;

        [Header("Тело")]
        [Tooltip("Рендереры тела: в первом лице только тень, в третьем видны")]
        public Renderer[] bodyRenderers = new Renderer[0];
        [Tooltip("Рендереры только для третьего лица (оранжевый контур героя)")]
        public Renderer[] thirdPersonOnly = new Renderer[0];

        [Header("Кулаки / вьюмодель")]
        [Tooltip("Корень вьюмодели (кулаки). Пусто — не трогаем")]
        public Transform viewModel;
        [Tooltip("Родитель вьюмодели в первом лице (камера)")]
        public Transform viewModelFirstPersonParent;
        [Tooltip("Родитель вьюмодели в третьем лице (точка у груди тела)")]
        public Transform viewModelThirdPersonParent;

        float blend;
        float camDistance;
        bool bodyShown;
        bool initialized;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>0 — первое лицо, 1 — третье (с плавным переходом).</summary>
        public float Blend => blend;
        /// <summary>Текущее смещение камеры от шарнира (после проверки стен).</summary>
        public Vector3 CurrentOffset { get; private set; }

        void Awake() => Init();

        void Init()
        {
            if (initialized) return;
            initialized = true;
            if (input == null) input = GetComponent<ZhdanInput>();
            if (cameraFx == null) cameraFx = GetComponentInChildren<ZhdanCameraFx>();
            if (cameraPivot == null && cameraFx != null) cameraPivot = cameraFx.transform.parent;
            blend = thirdPerson ? 1f : 0f;
            camDistance = distance;
            ApplyVisibility(thirdPerson, force: true);
        }

        void LateUpdate()
        {
            bool toggle = input != null && input.CameraTogglePressed();
            Apply(toggle, Time.deltaTime);
        }

        /// <summary>Шаг камеры. Зовётся из LateUpdate; самотест зовёт напрямую.</summary>
        public void Apply(bool togglePressed, float dt)
        {
            Init();
            if (togglePressed) thirdPerson = !thirdPerson;
            float target = thirdPerson ? 1f : 0f;
            blend = switchTime > 0f ? Mathf.MoveTowards(blend, target, dt / switchTime) : target;
            float s = Mathf.SmoothStep(0f, 1f, blend);

            ApplyVisibility(blend > 0.35f, force: false);

            Vector3 offset = Vector3.zero;
            if (s > 0.0001f && cameraPivot != null)
            {
                Vector3 desired = new Vector3(shoulderOffset.x, shoulderOffset.y, -distance) * s;
                float wanted = desired.magnitude;
                float allowed = wanted;
                Vector3 from = cameraPivot.position;
                Vector3 dirWorld = cameraPivot.TransformDirection(desired / Mathf.Max(wanted, 0.0001f));
                int n = Physics.SphereCastNonAlloc(from, collisionRadius, dirWorld, hits, wanted, collisionMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (hits[i].collider.transform.IsChildOf(transform)) continue;
                    if (hits[i].distance <= 0f) continue; // стартовое перекрытие
                    allowed = Mathf.Min(allowed, hits[i].distance);
                }
                allowed = Mathf.Max(Mathf.Min(minDistance, wanted), allowed);
                camDistance = allowed < camDistance ? allowed : Mathf.MoveTowards(camDistance, allowed, recoverSpeed * dt);
                camDistance = Mathf.Min(camDistance, wanted);
                offset = desired / Mathf.Max(wanted, 0.0001f) * camDistance;
            }
            else camDistance = 0f;

            CurrentOffset = offset;
            if (cameraFx != null) cameraFx.baseLocalOffset = offset;
            else if (cameraPivot != null && cameraPivot.childCount > 0) cameraPivot.GetChild(0).localPosition = offset;
        }

        void ApplyVisibility(bool showBody, bool force)
        {
            if (!force && showBody == bodyShown) return;
            bodyShown = showBody;
            foreach (var r in bodyRenderers)
                if (r != null) r.shadowCastingMode = showBody ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var r in thirdPersonOnly)
                if (r != null) r.enabled = showBody;
            if (viewModel != null)
            {
                Transform parent = showBody ? viewModelThirdPersonParent : viewModelFirstPersonParent;
                if (parent != null && viewModel.parent != parent) viewModel.SetParent(parent, false);
            }
        }
    }
}
