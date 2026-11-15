using System.Collections.Generic;
using Calabash.Audio;
using Calabash.Combat;
using Calabash.Enemies;
using Calabash.Movement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Calabash.Game
{
    /// <summary>
    /// Ход игры в деревне: главное меню, комикс-вступление, обучение, пауза (Esc), настройки,
    /// выбор улучшения после каждой волны, смерть → лимб, смерть палача Корнея → ад.
    /// Волны ведёт WaveDirector (enemies/), GameFlow только запускает их и слушает события.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GameFlow : MonoBehaviour
    {
        public const string VillageScene = "VillageTest";
        public const string AfterlifeScene = "Afterlife";
        public const string ExecutionScene = "Execution";

        [System.Serializable]
        public class Slide
        {
            public Texture2D image;
            [TextArea] public string text;
        }

        [Tooltip("Комикс-вступление: картинка и подпись")]
        public List<Slide> intro = new List<Slide>();

        enum Mode { Menu, Settings, Intro, Playing, Paused, Upgrade, Dying, Won }
        Mode mode;
        Mode settingsReturn;
        WaveDirector director;
        GameObject player;
        int slide;
        float slideTime, fade, endTimer;
        List<Upgrades.Def> offer;
        float savedTimeScale = 1f;

        // обучение: подсказка висит, пока игрок не сделает приём (или 12 с)
        static readonly string[] Hints =
        {
            "<b>ЛКМ</b> — удары: левой, правой, размах",
            "<b>ПКМ</b> — держи и отпусти: «Кувалда». Сильный замах поджигает",
            "<b>Shift</b> — дэш. Во время дэша удары проходят мимо",
            "<b>Пробел</b>, в воздухе <b>Ctrl</b> — удар оземь",
            "<b>Красное на снегу</b> — сюда сейчас ударят. Уходи",
            "Топливо — это жизнь. Бей, чтобы гореть. <b>V</b> — вид от третьего лица",
        };
        int hint = -1;
        float hintTime;

        void Awake()
        {
            Time.timeScale = 1f;
            GameSettings.Load();
        }

        void Start()
        {
            director = WaveDirector.Instance;
            var mv = FindAnyObjectByType<ZhdanMovement>();
            player = mv != null ? mv.gameObject : null;
            if (director != null) { director.autoStart = false; director.externalFlow = true; }
            WaveDirector.WaveCleared += OnWaveCleared;
            WaveDirector.PlayerDied += OnDied;
            WaveDirector.Victory += OnVictory;
            if (player != null)
            {
                var c = player.GetComponent<ZhdanCombat>();
                if (c != null) c.onAttackStart.AddListener(OnAttack);
                if (mv != null) { mv.onDash.AddListener(() => HintDone(2)); mv.onSlamStart.AddListener(() => HintDone(3)); }
                PlayerTarget.Find();
                Upgrades.ApplyAll(player);
            }
            GameSettings.Apply();

            if (GameProgress.SkipMenuOnce)
            {
                GameProgress.SkipMenuOnce = false;
                Begin();
            }
            else ShowMenu();
        }

        void OnDestroy()
        {
            WaveDirector.WaveCleared -= OnWaveCleared;
            WaveDirector.PlayerDied -= OnDied;
            WaveDirector.Victory -= OnVictory;
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------ состояния
        void ShowMenu()
        {
            mode = Mode.Menu;
            SetControl(false);
            CalabashMusic.Play("menu");
        }

        void NewGame()
        {
            GameProgress.Reset();
            Upgrades.ApplyAll(player);
            // вступление — катсцена казни на движке; без неё — комикс из картинок
            if (Application.CanStreamedLevelBeLoaded(ExecutionScene)) { Load(ExecutionScene); return; }
            mode = Mode.Intro;
            slide = 0;
            slideTime = 0f;
            CalabashMusic.Play("limbo");
        }

        void Begin()
        {
            var p = GameProgress.Current;
            p.introSeen = true;
            GameProgress.Save();
            mode = Mode.Playing;
            SetControl(true);
            CalabashMusic.Play("battle");
            string banner = p.deaths > 0 ? $"Смерть {p.deaths}. Ждан вернулся" : p.wave > 0 ? "Ждан вернулся" : "Отомсти им, Ждан";
            if (director != null) director.Begin(p.wave, banner);
            if (!p.tutorialDone) { hint = 0; hintTime = 0f; }
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

        void Pause(bool on)
        {
            if (on)
            {
                savedTimeScale = Time.timeScale > 0.5f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                mode = Mode.Paused;
                SetControl(false);
                AudioListener.pause = true;
            }
            else
            {
                Time.timeScale = savedTimeScale;
                mode = Mode.Playing;
                SetControl(true);
                AudioListener.pause = false;
            }
        }

        void OnWaveCleared(int wave)
        {
            var p = GameProgress.Current;
            p.wave = wave + 1;
            p.kills += director != null ? director.Kills - killsCounted : 0;
            killsCounted = director != null ? director.Kills : 0;
            GameProgress.Save();
            if (director != null && wave + 1 >= director.WaveCount) return;
            offer = Upgrades.Offer();
            if (offer.Count == 0) return;
            mode = Mode.Upgrade;
            if (director != null) director.holdBreak = true;
            SetControl(false);
        }
        int killsCounted;

        void Pick(int i)
        {
            if (offer == null || i < 0 || i >= offer.Count) return;
            Upgrades.Take(offer[i], player);
            CalabashSfx.Play2D("fire_burst", 0.9f, 1.1f);
            offer = null;
            if (director != null) director.holdBreak = false;
            mode = Mode.Playing;
            SetControl(true);
        }

        void OnDied()
        {
            var p = GameProgress.Current;
            p.deaths++;
            if (director != null) { p.kills += director.Kills - killsCounted; killsCounted = director.Kills; p.time += director.SurvivedTime; }
            GameProgress.Save();
            mode = Mode.Dying;
            endTimer = 3f;
            SetControl(false);
        }

        void OnVictory()
        {
            var p = GameProgress.Current;
            if (director != null) { p.kills += director.Kills - killsCounted; p.time += director.SurvivedTime; }
            GameProgress.Save();
            mode = Mode.Won;
            endTimer = 4f;
            CalabashMusic.Play("hell");
            CalabashMusic.Combat = false;
        }

        void OnAttack(AttackKind k)
        {
            if (k == AttackKind.Light1 || k == AttackKind.Light2 || k == AttackKind.Light3) HintDone(0);
            else if (k == AttackKind.Heavy) HintDone(1);
            else if (k == AttackKind.SlamWave) HintDone(3);
        }

        void HintDone(int i)
        {
            if (hint == i) NextHint();
        }

        void NextHint()
        {
            hint++;
            hintTime = 0f;
            if (hint >= Hints.Length)
            {
                hint = -1;
                GameProgress.Current.tutorialDone = true;
                GameProgress.Save();
            }
        }

        // ------------------------------------------------------------------ кадр
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            switch (mode)
            {
                case Mode.Playing:
                    if (Ui.Pressed("esc")) Pause(true);
                    if (hint >= 0)
                    {
                        hintTime += Time.deltaTime;
                        if (hintTime > (hint >= 4 ? 7f : 14f)) NextHint();
                    }
                    break;
                case Mode.Paused:
                    if (Ui.Pressed("esc")) Pause(false);
                    break;
                case Mode.Settings:
                    if (Ui.Pressed("esc")) CloseSettings();
                    break;
                case Mode.Intro:
                    slideTime += dt;
                    if (Ui.Pressed("esc")) Begin();
                    else if ((Ui.Pressed("next") || Ui.Clicked()) && slideTime > 0.35f)
                    {
                        slide++;
                        slideTime = 0f;
                        CalabashSfx.Play2D("cloth", 0.6f);
                        if (slide >= intro.Count) Begin();
                    }
                    break;
                case Mode.Upgrade:
                    for (int i = 0; i < 3; i++) if (Ui.Pressed((i + 1).ToString())) Pick(i);
                    break;
                case Mode.Dying:
                case Mode.Won:
                    endTimer -= dt;
                    if (endTimer < 1.2f) fade = Mathf.MoveTowards(fade, 1f, dt / 1.2f);
                    if (endTimer <= 0f)
                    {
                        GameProgress.NextAfterlife = mode == Mode.Won ? GameProgress.Afterlife.Hell : GameProgress.Afterlife.Limbo;
                        Load(AfterlifeScene);
                    }
                    break;
            }
        }

        public static void Load(string scene)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(scene);
        }

        void OpenSettings()
        {
            settingsReturn = mode;
            mode = Mode.Settings;
        }

        void CloseSettings()
        {
            GameSettings.Save();
            mode = settingsReturn;
        }

        // ------------------------------------------------------------------ экран
        void OnGUI()
        {
            GUI.depth = -10;
            float w = Screen.width, h = Screen.height, s = Ui.S;
            switch (mode)
            {
                case Mode.Menu: DrawMenu(w, h, s); break;
                case Mode.Settings: DrawSettings(w, h, s); break;
                case Mode.Intro: DrawIntro(w, h, s); break;
                case Mode.Paused: DrawPause(w, h, s); break;
                case Mode.Upgrade: DrawUpgrade(w, h, s); break;
                case Mode.Playing: DrawHint(w, h, s); break;
                case Mode.Won:
                    if (endTimer > 1.2f)
                    {
                        Ui.Fill(new Rect(0, h * 0.4f, w, 110 * s), new Color(0, 0, 0, 0.75f));
                        GUI.Label(new Rect(0, h * 0.4f, w, 110 * s), "ПАЛАЧ КОРНЕЙ МЁРТВ", Ui.Title(56, Ui.Fire));
                    }
                    break;
            }
            if (fade > 0f) Ui.Shade(mode == Mode.Dying ? Color.white : Color.black, fade);
        }

        void DrawMenu(float w, float h, float s)
        {
            Ui.Shade(Color.black, 0.35f);
            Ui.Fill(new Rect(0, h * 0.16f, w, 190 * s), new Color(0, 0, 0, 0.88f));
            GUI.Label(new Rect(0, h * 0.16f, w, 140 * s), "ЖДАН", Ui.Title(110, Ui.Paper));
            GUI.Label(new Rect(0, h * 0.16f + 125 * s, w, 50 * s), "его хотели сжечь", Ui.Title(26, Ui.Fire));

            float bw = 360 * s, bh = 62 * s, x = w * 0.5f - bw * 0.5f, y = h * 0.16f + 230 * s;
            var p = GameProgress.Current;
            if (GameProgress.CanContinue)
            {
                if (Ui.Button(new Rect(x, y, bw, bh), $"Продолжить · волна {p.wave + 1}")) Begin();
                y += bh + 16 * s;
            }
            if (Ui.Button(new Rect(x, y, bw, bh), "Новая игра")) NewGame();
            y += bh + 16 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "Настройки")) OpenSettings();
            y += bh + 16 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "Выход")) Quit();
            y += bh + 24 * s;
            if (p.bestWave > 0 || p.deaths > 0)
                GUI.Label(new Rect(0, y, w, 30 * s), $"лучшее: волна {Mathf.Max(p.bestWave, p.wave) + 1} · смертей: {p.deaths}", Ui.Text(20, Ui.Paper, TextAnchor.MiddleCenter));
        }

        void DrawSettings(float w, float h, float s)
        {
            Ui.Shade(Ui.Paper, 0.7f);
            var r = new Rect(w * 0.5f - 300 * s, h * 0.5f - 210 * s, 600 * s, 420 * s);
            Ui.Panel(r);
            GUI.Label(new Rect(r.x, r.y + 16 * s, r.width, 50 * s), "Настройки", Ui.Title(36, Ui.Ink));
            float x = r.x + 40 * s, cw = r.width - 80 * s, y = r.y + 90 * s, rh = 46 * s;
            GameSettings.Volume = Ui.Slider(new Rect(x, y, cw, rh), "Громкость", GameSettings.Volume, 0f, 1f); y += rh + 10 * s;
            GameSettings.Music = Ui.Slider(new Rect(x, y, cw, rh), "Музыка", GameSettings.Music, 0f, 1f); y += rh + 10 * s;
            GameSettings.Sensitivity = Ui.Slider(new Rect(x, y, cw, rh), "Мышь", GameSettings.Sensitivity, 0.2f, 3f); y += rh + 10 * s;
            if (Ui.Button(new Rect(x, y, cw, rh), GameSettings.Gloom ? "Манга-фильтр: вкл" : "Манга-фильтр: выкл")) GameSettings.Gloom = !GameSettings.Gloom;
            GameSettings.Apply();
            if (Ui.Button(new Rect(r.x + r.width * 0.5f - 120 * s, r.yMax - 70 * s, 240 * s, 52 * s), "Готово")) CloseSettings();
        }

        void DrawPause(float w, float h, float s)
        {
            Ui.Shade(Ui.Paper, 0.6f);
            GUI.Label(new Rect(0, h * 0.22f, w, 90 * s), "ПАУЗА", Ui.Title(72, Ui.Ink));
            float bw = 340 * s, bh = 60 * s, x = w * 0.5f - bw * 0.5f, y = h * 0.22f + 120 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "Продолжить")) Pause(false);
            y += bh + 14 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "Настройки")) OpenSettings();
            y += bh + 14 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "В главное меню"))
            {
                GameProgress.Save();
                Load(VillageScene);
            }
            y += bh + 14 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "Выход")) Quit();
            var p = GameProgress.Current;
            GUI.Label(new Rect(0, y + bh + 20 * s, w, 30 * s), $"смертей: {p.deaths} · улучшений: {p.upgrades.Count}", Ui.Text(20, Ui.Ink, TextAnchor.MiddleCenter));
        }

        void DrawIntro(float w, float h, float s)
        {
            if (intro.Count == 0) return;
            var sl = intro[Mathf.Min(slide, intro.Count - 1)];
            Ui.Shade(Ui.Paper, 1f);
            float a = Mathf.Clamp01(slideTime * 2.5f);
            float ih = h * 0.78f, iw = sl.image != null ? ih * sl.image.width / sl.image.height : 0f;
            float x0 = w * 0.5f - (iw + 640 * s) * 0.5f;
            if (sl.image != null)
            {
                var ir = new Rect(x0, h * 0.11f, iw, ih);
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                GUI.DrawTexture(ir, sl.image, ScaleMode.ScaleToFit);
                GUI.color = old;
                Ui.Frame(ir, Ui.Ink, 4f);
            }
            var tr = new Rect(x0 + iw + 50 * s, h * 0.3f, 590 * s, h * 0.45f);
            int chars = Mathf.Clamp(Mathf.FloorToInt(slideTime * 45f), 0, sl.text.Length);   // печатается по буквам
            GUI.Label(tr, sl.text.Substring(0, chars), Ui.Text(34, Ui.Ink));
            Ui.Fill(new Rect(tr.x, tr.y - 18 * s, 80 * s, 6 * s), Ui.Blood);
            GUI.Label(new Rect(0, h - 50 * s, w - 30 * s, 40 * s), $"{slide + 1}/{intro.Count}   Пробел — дальше · Esc — пропустить",
                      Ui.Text(18, Ui.Gray, TextAnchor.MiddleRight));
        }

        void DrawUpgrade(float w, float h, float s)
        {
            if (offer == null) return;
            Ui.Shade(Color.black, 0.55f);
            GUI.Label(new Rect(0, h * 0.14f, w, 70 * s), "ОГОНЬ РАСТЁТ", Ui.Title(56, Ui.FireCore));
            GUI.Label(new Rect(0, h * 0.14f + 70 * s, w, 36 * s), "выбери одно · 1, 2, 3 или мышью", Ui.Title(22, Ui.Paper));
            float cw = 330 * s, ch = 300 * s, gap = 30 * s;
            float x = w * 0.5f - (offer.Count * cw + (offer.Count - 1) * gap) * 0.5f, y = h * 0.34f;
            for (int i = 0; i < offer.Count; i++)
            {
                var r = new Rect(x + i * (cw + gap), y, cw, ch);
                var d = offer[i];
                bool hover = r.Contains(Event.current.mousePosition);
                Ui.Panel(r, 1f);
                if (hover) Ui.Frame(r, Ui.Fire, 5f);
                GUI.Label(new Rect(r.x, r.y + 18 * s, r.width, 40 * s), (i + 1).ToString(), Ui.Title(34, Ui.Blood));
                GUI.Label(new Rect(r.x + 16 * s, r.y + 66 * s, r.width - 32 * s, 70 * s), d.title, Ui.Title(30, Ui.Ink));
                GUI.Label(new Rect(r.x + 24 * s, r.y + 150 * s, r.width - 48 * s, 120 * s), d.text, Ui.Text(22, Ui.Ink, TextAnchor.UpperCenter));
                int have = Upgrades.Count(d.id);
                if (d.max > 1) GUI.Label(new Rect(r.x, r.yMax - 40 * s, r.width, 30 * s), $"{have}/{d.max}", Ui.Text(18, Ui.Gray, TextAnchor.MiddleCenter));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Pick(i);
            }
        }

        void DrawHint(float w, float h, float s)
        {
            if (hint < 0 || hint >= Hints.Length) return;
            var r = new Rect(w * 0.5f - 340 * s, h - 150 * s, 680 * s, 54 * s);
            Ui.Panel(r, 0.9f);
            Ui.Fill(new Rect(r.x, r.y, 8 * s, r.height), Ui.Fire);
            GUI.Label(new Rect(r.x + 20 * s, r.y, r.width - 30 * s, r.height), Hints[hint], Ui.Text(22, Ui.Ink, TextAnchor.MiddleLeft));
        }

        static void Quit()
        {
            GameProgress.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
