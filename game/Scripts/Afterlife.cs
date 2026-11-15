using System.Collections.Generic;
using Calabash.Audio;
using Calabash.Combat;
using Calabash.Movement;
using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Сцена после смерти. Режим берётся из GameProgress.NextAfterlife:
    /// - Лимб: пустой белый мир, вдали стоит Лимб. Подойди — он спросит, будешь ли ты ещё убивать.
    ///   «Да» — назад в деревню с тем же прогрессом. «Нет» — рай, конец игры.
    /// - Рай: свет, тишина, огонь гаснет. Конец.
    /// - Ад (после смерти палача Корнея): чёрное небо, красная земля, огонь вокруг. Конец.
    /// После концовки прогресс сбрасывается и игра возвращается в главное меню.
    /// </summary>
    public class Afterlife : MonoBehaviour
    {
        [Header("Сцена (ставит GameSetup)")]
        public Transform limbo;
        public Renderer ground;
        public Camera cam;
        public ParticleSystem hellFire;
        public Light sun;

        [Tooltip("Ближе этого к Лимбу — он заговорит, м")] public float talkDistance = 4.5f;

        [Header("Картинки концовок")]
        public Texture2D heavenImage;
        public Texture2D hellImage;
        public Texture2D limboImage;

        enum Mode { Walk, Talk, Ending }
        Mode mode;
        GameProgress.Afterlife kind;
        GameObject player;
        int line;
        float lineTime, fade = 1f, endTime;
        bool fadingOut;
        string fadeTarget;
        List<string> lines;
        List<string> ending;
        int endPage;

        static readonly Color HellSky = new Color32(0x12, 0x06, 0x06, 0xFF);
        static readonly Color HellGround = new Color32(0x5A, 0x0C, 0x10, 0xFF);
        static readonly Color HeavenSky = new Color32(0xFF, 0xF6, 0xE2, 0xFF);

        void Awake()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            GameSettings.Load();
        }

        void Start()
        {
            kind = GameProgress.NextAfterlife;
            GameProgress.NextAfterlife = GameProgress.Afterlife.Limbo;
            var mv = FindAnyObjectByType<ZhdanMovement>();
            player = mv != null ? mv.gameObject : null;
            GameSettings.Apply();
            var p = GameProgress.Current;

            if (kind == GameProgress.Afterlife.Hell) { SetupHell(); StartEnding(HellText(p)); return; }
            if (kind == GameProgress.Afterlife.Heaven) { SetupHeaven(); StartEnding(HeavenText(p)); return; }

            CalabashMusic.Play("limbo");
            mode = Mode.Walk;
            SetControl(true);
            lines = LimboLines(p);
        }

        // ------------------------------------------------------------------ тексты
        static List<string> LimboLines(GameProgress p)
        {
            var l = new List<string>();
            switch (p.deaths)
            {
                case 0:
                case 1:
                    l.Add("Здесь нет ни снега, ни огня, Ждан. Здесь ничего нет.");
                    l.Add("Тебя сожгли за краюху хлеба. А ты сжёг их за это.");
                    break;
                case 2:
                    l.Add("Опять ты. Огонь снова погас.");
                    l.Add($"Ты убил уже {p.kills}. Им тоже было холодно и голодно.");
                    break;
                default:
                    l.Add($"Это {p.deaths}-я смерть, Ждан. Я перестал удивляться.");
                    l.Add($"{p.kills} душ прошли мимо меня. Все спрашивали про тебя.");
                    break;
            }
            l.Add("Я спрошу один раз. Будешь ещё убивать?");
            return l;
        }

        static List<string> HeavenText(GameProgress p) => new List<string>
        {
            "Ждан опустил руки. Огонь вышел из него, как вздох, и стало тихо.",
            "Снег над деревней падал ровно. Кто-то оставил на окне каравай — просто так.",
            p.kills > 0 ? $"Он убил {p.kills}. Но остановился сам. Этого хватило." : "Он не стал мстить. Этого хватило.",
            "КОНЕЦ\nЖдан в раю",
        };

        static List<string> HellText(GameProgress p) => new List<string>
        {
            "Палач Корней упал в снег, и снег под ним зашипел.",
            "Деревни больше нет. Нет ни царя, ни попа, ни хлеба. Есть только огонь.",
            $"Убито: {p.kills}. Смертей: {p.deaths}. Лимб больше ни о чём не спрашивает.",
            "Огонь, который не гаснет, нужен только в одном месте.",
            "КОНЕЦ\nЖдан в аду",
        };

        // ------------------------------------------------------------------ ход
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (fadingOut)
            {
                fade = Mathf.MoveTowards(fade, 1f, dt / 1.5f);
                if (fade >= 1f) GameFlow.Load(fadeTarget);
                return;
            }
            fade = Mathf.MoveTowards(fade, 0f, dt / 1.5f);

            switch (mode)
            {
                case Mode.Walk:
                    if (limbo != null && player != null)
                    {
                        Vector3 d = limbo.position - player.transform.position; d.y = 0f;
                        // Лимб всегда смотрит на Ждана
                        if (d.sqrMagnitude > 0.01f) limbo.rotation = Quaternion.Slerp(limbo.rotation, Quaternion.LookRotation(-d.normalized), dt * 2f);
                        if (d.magnitude < talkDistance || Ui.Pressed("e")) { mode = Mode.Talk; line = 0; lineTime = 0f; SetControl(false); }
                    }
                    break;
                case Mode.Talk:
                    lineTime += dt;
                    bool last = line >= lines.Count - 1;
                    if (!last && (Ui.Pressed("next") || Ui.Clicked()) && lineTime > 0.4f) { line++; lineTime = 0f; }
                    if (last)
                    {
                        if (Ui.Pressed("1")) Answer(true);
                        else if (Ui.Pressed("2")) Answer(false);
                    }
                    break;
                case Mode.Ending:
                    endTime += dt;
                    if ((Ui.Pressed("next") || Ui.Clicked() || Ui.Pressed("esc")) && endTime > 0.6f)
                    {
                        endPage++;
                        endTime = 0f;
                        if (endPage >= ending.Count)
                        {
                            GameProgress.Reset();          // игра пройдена — заново с меню
                            FadeTo(GameFlow.VillageScene);
                        }
                    }
                    break;
            }
        }

        void Answer(bool keepKilling)
        {
            if (keepKilling)
            {
                CalabashSfx.Play2D("fire_burst", 1f, 0.8f);
                GameProgress.SkipMenuOnce = true;
                FadeTo(GameFlow.VillageScene);
            }
            else
            {
                SetupHeaven();
                StartEnding(HeavenText(GameProgress.Current));
            }
        }

        void FadeTo(string scene)
        {
            fadingOut = true;
            fadeTarget = scene;
        }

        void StartEnding(List<string> text)
        {
            mode = Mode.Ending;
            ending = text;
            endPage = 0;
            endTime = 0f;
            SetControl(false);
        }

        void SetupHeaven()
        {
            kind = GameProgress.Afterlife.Heaven;
            CalabashMusic.Play("heaven");
            if (cam != null) cam.backgroundColor = HeavenSky;
            RenderSettings.fogColor = HeavenSky;
            if (sun != null) { sun.color = new Color32(0xFF, 0xE8, 0xC0, 0xFF); sun.intensity *= 1.4f; }
            var fx = player != null ? player.GetComponent<Calabash.Vfx.ZhdanFireVfx>() : null;
            if (fx != null) fx.heatOverride = 0f;          // огонь гаснет
            if (limbo != null) limbo.gameObject.SetActive(false);
        }

        void SetupHell()
        {
            CalabashMusic.Play("hell");
            if (cam != null) cam.backgroundColor = HellSky;
            RenderSettings.fogColor = HellSky;
            RenderSettings.fogStartDistance = 5f;
            RenderSettings.fogEndDistance = 45f;
            if (ground != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", HellGround);
                mpb.SetColor("_Color", HellGround);
                ground.SetPropertyBlock(mpb);
            }
            if (sun != null) { sun.color = new Color32(0xFF, 0x7A, 0x18, 0xFF); }
            if (limbo != null) limbo.gameObject.SetActive(false);
            if (hellFire != null) { hellFire.gameObject.SetActive(true); hellFire.Play(); }
            var fx = player != null ? player.GetComponent<Calabash.Vfx.ZhdanFireVfx>() : null;
            if (fx != null) fx.heatOverride = 1f;          // горит в полную силу
            var fuel = player != null ? player.GetComponent<ZhdanFuel>() : null;
            if (fuel != null) fuel.decayPerSecond = 0f;
        }

        void SetControl(bool on)
        {
            if (player != null)
            {
                foreach (var b in new Behaviour[] { player.GetComponent<ZhdanMovement>(), player.GetComponent<ZhdanCombat>(), player.GetComponent<FirstPersonLook>() })
                    if (b != null) b.enabled = on;
            }
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        // ------------------------------------------------------------------ экран
        void OnGUI()
        {
            float w = Screen.width, h = Screen.height, s = Ui.S;
            switch (mode)
            {
                case Mode.Walk:
                    GUI.Label(new Rect(0, h * 0.08f, w, 60 * s), "ЛИМБ", Ui.Title(54, Ui.Ink));
                    GUI.Label(new Rect(0, h - 90 * s, w, 40 * s), "Иди к Лимбу", Ui.Text(24, Ui.Gray, TextAnchor.MiddleCenter));
                    break;
                case Mode.Talk: DrawTalk(w, h, s); break;
                case Mode.Ending: DrawEnding(w, h, s); break;
            }
            if (fade > 0f) Ui.Shade(kind == GameProgress.Afterlife.Hell ? Color.black : Color.white, fade);
        }

        void DrawTalk(float w, float h, float s)
        {
            var r = new Rect(w * 0.5f - 520 * s, h - 300 * s, 1040 * s, 230 * s);
            Ui.Panel(r, 0.96f);
            if (limboImage != null)
            {
                var ir = new Rect(r.x + 18 * s, r.y + 18 * s, (r.height - 36 * s) * limboImage.width / limboImage.height, r.height - 36 * s);
                GUI.DrawTexture(ir, limboImage, ScaleMode.ScaleToFit);
                Ui.Frame(ir, Ui.Ink, 2f);
            }
            float tx = r.x + 150 * s;
            GUI.Label(new Rect(tx, r.y + 14 * s, 400 * s, 34 * s), "Лимб", Ui.Text(24, Ui.Blood));
            string t = lines[line];
            int chars = Mathf.Clamp(Mathf.FloorToInt(lineTime * 40f), 0, t.Length);
            GUI.Label(new Rect(tx, r.y + 52 * s, r.width - 180 * s, 100 * s), t.Substring(0, chars), Ui.Text(30, Ui.Ink));
            bool last = line >= lines.Count - 1;
            if (!last)
            {
                GUI.Label(new Rect(r.x, r.yMax - 38 * s, r.width - 20 * s, 30 * s), "Пробел — дальше", Ui.Text(18, Ui.Gray, TextAnchor.MiddleRight));
                return;
            }
            if (chars < t.Length) return;
            float bw = 400 * s, bh = 58 * s, by = r.yMax - bh - 18 * s;
            if (Ui.Button(new Rect(tx, by, bw, bh), "1 · Да. Пусть горят")) Answer(true);
            if (Ui.Button(new Rect(tx + bw + 24 * s, by, bw, bh), "2 · Нет. Хватит")) Answer(false);
        }

        void DrawEnding(float w, float h, float s)
        {
            bool hell = kind == GameProgress.Afterlife.Hell;
            Color fg = hell ? Ui.Paper : Ui.Ink;
            string t = ending[Mathf.Min(endPage, ending.Count - 1)];
            bool final = endPage >= ending.Count - 1;
            Ui.Shade(hell ? Color.black : Ui.Paper, final ? 0.85f : 0.45f);
            var img = hell ? hellImage : heavenImage;
            if (final && img != null)
            {
                float ih = h * 0.5f, iw = ih * img.width / img.height;
                var ir = new Rect(w * 0.5f - iw * 0.5f, h * 0.08f, iw, ih);
                GUI.DrawTexture(ir, img, ScaleMode.ScaleToFit);
                Ui.Frame(ir, hell ? Ui.Blood : Ui.Ink, 4f);
            }
            int chars = Mathf.Clamp(Mathf.FloorToInt(endTime * 40f), 0, t.Length);
            if (final)
            {
                string[] parts = t.Split('\n');
                GUI.Label(new Rect(0, h * 0.62f, w, 110 * s), parts[0], Ui.Title(90, hell ? Ui.Blood : Ui.Ink));
                if (parts.Length > 1) GUI.Label(new Rect(0, h * 0.62f + 110 * s, w, 60 * s), parts[1], Ui.Title(36, hell ? Ui.Fire : Ui.Gray));
                GUI.Label(new Rect(0, h - 60 * s, w, 40 * s), "Пробел — в главное меню", Ui.Text(20, hell ? Ui.Gray : Ui.Gray, TextAnchor.MiddleCenter));
            }
            else
            {
                var r = new Rect(w * 0.5f - 480 * s, h * 0.68f, 960 * s, 150 * s);
                GUI.Label(r, t.Substring(0, chars), Ui.Text(34, fg, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(0, h - 50 * s, w, 40 * s), "Пробел — дальше", Ui.Text(18, Ui.Gray, TextAnchor.MiddleCenter));
            }
        }
    }
}
