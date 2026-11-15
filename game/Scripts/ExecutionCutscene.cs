using System.Collections;
using Calabash.Audio;
using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Катсцена «Казнь» на движке: ночная площадь, толпа с факелами, Ждан у столба на хворосте.
    /// Гаврила кричит о краже, поп Никодим проклинает, царь велит сжечь, палач Корней поджигает костёр.
    /// Камера идёт по планам (Shot), внизу субтитры, сверху и снизу кинополосы. Esc — пропустить.
    /// В конце — в деревню, сразу в бой. Сцену собирает game/Editor/CutsceneSetup.
    /// Время — реальное (unscaled): темп катсцены не зависит от FPS и паузы.
    /// </summary>
    public class ExecutionCutscene : MonoBehaviour
    {
        public Camera cam;
        [Tooltip("Основание столба (центр костра)")] public Transform stake;
        [Tooltip("Направление от столба к толпе")] public Vector3 front = Vector3.forward;

        public Animator zhdan;
        public Animator gavrila;
        public Animator nikodim;
        public Animator king;
        public Animator korney;
        public Animator[] crowd = new Animator[0];

        [Tooltip("Факел в руке Корнея")] public Transform korneyTorch;
        public Vector3 korneyFrom, korneyTo;

        public ParticleSystem pyreFire;
        public ParticleSystem pyreEmbers;
        public ParticleSystem pyreSmoke;
        public Light pyreLight;
        public AudioSource pyreSound;

        float fade;            // 0 — видно, 1 — залито fadeColor
        Color fadeColor = Color.black;
        string subtitle = "", bigText = "";
        float subTime, bigTime;
        bool done;

        Vector3 R => Vector3.Cross(Vector3.up, front).normalized;
        Vector3 S => stake != null ? stake.position : Vector3.zero;

        void Awake()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            GameSettings.Load();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
        }

        void Start()
        {
            GameSettings.Apply();
            CalabashMusic.Play("menu");
            if (pyreFire != null) pyreFire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (pyreEmbers != null) pyreEmbers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (pyreSmoke != null) pyreSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (pyreLight != null) pyreLight.intensity = 0f;
            if (korney != null) korney.transform.position = korneyFrom;
            foreach (var a in crowd) Anim(a, Random.value < 0.35f ? "Talk" : "Idle", Random.value);
            Anim(gavrila, "Idle");
            Anim(nikodim, "Idle");
            Anim(king, "Idle");
            Anim(korney, "Idle");
            fade = 1f;
            StartCoroutine(Run());
        }

        void Update()
        {
            subTime += Time.unscaledDeltaTime;
            bigTime += Time.unscaledDeltaTime;
            if (!done && (Ui.Pressed("esc"))) Finish();
        }

        // ------------------------------------------------------------------ сценарий
        IEnumerator Run()
        {
            Vector3 up = Vector3.up, F = front, r = R, s = S;

            // 1. площадь сверху: толпа с факелами вокруг столба
            Say("Зима. Голодный год.\nУ пекаря Гаврилы пропал каравай.");
            StartCoroutine(Fade(0f, 2f));
            yield return Shot(7f, s - r * 15f + F * 17f + up * 10f, s - r * 9f + F * 11f + up * 5f, s + up * 2f, s + up * 1.6f);

            // 2. Гаврила кричит
            Anim(gavrila, "Talk");
            Say("— Вор! Каравай украл Ждан!\nЕго поймали у реки с хлебом за пазухой.");
            yield return Close(gavrila, 5.5f, 2.4f, 1.7f, 0.7f);

            // 3. Ждан у столба, снизу
            Say("Ждана привязали к столбу на площади.\nПод ноги сложили хворост.");
            yield return Shot(6f, s + F * 4.6f + r * 1.2f + up * 1.4f, s + F * 3.2f + r * 0.6f + up * 1.6f, s + up * 1.6f, s + up * 1.9f);

            // 4. поп Никодим проклинает, толпа молится
            Anim(nikodim, "Talk");
            foreach (var a in crowd) Anim(a, Random.value < 0.5f ? "Pray" : "Idle", Random.value);
            Say("Поп Никодим сказал: вор и бесово отродье.\nНа площади все кивали.");
            yield return Close(nikodim, 6f, 2.6f, 1.8f, -0.8f);

            // 5. царь
            Anim(king, "Talk");
            Say("Царь даже не поднял глаз.\n— Сжечь.");
            yield return Close(king, 5f, 3.4f, 1.8f, 0.6f, lookUp: 0.2f);

            // 6. Корней идёт с факелом
            Anim(korney, "Walk");
            Say("Палач Корней взял факел.");
            yield return Track(korney != null ? korney.transform : null, korneyFrom, korneyTo, 7f);
            Anim(korney, "Idle");

            // 7. поджог
            Anim(korney, "Throw");
            Say("И поднёс его к хворосту.");
            StartCoroutine(ThrowTorch(0.45f));
            StartCoroutine(Ignite(1.0f));
            yield return Shot(5f, s + F * 10f - r * 4f + up * 3f, s + F * 8.5f - r * 3f + up * 2.6f, s + up * 1.5f, s + up * 1.8f);

            // 8. Ждан горит
            foreach (var a in crowd) Anim(a, Random.value < 0.6f ? "Cower" : "Idle", Random.value);
            Anim(gavrila, "Cower");
            Anim(zhdan, "Hit");
            CalabashMusic.Play("boss");
            Say("Ждан горел долго.\nТолпа молчала.");
            yield return Shot(7f, s + F * 4.5f + up * 2.2f, s + F * 3.2f + up * 2.4f, s + up * 2.2f, s + up * 2.4f);

            // 9. белая вспышка, текст
            Say("");
            fadeColor = Color.white;
            yield return Fade(1f, 1.8f);
            Big("Ждан сгорел.\nНо огонь не ушёл из него.");
            yield return new WaitForSecondsRealtime(4f);
            fadeColor = Color.black;
            Big("Теперь горит деревня.");
            yield return new WaitForSecondsRealtime(3.2f);
            Finish();
        }

        void Finish()
        {
            if (done) return;
            done = true;
            var p = GameProgress.Current;
            p.introSeen = true;
            GameProgress.Save();
            GameFlow.Load(GameFlow.VillageScene);
        }

        // ------------------------------------------------------------------ планы
        /// <summary>Камера плавно едет из from в to и переводит взгляд с lookFrom на lookTo.</summary>
        IEnumerator Shot(float dur, Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo)
        {
            for (float t = 0f; t < dur && !done; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                Place(Vector3.Lerp(from, to, k), Vector3.Lerp(lookFrom, lookTo, k));
                yield return null;
            }
        }

        /// <summary>Крупный план персонажа: камера перед лицом, медленный наезд.</summary>
        IEnumerator Close(Animator who, float dur, float dist, float height, float sideOff, float lookUp = 0f)
        {
            if (who == null) { yield return new WaitForSecondsRealtime(dur); yield break; }
            Transform t = who.transform.parent != null ? who.transform.parent : who.transform;
            Vector3 head = t.position + Vector3.up * 1.6f;
            Vector3 fwd = t.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Vector3 a = t.position + fwd * dist + side * sideOff + Vector3.up * height;
            Vector3 b = t.position + fwd * (dist * 0.75f) + side * (sideOff * 0.8f) + Vector3.up * height;
            yield return Shot(dur, a, b, head + Vector3.up * lookUp, head + Vector3.up * lookUp);
        }

        /// <summary>Камера идёт сбоку и чуть позади, пока target идёт из from в to.</summary>
        IEnumerator Track(Transform target, Vector3 from, Vector3 to, float dur)
        {
            if (target == null) { yield return new WaitForSecondsRealtime(dur); yield break; }
            Vector3 dir = (to - from); dir.y = 0f; dir.Normalize();
            target.rotation = Quaternion.LookRotation(dir);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (float t = 0f; t < dur && !done; t += Time.unscaledDeltaTime)
            {
                float k = t / dur;
                target.position = Vector3.Lerp(from, to, k);
                Vector3 p = target.position - dir * 3.2f + side * 0.6f + Vector3.up * 2.3f;
                Place(p, Vector3.Lerp(target.position + Vector3.up * 1.5f, S + Vector3.up * 2f, k * 0.6f));
                yield return null;
            }
            target.position = to;
        }

        void Place(Vector3 pos, Vector3 look)
        {
            if (cam == null) return;
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(look - pos);
        }

        // ------------------------------------------------------------------ огонь
        IEnumerator ThrowTorch(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (korneyTorch == null) yield break;
            korneyTorch.SetParent(null, true);
            Vector3 a = korneyTorch.position, b = S + Vector3.up * 0.5f + front * 0.6f;
            for (float t = 0f; t < 0.55f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.55f;
                korneyTorch.position = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
                korneyTorch.Rotate(Vector3.right, 720f * Time.unscaledDeltaTime, Space.Self);
                yield return null;
            }
            korneyTorch.position = b;
        }

        IEnumerator Ignite(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            CalabashSfx.Play("fire_burst", S + Vector3.up, 1f, 0.7f);
            if (pyreFire != null) pyreFire.Play();
            if (pyreEmbers != null) pyreEmbers.Play();
            if (pyreSmoke != null) pyreSmoke.Play();
            if (pyreSound != null) { pyreSound.volume = 0f; pyreSound.Play(); }
            var em = pyreFire != null ? pyreFire.emission : default;
            float rate = pyreFire != null ? em.rateOverTime.constant : 0f;
            for (float t = 0f; t < 6f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / 4.5f);
                if (pyreFire != null) em.rateOverTime = rate * Mathf.Lerp(0.15f, 1f, k);
                if (pyreLight != null)
                    pyreLight.intensity = Mathf.Lerp(2f, 30f, k) * (0.85f + 0.3f * Mathf.PerlinNoise(Time.unscaledTime * 8f, 2.3f));
                if (pyreSound != null) pyreSound.volume = k * GameSettings.Volume;
                yield return null;
            }
            while (!done)
            {
                if (pyreLight != null) pyreLight.intensity = 30f * (0.85f + 0.3f * Mathf.PerlinNoise(Time.unscaledTime * 8f, 2.3f));
                yield return null;
            }
        }

        // ------------------------------------------------------------------ экран
        IEnumerator Fade(float to, float dur)
        {
            float from = fade;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                fade = Mathf.Lerp(from, to, t / dur);
                yield return null;
            }
            fade = to;
        }

        void Say(string text) { subtitle = text; subTime = 0f; }
        void Big(string text) { bigText = text; bigTime = 0f; fade = 1f; }

        static void Anim(Animator a, string state, float offset = 0f)
        {
            if (a == null || !a.isActiveAndEnabled) return;
            int h = Animator.StringToHash(state);
            if (!a.HasState(0, h)) h = Animator.StringToHash("Idle");
            if (a.HasState(0, h)) a.CrossFadeInFixedTime(h, 0.3f, 0, offset);
        }

        void OnGUI()
        {
            GUI.depth = -20;
            float w = Screen.width, h = Screen.height, s = Ui.S;
            // кинополосы
            float bar = h * 0.11f;
            Ui.Fill(new Rect(0, 0, w, bar), Color.black);
            Ui.Fill(new Rect(0, h - bar, w, bar), Color.black);

            if (fade > 0f) Ui.Shade(fadeColor, fade);

            if (!string.IsNullOrEmpty(subtitle) && fade < 0.9f)
            {
                int chars = Mathf.Clamp(Mathf.FloorToInt(subTime * 40f), 0, subtitle.Length);
                GUI.Label(new Rect(0, h - bar + 12 * s, w, bar), subtitle.Substring(0, chars), Ui.Text(30, Ui.Paper, TextAnchor.UpperCenter));
            }
            if (!string.IsNullOrEmpty(bigText) && fade > 0.5f)
            {
                Color ink = fadeColor.grayscale > 0.5f ? Ui.Ink : Ui.Paper;
                var st = Ui.Title(44, ink);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01(bigTime * 1.5f));
                GUI.Label(new Rect(0, h * 0.35f, w, h * 0.3f), bigText, st);
                GUI.color = old;
            }
            GUI.Label(new Rect(0, h - 36 * s, w - 24 * s, 30 * s), "Esc — пропустить", Ui.Text(16, Ui.Gray, TextAnchor.MiddleRight));
        }
    }
}
