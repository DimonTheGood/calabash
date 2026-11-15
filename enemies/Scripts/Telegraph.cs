using UnityEngine;

namespace Calabash.Enemies
{
    /// <summary>
    /// Предупреждение об атаке на снегу, цветом крови из палитры: кольцо (куда упадёт удар или ведро)
    /// или полоса (куда бьёт копьё, летит стрела, бежит палач). Внутренняя часть растёт к моменту удара,
    /// поэтому видно и куда, и когда уходить.
    /// </summary>
    public class Telegraph : MonoBehaviour
    {
        public Material material;
        public Color color = new Color32(0xA3, 0x12, 0x1C, 0xFF);
        public float width = 0.07f;

        const int Segments = 40;
        LineRenderer outer, inner;

        public static Telegraph Create(Transform owner, Material material)
        {
            var go = new GameObject("Telegraph");
            go.transform.SetParent(owner, false);
            var t = go.AddComponent<Telegraph>();
            t.material = material;
            t.Build();
            return t;
        }

        void Build()
        {
            outer = Line("Outer");
            inner = Line("Inner");
            Hide();
        }

        LineRenderer Line(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true;
            l.sharedMaterial = material;
            l.startColor = l.endColor = color;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            l.alignment = LineAlignment.TransformZ;        // лежит на земле
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            l.numCapVertices = 2;
            return l;
        }

        public void Hide()
        {
            if (outer == null) return;
            outer.enabled = inner.enabled = false;
        }

        /// <summary>Кольцо радиуса r; progress 0..1 — внутреннее кольцо дорастает до края к удару.</summary>
        public void Circle(Vector3 center, float r, float progress)
        {
            center.y += 0.06f;
            Ring(outer, center, r, width);
            Ring(inner, center, Mathf.Max(0.05f, r * Mathf.Clamp01(progress)), width * 2.2f);
        }

        /// <summary>Полоса от from до to; progress 0..1 — заполняется к удару.</summary>
        public void Strip(Vector3 from, Vector3 to, float w, float progress)
        {
            from.y = to.y = Mathf.Min(from.y, to.y) + 0.06f;
            Vector3 d = to - from; d.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, d.normalized) * (w * 0.5f);
            outer.enabled = true;
            outer.loop = true;
            outer.positionCount = 4;
            outer.widthMultiplier = width;
            outer.SetPosition(0, from - side); outer.SetPosition(1, to - side);
            outer.SetPosition(2, to + side); outer.SetPosition(3, from + side);
            inner.enabled = true;
            inner.loop = false;
            inner.positionCount = 2;
            inner.widthMultiplier = w * 0.8f;
            inner.SetPosition(0, from);
            inner.SetPosition(1, from + d * Mathf.Clamp01(progress));
        }

        void Ring(LineRenderer l, Vector3 c, float r, float w)
        {
            l.enabled = true;
            l.loop = true;
            l.positionCount = Segments;
            l.widthMultiplier = w;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                l.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }
    }
}
