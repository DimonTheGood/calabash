using UnityEngine;

namespace Calabash.Animation
{
    /// <summary>
    /// Руки Ждана в кадре от первого лица: плечо, предплечье и кулак из SM_Zhdan_FPArms.
    /// Кулаки двигает ZhdanFistsView (удары, бег, слайд), а здесь двухзвенная IK ставит к ним руку:
    /// плечо закреплено под камерой, локоть сгибается вниз и наружу. Если кулак улетает дальше длины руки,
    /// плечо подаётся за ним — как выпад плечом.
    /// Вешается на объект под камерой; куски рук — его дети или любые объекты сцены.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class ZhdanArmsView : MonoBehaviour
    {
        [Header("Цели (кулаки из ZhdanFistsView)")]
        public Transform leftTarget;
        public Transform rightTarget;

        [Header("Куски рук")]
        public Transform upperLeft, forearmLeft, fistLeft;
        public Transform upperRight, forearmRight, fistRight;

        [Header("Размеры")]
        [Tooltip("Плечо относительно камеры (для левой руки x берётся со знаком минус), м")]
        public Vector3 shoulder = new Vector3(0.21f, -0.38f, -0.02f);
        [Tooltip("Длина плеча и предплечья модели, м (из build_characters: Ждан upper 0.31, fore 0.29)")]
        public float upperLength = 0.31f;
        public float forearmLength = 0.29f;
        [Tooltip("Масштаб рук в кадре: по доке руки Ждана «несоразмерно большие»")]
        public float scale = 1.25f;
        [Tooltip("Дополнительный масштаб кулака")]
        public float fistScale = 1.2f;
        [Tooltip("Насколько кулак заходит в предплечье, м (закрывает стык)")]
        public float fistInset = 0.03f;
        [Tooltip("Толщина рук относительно длины: руки-кувалды")]
        public float thickness = 1.5f;

        [Header("Сгиб и поворот")]
        [Tooltip("Куда смотрит локоть, в осях камеры (x — наружу)")]
        public Vector3 elbowHint = new Vector3(0.8f, -1f, -0.2f);
        [Tooltip("Поворот кулака вокруг предплечья, °: 0 — большой палец сверху, 90 — ладонью вниз")]
        public float fistRoll = 60f;
        [Tooltip("Доля поворота кулака, которую повторяет предплечье")]
        [Range(0f, 1f)] public float forearmRollShare = 0.5f;

        void LateUpdate()
        {
            Solve(-1f, leftTarget, upperLeft, forearmLeft, fistLeft);
            Solve(1f, rightTarget, upperRight, forearmRight, fistRight);
        }

        void Solve(float side, Transform target, Transform upper, Transform fore, Transform fist)
        {
            if (target == null) return;
            Transform cam = transform;
            float a = upperLength * scale, b = forearmLength * scale;
            Vector3 S = cam.TransformPoint(new Vector3(side * Mathf.Abs(shoulder.x), shoulder.y, shoulder.z));
            Vector3 W = target.position;
            Vector3 d = W - S;
            float reach = (a + b) * 0.995f;
            if (d.magnitude > reach)
            {
                S = W - d.normalized * reach;                 // плечо идёт за кулаком
                d = W - S;
            }
            float L = Mathf.Max(d.magnitude, Mathf.Abs(a - b) + 0.01f);
            Vector3 dn = d / Mathf.Max(d.magnitude, 1e-4f);
            float ca = Mathf.Clamp((a * a + L * L - b * b) / (2f * a * L), -1f, 1f);
            float sa = Mathf.Sqrt(1f - ca * ca);
            Vector3 pole = cam.TransformDirection(new Vector3(side * elbowHint.x, elbowHint.y, elbowHint.z));
            Vector3 p = Vector3.ProjectOnPlane(pole, dn);
            if (p.sqrMagnitude < 1e-6f) p = Vector3.ProjectOnPlane(-cam.up, dn);
            p.Normalize();
            Vector3 E = S + dn * (a * ca) + p * (a * sa);
            Vector3 up = cam.up;
            float roll = -side * fistRoll;

            Vector3 limb = new Vector3(scale * thickness, scale, scale * thickness);
            Place(upper, S, E - S, up, 0f, limb);
            Place(fore, E, W - E, up, roll * forearmRollShare, limb);
            Place(fist, W - dn2(W - E) * fistInset * scale, W - E, up, roll, Vector3.one * scale * fistScale);
        }

        static Vector3 dn2(Vector3 v) => v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;

        /// <summary>Кусок висит вдоль −Y от своего сустава, перед руки — +Z. Ставим −Y по кости, +Z — к «верху».</summary>
        static void Place(Transform t, Vector3 pos, Vector3 dir, Vector3 hint, float roll, Vector3 scale)
        {
            if (t == null) return;
            Vector3 dn = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
            Quaternion r0 = Quaternion.FromToRotation(Vector3.down, dn);
            Vector3 f = r0 * Vector3.forward;
            Vector3 want = Vector3.ProjectOnPlane(hint, dn);
            float ang = want.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(f, want, dn) : 0f;
            t.SetPositionAndRotation(pos, Quaternion.AngleAxis(ang + roll, dn) * r0);
            t.localScale = scale;
        }
    }
}
