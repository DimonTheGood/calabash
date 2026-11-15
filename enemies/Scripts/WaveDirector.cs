using System.Collections.Generic;
using Calabash.Audio;
using Calabash.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Calabash.Enemies
{
    /// <summary>
    /// Волны врагов в деревне. Враги появляются на улицах в 18–45 м от Ждана и бегут к нему.
    /// Между волнами — передышка и немного топлива. Во время волны огонь сам прогорает:
    /// стоять нельзя, топливо дают только удары и убийства. Последняя волна — с палачом Корнеем.
    /// Топливо кончилось — «Огонь погас», R — заново. Все волны пройдены — победа.
    /// Ближний бой по жетонам: одновременно бьют не больше meleeSlots врагов, остальные кружат рядом.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WaveDirector : MonoBehaviour
    {
        [System.Serializable]
        public class Wave
        {
            public string title = "";
            public int spearmen, pitchforks, buckets, archers, executioners;
            [Tooltip("Сколько врагов могут бить одновременно")] public int meleeSlots = 2;
            public int Total => spearmen + pitchforks + buckets + archers + executioners;
        }

        public static WaveDirector Instance { get; private set; }

        [Tooltip("Шаблоны врагов (выключенные объекты в сцене), по видам EnemyKind")]
        public Enemy[] templates = new Enemy[0];
        [Tooltip("Точки появления на улицах (ставит EnemySetup)")]
        public Vector3[] spawnPoints = new Vector3[0];
        public Vector2 spawnDistance = new Vector2(18f, 45f);
        [Tooltip("Враги выходят не все сразу, а по одному с этим интервалом, с")]
        public float spawnInterval = 0.45f;
        [Tooltip("Не больше врагов одновременно на улице")]
        public int maxAlive = 12;

        public List<Wave> waves = new List<Wave>();
        public float firstWaveDelay = 4f;
        [Tooltip("Начать волны сразу. Выключено — ждать Begin() (игру ведёт GameFlow из game/)")]
        public bool autoStart = true;
        [Tooltip("Смерть и победу показывает и обрабатывает кто-то другой (GameFlow): без «R — заново»")]
        public bool externalFlow;
        [Tooltip("Передышка не идёт, пока true (выбор улучшения)")]
        public bool holdBreak;
        public float breakTime = 5f;
        [Tooltip("Топливо за пройденную волну")] public float waveReward = 15f;
        [Tooltip("Огонь прогорает во время волны, ед./с")] public float fuelDecayInWave = 1.8f;
        [Tooltip("Множитель прогорания (улучшение «Медленное тление»)")] public float fuelDecayMultiplier = 1f;

        public enum Phase { Waiting, Intro, Fighting, Break, Victory, GameOver }

        /// <summary>Волна отбита (номер с нуля).</summary>
        public static event System.Action<int> WaveCleared;
        /// <summary>Топливо кончилось.</summary>
        public static event System.Action PlayerDied;
        /// <summary>Убит финальный босс (или пройдены все волны).</summary>
        public static event System.Action Victory;
        public Phase CurrentPhase { get; private set; }
        public int WaveIndex { get; private set; } = -1;
        public int WaveCount => waves.Count;
        public int Kills { get; private set; }
        public int Alive => Enemy.All.Count;
        public int Remaining => Alive + queue.Count;
        public float PhaseTimer => timer;
        public float SurvivedTime { get; private set; }
        public string Banner { get; private set; } = "";
        public float BannerTimer { get; private set; }
        /// <summary>Сколько секунд надпись уже на экране (для въезда).</summary>
        public float BannerAge => bannerShown - BannerTimer;
        float bannerShown;

        static int slots, slotsTaken;
        public static bool MeleeSlotsFull => slotsTaken >= slots;
        public static bool TakeMeleeSlot() { if (slotsTaken >= slots) return false; slotsTaken++; return true; }
        public static void ReturnMeleeSlot() => slotsTaken = Mathf.Max(0, slotsTaken - 1);

        readonly Queue<EnemyKind> queue = new Queue<EnemyKind>();
        float timer, spawnTimer;
        ZhdanFuel fuel;

        void Awake()
        {
            Instance = this;
            Enemy.All.Clear();
            PlayerTarget.Reset();
            slots = 2; slotsTaken = 0;
            Enemy.Died += OnDied;
            foreach (var t in templates) if (t != null) t.gameObject.SetActive(false);
            if (waves.Count == 0) waves = DefaultWaves();
        }

        void OnDestroy()
        {
            Enemy.Died -= OnDied;
            if (Instance == this) Instance = null;
            CalabashMusic.Combat = false;
        }

        void Start()
        {
            PlayerTarget.Find();
            fuel = PlayerTarget.Fuel;
            if (fuel != null) fuel.onEmpty.AddListener(OnFuelEmpty);
            CurrentPhase = Phase.Waiting;
            if (autoStart) Begin(0);
        }

        /// <summary>Начать волны с номера startWave (с нуля).</summary>
        public void Begin(int startWave, string banner = "Защищайся, Ждан")
        {
            WaveIndex = Mathf.Clamp(startWave, 0, Mathf.Max(0, waves.Count - 1)) - 1;
            CurrentPhase = Phase.Intro;
            timer = firstWaveDelay;
            if (!string.IsNullOrEmpty(banner)) Show(banner, 3f);
        }

        public static List<Wave> DefaultWaves() => new List<Wave>
        {
            new Wave { title = "Мужики с вилами", pitchforks = 5, meleeSlots = 2 },
            new Wave { title = "Стража", spearmen = 3, pitchforks = 4, meleeSlots = 3 },
            new Wave { title = "Вода!", spearmen = 3, pitchforks = 3, buckets = 3, meleeSlots = 3 },
            new Wave { title = "Лучники", spearmen = 4, pitchforks = 2, archers = 3, buckets = 2, meleeSlots = 3 },
            new Wave { title = "Облава", spearmen = 5, pitchforks = 5, archers = 3, buckets = 3, meleeSlots = 4 },
            new Wave { title = "Палач Корней", executioners = 1, spearmen = 4, pitchforks = 3, buckets = 2, archers = 2, meleeSlots = 4 },
        };

        void Update()
        {
            float dt = Time.deltaTime;
            timer -= dt;
            BannerTimer -= Time.unscaledDeltaTime;
            if (CurrentPhase == Phase.Fighting || CurrentPhase == Phase.Break) SurvivedTime += dt;

            switch (CurrentPhase)
            {
                case Phase.Intro:
                case Phase.Break:
                    if (holdBreak && CurrentPhase == Phase.Break) timer = Mathf.Max(timer, 2f);
                    if (timer <= 0f) StartWave(WaveIndex + 1);
                    break;
                case Phase.Fighting:
                    spawnTimer -= dt;
                    if (queue.Count > 0 && spawnTimer <= 0f && Alive < maxAlive)
                    {
                        if (Spawn(queue.Peek())) queue.Dequeue();
                        spawnTimer = spawnInterval;
                    }
                    if (queue.Count == 0 && Alive == 0) EndWave();
                    break;
                case Phase.Victory:
                case Phase.GameOver:
                    if (!externalFlow && RestartPressed()) Restart();
                    break;
            }
        }

        void LateUpdate()
        {
            // на экране конца игры мир стоит (стоп-кадр боя сам возвращает timeScale, поэтому держим каждый кадр)
            if (CurrentPhase == Phase.GameOver) Time.timeScale = 0f;
        }

        void StartWave(int i)
        {
            if (i >= waves.Count) { Win(); return; }
            WaveIndex = i;
            var w = waves[i];
            slots = Mathf.Max(1, w.meleeSlots);
            queue.Clear();
            var list = new List<EnemyKind>();
            for (int k = 0; k < w.executioners; k++) list.Add(EnemyKind.Executioner);
            for (int k = 0; k < w.spearmen; k++) list.Add(EnemyKind.Spearman);
            for (int k = 0; k < w.pitchforks; k++) list.Add(EnemyKind.Pitchfork);
            for (int k = 0; k < w.buckets; k++) list.Add(EnemyKind.Bucket);
            for (int k = 0; k < w.archers; k++) list.Add(EnemyKind.Archer);
            // палач выходит первым, остальные вперемешку
            for (int k = list.Count - 1; k > w.executioners; k--)
            {
                int j = Random.Range(w.executioners, k + 1);
                (list[k], list[j]) = (list[j], list[k]);
            }
            foreach (var e in list) queue.Enqueue(e);
            CurrentPhase = Phase.Fighting;
            spawnTimer = 0f;
            if (fuel != null) fuel.decayPerSecond = fuelDecayInWave * fuelDecayMultiplier;
            CalabashMusic.Combat = true;
            CalabashMusic.Play(w.executioners > 0 ? "boss" : "battle");
            Show($"Волна {i + 1}/{waves.Count}\n{w.title}", 2.5f);
            CalabashSfx.Play2D("explosion", 0.5f, 0.6f);
        }

        void EndWave()
        {
            if (fuel != null) { fuel.decayPerSecond = 0f; fuel.Add(waveReward); }
            CalabashMusic.Combat = false;
            if (WaveIndex + 1 >= waves.Count) { Win(); return; }
            CurrentPhase = Phase.Break;
            timer = breakTime;
            Show($"Волна {WaveIndex + 1} отбита\n+{waveReward:0} топлива", 2.5f);
            WaveCleared?.Invoke(WaveIndex);
        }

        void Win()
        {
            if (CurrentPhase == Phase.Victory) return;
            CurrentPhase = Phase.Victory;
            queue.Clear();
            if (fuel != null) fuel.decayPerSecond = 0f;
            CalabashMusic.Combat = false;
            Show("", 0f);
            Victory?.Invoke();
        }

        void OnFuelEmpty()
        {
            if (CurrentPhase == Phase.Victory || CurrentPhase == Phase.GameOver) return;
            CurrentPhase = Phase.GameOver;
            CalabashMusic.Combat = false;
            CalabashSfx.Play2D("fire_burst", 1f, 0.5f);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            PlayerDied?.Invoke();
        }

        void OnDied(Enemy e)
        {
            Kills++;
            // палач Корней — финальный босс: его смерть заканчивает игру
            if (e.kind == EnemyKind.Executioner && WaveIndex == waves.Count - 1 && CurrentPhase == Phase.Fighting) Win();
        }

        void Show(string text, float seconds) { Banner = text; BannerTimer = bannerShown = seconds; }

        bool Spawn(EnemyKind kind)
        {
            Enemy template = null;
            foreach (var t in templates) if (t != null && t.kind == kind) { template = t; break; }
            if (template == null) return true;                         // нет шаблона — пропустить
            if (!PickSpawn(out Vector3 p)) return false;
            Vector3 look = PlayerTarget.Position - p; look.y = 0f;
            var go = Instantiate(template.gameObject, p, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            go.name = template.name.Replace("Template_", "") + "_" + Kills;
            go.SetActive(true);
            return true;
        }

        /// <summary>Переставить застрявшего врага на улицу поближе к Ждану (за его спиной).</summary>
        public bool Relocate(Enemy e)
        {
            var keep = spawnDistance;
            spawnDistance = new Vector2(10f, 25f);
            bool ok = PickSpawn(out Vector3 p) && spawnPoints.Length > 0;
            spawnDistance = keep;
            if (!ok) return false;
            e.transform.position = p;
            return true;
        }

        bool PickSpawn(out Vector3 point)
        {
            Vector3 hero = PlayerTarget.Position;
            Vector3 look = PlayerTarget.Find() ? PlayerTarget.Transform.forward : Vector3.forward;
            point = Vector3.zero;
            float best = float.MinValue;
            for (int tries = 0; tries < 24 && spawnPoints.Length > 0; tries++)
            {
                Vector3 p = spawnPoints[Random.Range(0, spawnPoints.Length)];
                float d = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(hero.x, 0f, hero.z));
                if (d < spawnDistance.x || d > spawnDistance.y) continue;
                bool near = false;
                foreach (var e in Enemy.All) if ((e.transform.position - p).sqrMagnitude < 2f) near = true;
                if (near) continue;
                // лучше сбоку или со спины, чем прямо перед глазами
                float score = -Vector3.Dot((p - hero).normalized, look) + Random.value * 0.5f;
                if (score > best) { best = score; point = p; }
            }
            if (best > float.MinValue) return true;
            // точек нет — по кругу вокруг героя
            Vector2 r = Random.insideUnitCircle.normalized * spawnDistance.x;
            point = hero + new Vector3(r.x, 0f, r.y);
            return spawnPoints.Length == 0;
        }

        static bool RestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && (kb.rKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return);
#endif
        }

        public static void Restart()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.buildIndex);
#endif
        }
    }
}
