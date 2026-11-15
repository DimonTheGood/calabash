using System.Collections.Generic;
using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Прогресс прохождения: до какой волны дошёл Ждан, сколько раз умирал, какие улучшения взял.
    /// Хранится в PlayerPrefs, поэтому «Продолжить» в меню работает и после перезапуска игры.
    /// Смерть не сбрасывает прогресс (лимб возвращает в бой с тем же), сбрасывают его концовки и «Новая игра».
    /// </summary>
    [System.Serializable]
    public class GameProgress
    {
        const string Key = "calabash.progress";

        /// <summary>С какой волны начинать (с нуля).</summary>
        public int wave;
        public int deaths;
        public int kills;
        public float time;
        public List<string> upgrades = new List<string>();
        public bool tutorialDone;
        public bool introSeen;
        /// <summary>Лучший результат за все игры: дальше всего дошёл.</summary>
        public int bestWave;

        public enum Afterlife { Limbo, Heaven, Hell }

        static GameProgress current;

        /// <summary>Куда ведёт сцена Afterlife при загрузке.</summary>
        public static Afterlife NextAfterlife = Afterlife.Limbo;
        /// <summary>Загрузить деревню сразу в бой, без меню (после лимба).</summary>
        public static bool SkipMenuOnce;

        public static GameProgress Current
        {
            get
            {
                if (current == null) current = Load();
                return current;
            }
        }

        /// <summary>Есть сохранённая игра, которую можно продолжить.</summary>
        public static bool CanContinue => Current.introSeen && (Current.wave > 0 || Current.deaths > 0 || Current.upgrades.Count > 0);

        static GameProgress Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { return JsonUtility.FromJson<GameProgress>(json) ?? new GameProgress(); }
                catch { /* испорченное сохранение — начать заново */ }
            }
            return new GameProgress();
        }

        public static void Save()
        {
            var p = Current;
            p.bestWave = Mathf.Max(p.bestWave, p.wave);
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(p));
            PlayerPrefs.Save();
        }

        /// <summary>Новая игра: всё с нуля, кроме рекорда и пройденного обучения.</summary>
        public static void Reset()
        {
            var old = Current;
            current = new GameProgress { bestWave = old.bestWave, tutorialDone = old.tutorialDone };
            Save();
        }
    }

    /// <summary>Настройки: громкость, музыка, чувствительность мыши, манга-фильтр. PlayerPrefs.</summary>
    public static class GameSettings
    {
        public static float Volume = 1f;
        public static float Music = 1f;
        public static float Sensitivity = 1f;
        public static bool Gloom = true;
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Volume = PlayerPrefs.GetFloat("calabash.volume", 1f);
            Music = PlayerPrefs.GetFloat("calabash.music", 1f);
            Sensitivity = PlayerPrefs.GetFloat("calabash.sens", 1f);
            Gloom = PlayerPrefs.GetInt("calabash.gloom", 1) == 1;
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("calabash.volume", Volume);
            PlayerPrefs.SetFloat("calabash.music", Music);
            PlayerPrefs.SetFloat("calabash.sens", Sensitivity);
            PlayerPrefs.SetInt("calabash.gloom", Gloom ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Применить к сцене: громкость, музыка, мышь героя, фильтр на камере.</summary>
        public static void Apply(float baseSensitivity = 0.1f)
        {
            Load();
            AudioListener.volume = Volume;
            Calabash.Audio.CalabashMusic.VolumeScale = Music;
            foreach (var inp in Object.FindObjectsByType<Calabash.Movement.ZhdanInput>(FindObjectsInactive.Exclude))
                inp.mouseSensitivity = baseSensitivity * Sensitivity;
            foreach (var g in Object.FindObjectsByType<Calabash.Style.MangaGloom>(FindObjectsInactive.Exclude))
                g.intensity = Gloom ? 1f : 0f;
        }
    }
}
