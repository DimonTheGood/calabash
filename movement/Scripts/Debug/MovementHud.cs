using UnityEngine;

namespace Calabash.Movement
{
    /// <summary>Отладочный HUD: скорость, состояние, стамина, прыжки от стен, прицел. F1 — показать/скрыть.</summary>
    public class MovementHud : MonoBehaviour
    {
        public ZhdanMovement movement;
        public bool visible = true;
        public bool showControls = true;

        static readonly Color Paper = new Color32(0xF2, 0xF0, 0xEA, 0xFF);
        static readonly Color Ink = new Color32(0x0B, 0x0B, 0x0B, 0xFF);
        static readonly Color Fire = new Color32(0xFF, 0x7A, 0x18, 0xFF);
        static readonly Color Gray = new Color32(0x6E, 0x6E, 0x6E, 0xFF);

        GUIStyle label;
        float peakSpeed, peakTimer;

        void Awake()
        {
            if (movement == null) movement = GetComponent<ZhdanMovement>();
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;
#else
            if (Input.GetKeyDown(KeyCode.F1)) visible = !visible;
#endif
            if (movement == null) return;
            float s = movement.HorizontalSpeed;
            peakTimer -= Time.deltaTime;
            if (s > peakSpeed || peakTimer <= 0f) { peakSpeed = s; peakTimer = 1.5f; }
        }

        void OnGUI()
        {
            if (!visible || movement == null) return;
            if (label == null)
                label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Ink } };

            // прицел
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Fill(new Rect(c.x - 1, c.y - 8, 2, 16), Ink);
            Fill(new Rect(c.x - 8, c.y - 1, 16, 2), Ink);

            float x = 16, y = 16, w = 260;
            Fill(new Rect(x - 8, y - 8, w + 16, showControls ? 300 : 150), new Color(Paper.r, Paper.g, Paper.b, 0.85f));
            GUI.Label(new Rect(x, y, w, 22), $"скорость  {movement.HorizontalSpeed,5:0.0} м/с  (пик {peakSpeed:0.0})", label); y += 22;
            GUI.Label(new Rect(x, y, w, 22), $"верт.     {movement.Velocity.y,5:0.0} м/с", label); y += 22;
            GUI.Label(new Rect(x, y, w, 22), $"состояние {StateName(movement)}", label); y += 26;

            // стамина: по прямоугольнику на заряд, оранжевый = готов
            int charges = movement.staminaCharges;
            float pip = (w - (charges - 1) * 6) / charges;
            for (int i = 0; i < charges; i++)
            {
                var r = new Rect(x + i * (pip + 6), y, pip, 12);
                Fill(r, Gray);
                float fill = Mathf.Clamp01(movement.Stamina - i);
                Fill(new Rect(r.x, r.y, r.width * fill, r.height), fill >= 1f ? Fire : Ink);
            }
            y += 20;
            GUI.Label(new Rect(x, y, w, 22), $"прыжки от стен: {movement.WallJumpsLeft}/{movement.maxWallJumps}", label); y += 22;
            if (movement.SlamBounceTimeLeft > 0f)
                GUI.Label(new Rect(x, y, w, 22), "ОТСКОК! жми прыжок", label);
            y += 30;

            if (showControls)
            {
                GUI.Label(new Rect(x, y, w, 140),
                    "WASD — бег    мышь — обзор\n" +
                    "Space — прыжок / от стены\n" +
                    "Shift (Mouse4) — дэш\n" +
                    "Ctrl / C — слайд, в воздухе — слэм\n" +
                    "R — респаун   F1 — HUD   Esc — курсор", label);
            }
        }

        static string StateName(ZhdanMovement m)
        {
            switch (m.State)
            {
                case MoveState.Dashing: return "дэш" + (m.IsInvulnerable ? " (неуязвим)" : "");
                case MoveState.Slamming: return "граунд-слэм";
                case MoveState.Sliding: return "слайд";
                case MoveState.Grounded: return "на земле";
                default: return m.IsWallSliding ? "сползает по стене" : "в воздухе";
            }
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
