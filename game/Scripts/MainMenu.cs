using Calabash.Audio;
using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Главное меню — своя сцена (MainMenu), до игры. Ночь, догорающий костёр, у него горящий Ждан;
    /// камера медленно ходит дугой вокруг него. Слева название и кнопки: продолжить, новая игра
    /// (катсцена казни → деревня), настройки, выход. Переход — через затемнение.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public const string SceneName = "MainMenu";

        public Camera cam;
        [Tooltip("Вокруг кого ходит камера (Ждан)")]
        public Transform focus;
        public Vector3 pyre;
        [Tooltip("Куда смотрит Ждан (в сторону деревни)")]
        public Vector3 front = Vector3.forward;
        public float radius = 4.6f;
        public float height = 1.35f;
        [Tooltip("Размах дуги камеры, градусы")]
        public float arc = 38f;
        [Tooltip("Период качания дуги, секунды")]
        public float period = 70f;

        enum Mode { Main, Settings, Leaving }
        Mode mode;
        float time, fade = 1f, titleIn;
        string target;

        void Awake()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            GameSettings.Load();
        }

        void Start()
        {
            GameSettings.Apply();
            CalabashMusic.Play("menu");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            foreach (var ps in FindObjectsByType<ParticleSystem>())
                if (!ps.isPlaying && ps.transform.parent != null && ps.transform.parent.GetComponent<ParticleSystem>() == null) ps.Play(true);
            var anim = focus != null ? focus.GetComponentInChildren<Animator>() : null;
            if (anim != null)                                           // стоит, переминается
                foreach (var p in anim.parameters)
                    if (p.type == AnimatorControllerParameterType.Float) anim.SetFloat(p.name, 0f);
            Place(0f);
        }

        /// <summary>Камера на дуге вокруг Ждана; Ждан справа в кадре, слева место под меню.</summary>
        public void Place(float t)
        {
            if (cam == null || focus == null) return;
            Vector3 f = front; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            float a = 25f + Mathf.Sin(t * 2f * Mathf.PI / period) * arc * 0.5f;
            Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * f;
            Vector3 c = focus.position + Vector3.up * 1.15f;
            Vector3 pos = c + dir * radius + Vector3.up * (height - 1.15f + 0.25f * Mathf.Sin(t * 0.21f));
            Vector3 right = Vector3.Cross(Vector3.up, c - pos).normalized;
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation((c - right * 1.25f) - pos);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            time += dt;
            titleIn += dt;
            Place(time);
            if (mode == Mode.Leaving)
            {
                fade += dt * 1.4f;
                if (fade >= 1f) GameFlow.Load(target);
                return;
            }
            fade = Mathf.Max(0f, fade - dt * 0.7f);
            if (mode == Mode.Settings && Ui.Pressed("esc")) CloseSettings();
        }

        void Leave(string scene)
        {
            target = scene;
            mode = Mode.Leaving;
            CalabashSfx.Play2D("fire_burst", 0.8f, 0.7f);
        }

        public void Continue() => Leave(GameFlow.VillageScene);

        public void NewGame()
        {
            GameProgress.Reset();
            Leave(Application.CanStreamedLevelBeLoaded(GameFlow.ExecutionScene) ? GameFlow.ExecutionScene : GameFlow.VillageScene);
        }

        public void OpenSettings() => mode = Mode.Settings;

        public void CloseSettings()
        {
            GameSettings.Save();
            mode = Mode.Main;
        }

        void OnGUI()
        {
            GUI.depth = -10;
            float w = Screen.width, h = Screen.height, s = Ui.S;
            // тёмная полоса слева под меню, к центру уходит в ноль
            const int n = 96;
            float sw = w * 0.6f / n;
            for (int i = 0; i < n; i++)
            {
                float k = 1f - i / (float)n;
                Ui.Fill(new Rect(i * sw, 0, sw + 1, h), new Color(0, 0, 0, 0.85f * k * k));
            }

            if (mode == Mode.Settings) GameSettings.DrawPanel(w, h, s, CloseSettings);
            else DrawMain(w, h, s);

            if (fade > 0f) Ui.Shade(Color.black, Mathf.Clamp01(fade));
        }

        void DrawMain(float w, float h, float s)
        {
            float x = 110 * s, a = Mathf.Clamp01((titleIn - 0.6f) * 0.8f);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            var ts = new GUIStyle(Ui.Title(150, Ui.Paper)); ts.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x, h * 0.12f, w * 0.5f, 170 * s), "ЖДАН", ts);
            Ui.Fill(new Rect(x + 6 * s, h * 0.12f + 168 * s, 210 * s, 7 * s), Ui.Fire);
            var sub = new GUIStyle(Ui.Title(28, Ui.Fire)); sub.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x + 4 * s, h * 0.12f + 182 * s, w * 0.5f, 44 * s), "его хотели сжечь", sub);
            GUI.color = old;

            float bw = 380 * s, bh = 62 * s, y = h * 0.12f + 280 * s;
            bool live = mode == Mode.Main && a > 0.5f;
            var p = GameProgress.Current;
            if (GameProgress.CanContinue)
            {
                if (Ui.Button(new Rect(x, y, bw, bh), $"Продолжить · волна {p.wave + 1}", true, live)) Continue();
                y += bh + 16 * s;
            }
            if (Ui.Button(new Rect(x, y, bw, bh), "Новая игра", true, live)) NewGame();
            y += bh + 16 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "Настройки", true, live)) OpenSettings();
            y += bh + 16 * s;
            if (Ui.Button(new Rect(x, y, bw, bh), "Выход", true, live)) Quit();
            y += bh + 26 * s;
            if (p.bestWave > 0 || p.deaths > 0)
                GUI.Label(new Rect(x, y, bw, 30 * s), $"лучшее: волна {Mathf.Max(p.bestWave, p.wave) + 1} · смертей: {p.deaths}",
                          Ui.Text(20, Ui.Gray, TextAnchor.MiddleLeft));
            GUI.Label(new Rect(0, h - 44 * s, w - 30 * s, 34 * s), "Calabash · 2026", Ui.Text(16, Ui.Gray, TextAnchor.MiddleRight));
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
