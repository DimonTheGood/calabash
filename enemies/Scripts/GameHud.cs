using Calabash.Combat;
using UnityEngine;

namespace Calabash.Enemies
{
    /// <summary>
    /// Игровой экран поверх отладочного CombatHud: волна и сколько врагов осталось, крупные надписи волн,
    /// полоски здоровья над побитыми врагами, вспышка по краям экрана от урона (кровь, вода),
    /// «Уклонился!» при дэше сквозь удар, экраны «Огонь погас» и победы с перезапуском.
    /// Цвета — палитра style bible: бумага, тушь, огонь, кровь.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        public WaveDirector director;

        static readonly Color Paper = new Color32(0xF2, 0xF0, 0xEA, 0xFF);
        static readonly Color Ink = new Color32(0x0B, 0x0B, 0x0B, 0xFF);
        static readonly Color Fire = new Color32(0xFF, 0x7A, 0x18, 0xFF);
        static readonly Color Blood = new Color32(0xA3, 0x12, 0x1C, 0xFF);
        static readonly Color Water = new Color32(0x5E, 0x86, 0xA8, 0xFF);

        GUIStyle small, mid, big, huge;
        float flash, dodgeTimer;
        Color flashColor = Blood;
        Camera cam;

        void OnEnable()
        {
            PlayerTarget.Hurt += OnHurt;
            PlayerTarget.Dodged += OnDodged;
        }

        void OnDisable()
        {
            PlayerTarget.Hurt -= OnHurt;
            PlayerTarget.Dodged -= OnDodged;
        }

        void Start()
        {
            // PlayerTarget.Reset() в WaveDirector.Awake снимает подписки — подписываемся заново
            OnDisable(); OnEnable();
            if (director == null) director = WaveDirector.Instance;
        }

        void OnHurt(float dmg, bool water, Vector3 from)
        {
            flash = Mathf.Clamp(0.35f + dmg * 0.02f, 0.4f, 0.85f);
            flashColor = water ? Water : Blood;
        }

        void OnDodged(Vector3 from) => dodgeTimer = 0.7f;

        void Update()
        {
            flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 1.6f);
            dodgeTimer -= Time.unscaledDeltaTime;
        }

        void OnGUI()
        {
            if (director == null) director = WaveDirector.Instance;
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
                mid = new GUIStyle(small) { fontSize = 22, fontStyle = FontStyle.Bold };
                big = new GUIStyle(small) { fontSize = 40, fontStyle = FontStyle.Bold };
                huge = new GUIStyle(small) { fontSize = 64, fontStyle = FontStyle.Bold };
            }
            float w = Screen.width, h = Screen.height;

            if (flash > 0.01f) Vignette(w, h, flashColor, flash);
            EnemyBars();

            if (director == null) return;
            var ph = director.CurrentPhase;

            // волна и сколько осталось — сверху по центру
            if (ph == WaveDirector.Phase.Fighting || ph == WaveDirector.Phase.Break)
            {
                float k = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
                var r = new Rect(w * 0.5f - 210 * k, 18 * k, 420 * k, 84 * k);
                Fill(r, new Color(Ink.r, Ink.g, Ink.b, 0.86f));
                Fill(new Rect(r.x, r.yMax - 4 * k, r.width, 4 * k), Fire);
                // волны — кружки-огоньки: пройденные горят, текущая мигает
                int n = director.WaveCount, cur = director.WaveIndex;
                float pw = 14 * k, gap = 10 * k, px = w * 0.5f - (n * pw + (n - 1) * gap) * 0.5f, py = r.y + 54 * k;
                for (int i = 0; i < n; i++)
                {
                    var pr = new Rect(px + i * (pw + gap), py, pw, pw);
                    Color c = i < cur ? Fire : i == cur ? Color.Lerp(Fire, Paper, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) : new Color(0.3f, 0.3f, 0.3f);
                    if (i == n - 1 && i > cur) c = Blood;              // последняя — палач
                    Fill(pr, c);
                }
                var ts = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(26 * k), alignment = TextAnchor.MiddleCenter, normal = { textColor = Paper } };
                string t = ph == WaveDirector.Phase.Fighting
                    ? $"волна {cur + 1} · врагов {director.Remaining}"
                    : $"передышка {Mathf.CeilToInt(director.PhaseTimer)} с";
                GUI.Label(new Rect(r.x, r.y + 6 * k, r.width, 40 * k), t, ts);
            }

            if (director.BannerTimer > 0f && director.Banner != "")
            {
                float a = Mathf.Clamp01(director.BannerTimer * 2f);
                var r = new Rect(0, h * 0.22f, w, 120);
                Fill(new Rect(0, r.y + 10, w, 100), new Color(Paper.r, Paper.g, Paper.b, 0.75f * a));
                var s = new GUIStyle(big) { normal = { textColor = new Color(Ink.r, Ink.g, Ink.b, a) } };
                GUI.Label(r, director.Banner, s);
            }

            if (dodgeTimer > 0f)
            {
                var s = new GUIStyle(mid) { normal = { textColor = Fire } };
                GUI.Label(new Rect(0, h * 0.6f, w, 30), "уклонился!", s);
            }

            if (ph == WaveDirector.Phase.GameOver) EndScreen(w, h, Ink, Paper, "ОГОНЬ ПОГАС",
                director.externalFlow ? "" : $"волна {director.WaveIndex + 1}/{director.WaveCount} · убито: {director.Kills}\n\nR — заново");
            else if (ph == WaveDirector.Phase.Victory && !director.externalFlow) EndScreen(w, h, Paper, Ink, "ДЕРЕВНЯ ОТБИТА",
                $"убито: {director.Kills} · время {Mathf.FloorToInt(director.SurvivedTime / 60)}:{Mathf.FloorToInt(director.SurvivedTime % 60):00}\n\nR — ещё раз");
        }

        void EndScreen(float w, float h, Color bg, Color fg, string title, string sub)
        {
            Fill(new Rect(0, 0, w, h), new Color(bg.r, bg.g, bg.b, 0.88f));
            Fill(new Rect(0, h * 0.5f - 2, w, 4), Blood);
            GUI.Label(new Rect(0, h * 0.5f - 110, w, 90), title, new GUIStyle(huge) { normal = { textColor = fg } });
            GUI.Label(new Rect(0, h * 0.5f + 20, w, 120), sub, new GUIStyle(mid) { normal = { textColor = fg } });
        }

        void EnemyBars()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            foreach (var e in Enemy.All)
            {
                if (e == null || !e.IsAlive) continue;
                bool boss = e.kind == EnemyKind.Executioner;
                if (!boss && (e.SinceHit > 3f || e.Health >= e.maxHealth)) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (boss ? 2.6f : 2.2f));
                if (sp.z <= 0f || sp.z > 40f) continue;
                float bw = boss ? 90f : 50f, y = Screen.height - sp.y;
                var r = new Rect(sp.x - bw * 0.5f, y, bw, boss ? 7f : 5f);
                Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), Ink);
                Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(e.Health / e.maxHealth), r.height), Blood);
            }
        }

        static void Vignette(float w, float h, Color c, float a)
        {
            float t = Mathf.Min(w, h) * 0.08f;
            var col = new Color(c.r, c.g, c.b, a);
            Fill(new Rect(0, 0, w, t), col);
            Fill(new Rect(0, h - t, w, t), col);
            Fill(new Rect(0, t, t, h - 2 * t), col);
            Fill(new Rect(w - t, t, t, h - 2 * t), col);
            var inner = new Color(c.r, c.g, c.b, a * 0.4f);
            Fill(new Rect(t, t, w - 2 * t, t * 0.6f), inner);
            Fill(new Rect(t, h - t * 1.6f, w - 2 * t, t * 0.6f), inner);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
