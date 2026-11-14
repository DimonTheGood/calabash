using UnityEngine;

namespace Calabash.Combat
{
    /// <summary>Отладочный HUD боя: топливо, серия, текущий приём, замах тяжёлого удара. F1 прячет вместе с MovementHud.</summary>
    public class CombatHud : MonoBehaviour
    {
        public ZhdanCombat combat;
        public ZhdanFuel fuel;
        public bool visible = true;

        static readonly Color Paper = new Color32(0xF2, 0xF0, 0xEA, 0xFF);
        static readonly Color Ink = new Color32(0x0B, 0x0B, 0x0B, 0xFF);
        static readonly Color Fire = new Color32(0xFF, 0x7A, 0x18, 0xFF);
        static readonly Color FireCore = new Color32(0xFF, 0xD1, 0x66, 0xFF);
        static readonly Color Gray = new Color32(0x6E, 0x6E, 0x6E, 0xFF);

        GUIStyle label, big;
        string lastName = "";
        float nameTimer;

        void Awake()
        {
            if (combat == null) combat = GetComponent<ZhdanCombat>();
            if (fuel == null) fuel = GetComponent<ZhdanFuel>();
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
        }

        void OnGUI()
        {
            if (!visible || combat == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Ink } };
                big = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            }
            float w = Screen.width, h = Screen.height;

            // топливо — снизу по центру
            if (fuel != null)
            {
                var bar = new Rect(w * 0.5f - 160, h - 48, 320, 14);
                Fill(new Rect(bar.x - 4, bar.y - 24, bar.width + 8, bar.height + 30), new Color(Paper.r, Paper.g, Paper.b, 0.85f));
                GUI.Label(new Rect(bar.x, bar.y - 22, 320, 20), $"топливо {fuel.Fuel:0}/{fuel.maxFuel:0}", label);
                Fill(bar, Gray);
                Fill(new Rect(bar.x, bar.y, bar.width * fuel.Fuel01, bar.height), fuel.Fuel01 < 0.4f ? Ink : Fire);
            }

            // замах тяжёлого — под прицелом
            if (combat.Phase == AttackPhase.Charging)
            {
                float c = combat.Charge01;
                var r = new Rect(w * 0.5f - 40, h * 0.5f + 18, 80, 6);
                Fill(r, Gray);
                Fill(new Rect(r.x, r.y, r.width * c, r.height), c >= 1f ? FireCore : Fire);
            }

            // приём и серия — справа
            if (lastName != "") GUI.Label(new Rect(w - 320, 20, 300, 40), lastName, big);
            if (combat.Series > 1) GUI.Label(new Rect(w - 320, 60, 300, 40), $"серия ×{combat.Series}", big);

            GUI.Label(new Rect(16, h - 110, 420, 90),
                "ЛКМ — комбо (3 удара)   ПКМ держать — тяжёлый\n" +
                "ЛКМ в дэше — таран   ЛКМ в слайде — апперкот\n" +
                "слэм (Ctrl в воздухе) — ударная волна", label);
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
