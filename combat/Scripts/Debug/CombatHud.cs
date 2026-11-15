using UnityEngine;

namespace Calabash.Combat
{
    /// <summary>
    /// HUD боя. Слева большая шкала огня (топливо = жизнь): тушевой столб, внутри огонь от тёмно-красного
    /// к жёлтому, сверху пляшут языки пламени, за ним «след» недавней потери, при росте белая вспышка,
    /// ниже 30% рамка бьётся кровью. Под прицелом замах тяжёлого, справа приём и серия.
    /// F1 прячет HUD вместе с MovementHud. showHelp — подсказка по клавишам (в игре выключена).
    /// </summary>
    public class CombatHud : MonoBehaviour
    {
        public ZhdanCombat combat;
        public ZhdanFuel fuel;
        public bool visible = true;
        [Tooltip("Подсказка по клавишам в левом нижнем углу (для тестовых сцен)")]
        public bool showHelp = true;

        static readonly Color Paper = new Color32(0xF2, 0xF0, 0xEA, 0xFF);
        static readonly Color Ink = new Color32(0x0B, 0x0B, 0x0B, 0xFF);
        static readonly Color Fire = new Color32(0xFF, 0x7A, 0x18, 0xFF);
        static readonly Color FireCore = new Color32(0xFF, 0xD1, 0x66, 0xFF);
        static readonly Color FireDeep = new Color32(0xC2, 0x4E, 0x00, 0xFF);
        static readonly Color Blood = new Color32(0xA3, 0x12, 0x1C, 0xFF);
        static readonly Color Gray = new Color32(0x6E, 0x6E, 0x6E, 0xFF);

        GUIStyle label, big, num, cap;
        Calabash.Style.MangaGloom gloom;
        string lastName = "";
        float nameTimer, shown = -1f, trail = -1f, gain, lastFuel = -1f;
        static Texture2D gradient, tongues, glow;

        static float S => Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);

        void Awake()
        {
            if (combat == null) combat = GetComponent<ZhdanCombat>();
            if (fuel == null) fuel = GetComponent<ZhdanFuel>();
            gloom = GetComponentInChildren<Calabash.Style.MangaGloom>();
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;
#else
            if (Input.GetKeyDown(KeyCode.F1)) visible = !visible;
#endif
            if (combat != null && combat.CurrentName != "" && combat.CurrentName != lastName) { lastName = combat.CurrentName; nameTimer = 0.8f; }
            nameTimer -= Time.unscaledDeltaTime;
            if (nameTimer <= 0f) lastName = "";

            if (fuel != null)
            {
                float f = fuel.Fuel01, dt = Time.unscaledDeltaTime;
                if (shown < 0f) shown = trail = lastFuel = f;
                if (f > lastFuel + 0.005f) gain = 1f;
                lastFuel = f;
                shown = Mathf.Lerp(shown, f, 1f - Mathf.Exp(-dt * 14f));
                trail = trail < shown ? shown : Mathf.MoveTowards(trail, shown, dt * 0.25f);   // след потери тает
                gain = Mathf.MoveTowards(gain, 0f, dt * 2.5f);
            }
        }

        void OnGUI()
        {
            if (!visible || combat == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Ink } };
                big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
                num = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                cap = new GUIStyle(num);
            }
            float w = Screen.width, h = Screen.height, s = S;

            if (fuel != null) FireGauge(h, s);

            // замах тяжёлого — дуга-полоса под прицелом
            if (combat.Phase == AttackPhase.Charging)
            {
                float c = combat.Charge01;
                var r = new Rect(w * 0.5f - 60 * s, h * 0.5f + 26 * s, 120 * s, 8 * s);
                Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Ink);
                Fill(new Rect(r.x, r.y, r.width * c, r.height), c >= 1f ? FireCore : Fire);
            }

            // приём и серия — справа, белым по туши
            big.fontSize = Mathf.RoundToInt(30 * s);
            if (lastName != "") Plate(new Rect(w - 360 * s, 70 * s, 330 * s, 48 * s), lastName, Paper);
            if (combat.Series > 1) Plate(new Rect(w - 240 * s, 124 * s, 210 * s, 48 * s), $"серия ×{combat.Series}", FireCore);

            if (showHelp)
                GUI.Label(new Rect(16, h - 130, 420, 110),
                    "ЛКМ — комбо (3 удара)   ПКМ держать — тяжёлый\n" +
                    "ЛКМ в дэше — таран   ЛКМ в слайде — апперкот\n" +
                    "слэм (Ctrl в воздухе) — ударная волна" +
                    (gloom != null ? $"\nG — мрак ({(gloom.IsOn ? "вкл" : "выкл")})" : ""), label);
        }

        void Plate(Rect r, string text, Color c)
        {
            Fill(r, new Color(Ink.r, Ink.g, Ink.b, 0.85f));
            Fill(new Rect(r.x, r.y, 6 * S, r.height), Fire);
            big.normal.textColor = c;
            GUI.Label(new Rect(r.x, r.y, r.width - 14 * S, r.height), text, big);
        }

        /// <summary>Шкала огня слева: высокий столб на ~58% экрана.</summary>
        void FireGauge(float h, float s)
        {
            MakeTextures();
            float f = Mathf.Clamp01(shown), t = Time.unscaledTime;
            bool low = f < 0.3f;
            float pulse = low ? 0.5f + 0.5f * Mathf.Sin(t * (6f + (0.3f - f) * 30f)) : 0f;
            float shake = low ? (Mathf.PerlinNoise(t * 20f, 0f) - 0.5f) * 6f * s * (1f - f / 0.3f) : 0f;

            float gw = 88 * s, gh = h * 0.58f;
            var outer = new Rect(46 * s + shake, h * 0.5f - gh * 0.5f - 20 * s, gw, gh);
            var inner = new Rect(outer.x + 7 * s, outer.y + 7 * s, outer.width - 14 * s, outer.height - 14 * s);

            // мягкое свечение за столбом, сильнее при полном огне
            var oldc = GUI.color;
            GUI.color = new Color(Fire.r, Fire.g, Fire.b, 0.12f + 0.3f * f + 0.25f * gain);
            float gl = gw * 1.6f;
            GUI.DrawTexture(new Rect(outer.x - gl, inner.yMax - gh * f - gl, gw + gl * 2f, gh * f + gl * 2f), glow);
            GUI.color = oldc;

            // корпус: тушь, рамка бумагой (кровью при низком огне)
            Color frame = Color.Lerp(Paper, Blood, pulse);
            Fill(new Rect(outer.x - 3 * s, outer.y - 3 * s, outer.width + 6 * s, outer.height + 6 * s), frame);
            Fill(outer, Ink);
            Fill(inner, new Color(0.1f, 0.06f, 0.04f, 1f));

            // след потери — тёмно-красный
            if (trail > f + 0.002f)
            {
                float y0 = inner.yMax - inner.height * trail;
                Fill(new Rect(inner.x, y0, inner.width, inner.height * (trail - f)), Blood);
            }
            // огонь: градиент привязан к столбу, а не к уровню
            if (f > 0.001f)
            {
                var fr = new Rect(inner.x, inner.yMax - inner.height * f, inner.width, inner.height * f);
                GUI.DrawTextureWithTexCoords(fr, gradient, new Rect(0, 0, 1, f));
                // языки пламени над уровнем: две полосы, бегут в разные стороны
                float th = (26f + 14f * Mathf.PerlinNoise(t * 3f, 1f)) * s * Mathf.Lerp(0.5f, 1f, f);
                var tr = new Rect(inner.x, fr.y - th + 2 * s, inner.width, th);
                GUI.BeginGroup(new Rect(inner.x, Mathf.Max(inner.y, tr.y), inner.width, tr.yMax - Mathf.Max(inner.y, tr.y)));
                float oy = tr.y - Mathf.Max(inner.y, tr.y);
                GUI.color = Fire;
                GUI.DrawTextureWithTexCoords(new Rect(0, oy, inner.width, th), tongues, new Rect(t * 0.35f, 0, 1.3f, 1));
                GUI.color = FireCore;
                GUI.DrawTextureWithTexCoords(new Rect(0, oy + th * 0.35f, inner.width, th * 0.65f), tongues, new Rect(-t * 0.5f + 0.37f, 0, 1.1f, 1));
                GUI.color = oldc;
                GUI.EndGroup();
                if (gain > 0f) Fill(fr, new Color(1f, 1f, 1f, gain * 0.45f));
            }
            // деления по 25%
            for (int i = 1; i < 4; i++)
            {
                float y = inner.yMax - inner.height * i * 0.25f;
                Fill(new Rect(outer.xMax - 16 * s, y - 1.5f * s, 16 * s, 3 * s), Paper);
                Fill(new Rect(outer.x, y - 1.5f * s, 9 * s, 3 * s), Paper);
            }

            // подпись сверху и число снизу
            cap.fontSize = Mathf.RoundToInt(26 * s);
            cap.normal.textColor = low ? Color.Lerp(Paper, Blood, pulse) : Fire;
            GUI.Label(new Rect(outer.x - 40 * s, outer.y - 44 * s, outer.width + 80 * s, 34 * s), "ОГОНЬ", cap);
            var nb = new Rect(outer.x - 12 * s, outer.yMax + 12 * s, outer.width + 24 * s, 60 * s);
            Fill(nb, Ink);
            Fill(new Rect(nb.x, nb.yMax - 5 * s, nb.width, 5 * s), low ? Blood : Fire);
            num.fontSize = Mathf.RoundToInt(42 * s);
            num.normal.textColor = low ? Color.Lerp(Paper, Blood, pulse) : Paper;
            GUI.Label(nb, Mathf.CeilToInt(fuel.Fuel).ToString(), num);
        }

        static void MakeTextures()
        {
            if (gradient != null) return;
            gradient = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 64; y++)
            {
                float k = y / 63f;
                Color c = k < 0.5f ? Color.Lerp(Blood, FireDeep, k * 2f) : k < 0.85f ? Color.Lerp(FireDeep, Fire, (k - 0.5f) / 0.35f) : Color.Lerp(Fire, FireCore, (k - 0.85f) / 0.15f);
                gradient.SetPixel(0, y, c);
            }
            gradient.Apply();

            // языки пламени: белая маска (красится через GUI.color), повторяется по горизонтали
            const int TW = 128, TH = 48;
            tongues = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            for (int x = 0; x < TW; x++)
            {
                float u = x / (float)TW * Mathf.PI * 2f;
                float top = 0.45f + 0.3f * Mathf.Abs(Mathf.Sin(u * 3f)) + 0.2f * Mathf.Abs(Mathf.Sin(u * 5f + 1.3f));
                top = Mathf.Clamp01(top);
                for (int y = 0; y < TH; y++)
                {
                    float v = y / (float)(TH - 1);
                    float a = Mathf.Clamp01((top - v) * 10f);
                    tongues.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            }
            tongues.Apply();

            glow = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    glow.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            glow.Apply();
        }

        static void Fill(Rect r, Color col)
        {
            var old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
