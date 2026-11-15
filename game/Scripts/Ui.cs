using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Общий вид экранов игры (меню, комикс, лимб, концовки) на IMGUI: бумага, тушь, огонь, кровь —
    /// палитра style bible. Кнопки — прямоугольники с чернильной рамкой, под мышью краснеют.
    /// </summary>
    public static class Ui
    {
        public static readonly Color Paper = new Color32(0xF2, 0xF0, 0xEA, 0xFF);
        public static readonly Color Ink = new Color32(0x0B, 0x0B, 0x0B, 0xFF);
        public static readonly Color Gray = new Color32(0x6E, 0x6E, 0x6E, 0xFF);
        public static readonly Color Fire = new Color32(0xFF, 0x7A, 0x18, 0xFF);
        public static readonly Color FireCore = new Color32(0xFF, 0xD1, 0x66, 0xFF);
        public static readonly Color Blood = new Color32(0xA3, 0x12, 0x1C, 0xFF);

        static GUIStyle text, title, huge, button, small;
        static string hovered;

        /// <summary>Масштаб интерфейса от высоты экрана (1080 → 1).</summary>
        public static float S => Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);

        static void Init()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true };
            title = new GUIStyle(text) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            huge = new GUIStyle(title);
            small = new GUIStyle(text);
            button = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        public static GUIStyle Text(float size, Color c, TextAnchor a = TextAnchor.UpperLeft)
        {
            Init();
            text.fontSize = Mathf.RoundToInt(size * S);
            text.normal.textColor = c;
            text.alignment = a;
            return text;
        }

        public static GUIStyle Title(float size, Color c)
        {
            Init();
            title.fontSize = Mathf.RoundToInt(size * S);
            title.normal.textColor = c;
            title.alignment = TextAnchor.MiddleCenter;     // стиль общий: кто-то мог его поменять
            return title;
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Frame(Rect r, Color c, float w = 3f)
        {
            w *= S;
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        /// <summary>Панель: бумага с чернильной рамкой.</summary>
        public static void Panel(Rect r, float alpha = 0.95f)
        {
            Fill(r, new Color(Paper.r, Paper.g, Paper.b, alpha));
            Frame(r, Ink);
        }

        /// <summary>Кнопка в манга-стиле. dark — светлый текст на тёмном фоне (ад).</summary>
        public static bool Button(Rect r, string label, bool dark = false, bool enabled = true)
        {
            Init();
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Color bg = dark ? (hover ? Blood : new Color(0.08f, 0.08f, 0.08f, 0.9f)) : (hover ? Ink : Paper);
            Color fg = !enabled ? Gray : dark ? Paper : (hover ? Paper : Ink);
            Fill(r, bg);
            Frame(r, dark ? (hover ? Fire : Gray) : Ink);
            if (hover) Fill(new Rect(r.x, r.yMax - 4 * S, r.width, 4 * S), Blood);
            button.fontSize = Mathf.RoundToInt(24 * S);
            button.normal.textColor = fg;
            GUI.Label(r, label, button);
            // звук: шорох при наведении, глухой удар при нажатии
            if (hover && Event.current.type == EventType.Repaint && hovered != label)
            {
                hovered = label;
                Calabash.Audio.CalabashSfx.Play2D("cloth", 0.35f, 1.5f);
            }
            else if (!hover && hovered == label && Event.current.type == EventType.Repaint) hovered = null;
            bool click = enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (click) Calabash.Audio.CalabashSfx.Play2D("punch_light", 0.5f, 0.75f);
            return click;
        }

        /// <summary>Ползунок с подписью; возвращает новое значение.</summary>
        public static float Slider(Rect r, string label, float value, float min, float max)
        {
            GUI.Label(new Rect(r.x, r.y, r.width * 0.45f, r.height), label, Text(22, Ink, TextAnchor.MiddleLeft));
            var bar = new Rect(r.x + r.width * 0.47f, r.y + r.height * 0.5f - 6 * S, r.width * 0.53f, 12 * S);
            Fill(bar, Gray);
            float t = Mathf.InverseLerp(min, max, value);
            Fill(new Rect(bar.x, bar.y, bar.width * t, bar.height), Fire);
            Frame(bar, Ink, 2f);
            var e = Event.current;
            var hit = new Rect(bar.x, r.y, bar.width, r.height);
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && hit.Contains(e.mousePosition))
            {
                value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - bar.x) / bar.width));
                e.Use();
            }
            return value;
        }

        /// <summary>Затемнение всего экрана.</summary>
        public static void Shade(Color c, float a) => Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(c.r, c.g, c.b, a));

        /// <summary>Нажата ли клавиша в этом кадре (новый Input System или старый).</summary>
        public static bool Pressed(string key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return false;
            switch (key)
            {
                case "esc": return kb.escapeKey.wasPressedThisFrame;
                case "next": return kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame;
                case "1": return kb.digit1Key.wasPressedThisFrame;
                case "2": return kb.digit2Key.wasPressedThisFrame;
                case "3": return kb.digit3Key.wasPressedThisFrame;
                case "e": return kb.eKey.wasPressedThisFrame;
            }
            return false;
#else
            switch (key)
            {
                case "esc": return Input.GetKeyDown(KeyCode.Escape);
                case "next": return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
                case "1": return Input.GetKeyDown(KeyCode.Alpha1);
                case "2": return Input.GetKeyDown(KeyCode.Alpha2);
                case "3": return Input.GetKeyDown(KeyCode.Alpha3);
                case "e": return Input.GetKeyDown(KeyCode.E);
            }
            return false;
#endif
        }

        public static bool Clicked()
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null && m.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
