using System.Collections;
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
    /// - Рай: катсцена — огонь гаснет, каравай на постаменте, Лимб уходит в свет, камера поднимается. Конец.
    /// - Ад (после смерти палача Корнея): катсцена — Ждан горит в кольце огня, вокруг убитые им жители,
    ///   Лимб отворачивается, камера уходит вверх. Конец.
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

        [Header("Портрет Лимба в разговоре")]
        public Texture2D limboImage;

        [Header("Концовки-катсцены (ставит GameSetup)")]
        [Tooltip("Камера концовок; при концовке герой с камерой выключается")] public Camera endCam;
        [Tooltip("Ждан со стороны: модель героя с анимацией")] public Transform actor;
        public ParticleSystem actorFire;
        public Light actorLight;
        [Tooltip("Рай: постамент с караваем, светлые искры")] public GameObject heavenProps;
        public Transform bread;
        [Tooltip("Ад: убитые жители стоят в огне")] public GameObject hellCrowd;

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
                    subTime += dt;
                    bool key = Ui.Pressed("next") || Ui.Clicked() || Ui.Pressed("esc");
                    if (finalCard && key && endTime > 1f)
                    {
                        GameProgress.Reset();          // игра пройдена — заново с меню
                        FadeTo(GameFlow.MenuScene);
                    }
                    else if (!finalCard && key && endTime > 0.6f) skip = true;   // пропустить катсцену
                    break;
            }
        }

        void Answer(bool keepKilling)
        {
            if (keepKilling)
            {
                CalabashSfx.Play2D("fire_burst", 1f, 0.8f);
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
            endTime = 0f;
            SetControl(false);
            StartCoroutine(kind == GameProgress.Afterlife.Hell ? HellScene() : HeavenScene());
        }

        // ------------------------------------------------------------------ концовки-катсцены
        bool skip, finalCard;
        float endFade;
        Color endFadeColor = Color.white;
        string subtitle = "";
        float subTime;

        /// <summary>Переключиться с героя на камеру концовки и показать Ждана со стороны.</summary>
        void StageOn()
        {
            if (endCam == null || actor == null) return;
            Vector3 pos = player != null ? player.transform.position : Vector3.zero;
            Vector3 f = limbo != null ? limbo.position - pos : Vector3.forward;
            f.y = 0f;
            actor.SetPositionAndRotation(pos, Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward));
            actor.gameObject.SetActive(true);
            endCam.gameObject.SetActive(true);
            if (player != null) player.SetActive(false);
        }

        IEnumerator HeavenScene()
        {
            StageOn();
            if (limbo != null) limbo.gameObject.SetActive(true);
            if (heavenProps != null) heavenProps.SetActive(true);
            Vector3 c = actor != null ? actor.position : Vector3.zero;
            Vector3 f = actor != null ? actor.forward : Vector3.forward, r = Vector3.Cross(Vector3.up, f);
            Anim(LimboAnim, "Idle");
            StartCoroutine(FireTo(0f, 4f));                           // огонь выходит из него
            endFadeColor = Color.white; endFade = 1f;
            StartCoroutine(EndFade(0f, 1.5f));

            Say(ending[0]);
            yield return Orbit(c + Vector3.up * 1.4f, 4.2f, 1.7f, -150f, -100f, 7f);

            Say(ending[1]);
            Vector3 b = bread != null ? bread.position : c + f * 5f + r * 3f;
            yield return Shot(6f, b + Vector3.up * 0.45f - f * 1.7f + r * 0.7f, b + Vector3.up * 0.3f - f * 1.1f + r * 0.4f, b, b + Vector3.up * 0.05f);

            Say(ending[2]);
            StartCoroutine(LimboLeaves(f));
            yield return Shot(7f, c - f * 2.2f + r * 0.7f + Vector3.up * 1.8f, c - f * 1.6f + r * 0.5f + Vector3.up * 1.7f,
                              c + f * 8f + Vector3.up * 1.5f, c + f * 14f + Vector3.up * 1.6f);

            Say("");
            yield return Crane(c, 6f, Color.white);
            yield return FinalCard();
        }

        IEnumerator HellScene()
        {
            StageOn();
            if (hellCrowd != null)
            {
                hellCrowd.SetActive(true);
                foreach (var a in hellCrowd.GetComponentsInChildren<Animator>()) Anim(a, Random.value < 0.7f ? "Cower" : "Pray", Random.value);
            }
            Vector3 c = actor != null ? actor.position : Vector3.zero;
            Vector3 f = actor != null ? actor.forward : Vector3.forward, r = Vector3.Cross(Vector3.up, f);
            if (limbo != null)
            {
                limbo.gameObject.SetActive(true);
                limbo.rotation = Quaternion.LookRotation(f);               // спиной к Ждану
            }
            StartCoroutine(FireTo(1f, 0.5f));
            endFadeColor = Color.black; endFade = 1f;
            StartCoroutine(EndFade(0f, 1.5f));

            Say(ending[0]);
            yield return Orbit(c + Vector3.up * 1.3f, 3.6f, 0.6f, 160f, 230f, 7f);

            Say(ending[1]);
            yield return Pan(c + Vector3.up * 1.7f, f, -70f, 70f, 8f);

            Say(ending[2]);
            Vector3 l = limbo != null ? limbo.position : c + f * 14f;
            yield return Shot(6f, c - f * 1.8f + r * 0.8f + Vector3.up * 1.9f, c - f * 1.2f + r * 0.6f + Vector3.up * 1.8f,
                              l + Vector3.up * 1.4f, l + Vector3.up * 1.6f);

            Say(ending[3]);
            StartCoroutine(Flare());
            yield return Shot(6f, c + f * 2.6f + Vector3.up * 1.7f, c + f * 1.7f + Vector3.up * 1.75f, c + Vector3.up * 1.6f, c + Vector3.up * 1.7f);

            Say("");
            yield return Crane(c, 6f, Color.black);
            yield return FinalCard();
        }

        IEnumerator FinalCard()
        {
            finalCard = true;
            endTime = 0f;
            Say("");
            yield break;
        }

        Animator LimboAnim => limbo != null ? limbo.GetComponentInChildren<Animator>() : null;

        IEnumerator LimboLeaves(Vector3 f)
        {
            if (limbo == null) yield break;
            yield return new WaitForSecondsRealtime(1.5f);
            limbo.rotation = Quaternion.LookRotation(f);
            Anim(LimboAnim, "Walk");
            for (float t = 0f; t < 8f && !skip; t += Time.unscaledDeltaTime)
            {
                limbo.position += f * 1.1f * Time.unscaledDeltaTime;
                yield return null;
            }
        }

        IEnumerator FireTo(float to, float dur)
        {
            if (actorFire == null) yield break;
            var em = actorFire.emission;
            float rate = 90f;
            if (!actorFire.isPlaying) actorFire.Play();
            float from = to > 0.5f ? 0.3f : 1f;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Lerp(from, to, t / dur);
                em.rateOverTime = rate * k;
                if (actorLight != null) actorLight.intensity = 14f * k;
                yield return null;
            }
            em.rateOverTime = rate * to;
            if (actorLight != null) actorLight.intensity = 14f * to;
        }

        IEnumerator Flare()
        {
            if (actorFire == null) yield break;
            var em = actorFire.emission;
            for (float t = 0f; t < 5f && !skip; t += Time.unscaledDeltaTime)
            {
                float k = 1f + 1.6f * Mathf.Clamp01(t / 3f);
                em.rateOverTime = 90f * k;
                if (actorLight != null) actorLight.intensity = 14f * k;
                yield return null;
            }
        }

        // камера
        IEnumerator Shot(float dur, Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo)
        {
            for (float t = 0f; t < dur && !skip; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                Place(Vector3.Lerp(from, to, k), Vector3.Lerp(lookFrom, lookTo, k));
                yield return null;
            }
        }

        /// <summary>Облёт вокруг center на радиусе radius, высота height над центром, угол от a0 до a1 (от «лица»).</summary>
        IEnumerator Orbit(Vector3 center, float radius, float height, float a0, float a1, float dur)
        {
            Vector3 f = actor != null ? actor.forward : Vector3.forward;
            for (float t = 0f; t < dur && !skip; t += Time.unscaledDeltaTime)
            {
                float a = Mathf.Lerp(a0, a1, Mathf.SmoothStep(0f, 1f, t / dur));
                Vector3 d = Quaternion.AngleAxis(a + 180f, Vector3.up) * f;
                Place(center + d * radius + Vector3.up * height, center);
                yield return null;
            }
        }

        /// <summary>Камера стоит у Ждана и медленно поворачивается, оглядывая круг.</summary>
        IEnumerator Pan(Vector3 eye, Vector3 f, float a0, float a1, float dur)
        {
            for (float t = 0f; t < dur && !skip; t += Time.unscaledDeltaTime)
            {
                float a = Mathf.Lerp(a0, a1, Mathf.SmoothStep(0f, 1f, t / dur));
                Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * f;
                Place(eye + Vector3.up * 1.3f - d * 0.5f, eye + d * 10f - Vector3.up * 0.6f);   // над головой Ждана
                yield return null;
            }
        }

        /// <summary>Камера уходит вверх, Ждан остаётся внизу маленьким, экран заливается цветом.</summary>
        IEnumerator Crane(Vector3 c, float dur, Color to)
        {
            Vector3 f = actor != null ? actor.forward : Vector3.forward;
            Vector3 a = c - f * 3f + Vector3.up * 3f, b = c - f * 9f + Vector3.up * 26f;
            endFadeColor = to;
            StartCoroutine(EndFade(1f, dur, dur * 0.55f));
            for (float t = 0f; t < dur && !skip; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                Place(Vector3.Lerp(a, b, k), c + Vector3.up * 0.8f);
                yield return null;
            }
            endFade = 1f;
        }

        IEnumerator EndFade(float to, float dur, float delay = 0f)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            float from = endFade, d = Mathf.Max(0.01f, dur - delay);
            for (float t = 0f; t < d; t += Time.unscaledDeltaTime)
            {
                endFade = Mathf.Lerp(from, to, t / d);
                yield return null;
            }
            endFade = to;
        }

        void Place(Vector3 pos, Vector3 look)
        {
            var c = endCam != null && endCam.isActiveAndEnabled ? endCam : cam;
            if (c == null) return;
            c.transform.position = pos;
            if ((look - pos).sqrMagnitude > 1e-4f) c.transform.rotation = Quaternion.LookRotation(look - pos);
        }

        void Say(string text) { subtitle = text; subTime = 0f; }

        static void Anim(Animator a, string state, float offset = 0f)
        {
            if (a == null || !a.isActiveAndEnabled) return;
            int h = Animator.StringToHash(state);
            if (!a.HasState(0, h)) h = Animator.StringToHash("Idle");
            if (a.HasState(0, h)) a.CrossFadeInFixedTime(h, 0.3f, 0, offset);
        }

        void SetupHeaven()
        {
            kind = GameProgress.Afterlife.Heaven;
            CalabashMusic.Play("heaven");
            if (cam != null) cam.backgroundColor = HeavenSky;
            if (endCam != null) endCam.backgroundColor = HeavenSky;
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
            if (endCam != null) endCam.backgroundColor = HellSky;
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
            // кинополосы
            float bar = h * 0.11f;
            if (!finalCard)
            {
                Ui.Fill(new Rect(0, 0, w, bar), Color.black);
                Ui.Fill(new Rect(0, h - bar, w, bar), Color.black);
            }
            if (endFade > 0f) Ui.Shade(endFadeColor, endFade);
            if (!finalCard)
            {
                if (!string.IsNullOrEmpty(subtitle))
                {
                    int chars = Mathf.Clamp(Mathf.FloorToInt(subTime * 40f), 0, subtitle.Length);
                    GUI.Label(new Rect(w * 0.1f, h - bar + 12 * s, w * 0.8f, bar), subtitle.Substring(0, chars), Ui.Text(30, Ui.Paper, TextAnchor.UpperCenter));
                }
                GUI.Label(new Rect(0, h - 36 * s, w - 24 * s, 30 * s), "Пробел — пропустить", Ui.Text(16, Ui.Gray, TextAnchor.MiddleRight));
                return;
            }
            string t = ending[ending.Count - 1];
            string[] parts = t.Split('\n');
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(endTime * 1.2f));
            GUI.Label(new Rect(0, h * 0.36f, w, 120 * s), parts[0], Ui.Title(110, hell ? Ui.Blood : Ui.Ink));
            if (parts.Length > 1) GUI.Label(new Rect(0, h * 0.36f + 125 * s, w, 60 * s), parts[1], Ui.Title(38, hell ? Ui.Fire : Ui.Gray));
            GUI.color = old;
            if (endTime > 1f) GUI.Label(new Rect(0, h - 60 * s, w, 40 * s), "Пробел — в главное меню", Ui.Text(20, Ui.Gray, TextAnchor.MiddleCenter));
        }
    }
}
