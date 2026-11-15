using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Calabash.Audio.EditorTools
{
    /// <summary>
    /// Подключение звука к сцене: настраивает импорт audio/Sounds, создаёт объект «Audio» с банками звуков
    /// (CalabashSfx) и музыкой (CalabashMusic), вешает ZhdanSfx на героя.
    /// Банк — это все файлы &lt;банк&gt;_NN.ogg; громкость и разброс высоты банков — в таблице Defaults.
    /// Вызывается сборщиками сцен (арена, деревня).
    /// </summary>
    public static class CalabashAudioSetup
    {
        /// <summary>id → громкость, разброс высоты, доля 3D, близкая дистанция.</summary>
        static readonly Dictionary<string, (float vol, float pmin, float pmax, float spatial, float minDist)> Defaults =
            new Dictionary<string, (float, float, float, float, float)>
            {
                { "punch_light", (0.9f, 0.92f, 1.08f, 0.6f, 6f) },
                { "punch_heavy", (1f, 0.9f, 1.05f, 0.6f, 8f) },
                { "hurt_hit", (0.9f, 0.9f, 1.05f, 0.5f, 5f) },
                { "swing", (0.55f, 0.92f, 1.1f, 0.5f, 4f) },
                { "footstep_snow", (0.6f, 0.9f, 1.1f, 0.8f, 3f) },
                { "land", (0.8f, 0.9f, 1.05f, 0.6f, 4f) },
                { "splash", (0.9f, 0.9f, 1.1f, 0.8f, 5f) },
                { "explosion", (0.9f, 0.85f, 1.05f, 0.5f, 10f) },
                { "fire_burst", (0.7f, 0.9f, 1.15f, 0.6f, 6f) },
                { "metal_clank", (0.7f, 0.92f, 1.08f, 0.9f, 4f) },
                { "blade", (0.7f, 0.95f, 1.1f, 0.9f, 4f) },
                { "wood_hit", (0.8f, 0.9f, 1.1f, 0.9f, 4f) },
                { "bow_release", (0.7f, 0.95f, 1.1f, 0.9f, 4f) },
                { "cloth", (0.5f, 0.9f, 1.1f, 0.8f, 3f) },
                { "grunt", (0.75f, 0.88f, 1.12f, 0.9f, 5f) },
                { "death", (0.85f, 0.9f, 1.05f, 0.9f, 6f) },
            };

        /// <summary>Папка модуля (Assets/Calabash/Audio).</summary>
        public static string RootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("CalabashAudioSetup t:Script"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == "CalabashAudioSetup")
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets/Calabash/Audio";
        }

        static string SoundsFolder => $"{RootFolder()}/Sounds";

        public static bool Available => AssetDatabase.IsValidFolder(SoundsFolder);

        /// <summary>Звук в сцене и на герое. Возвращает CalabashSfx или null, если звуков нет.</summary>
        public static CalabashSfx Setup(GameObject player)
        {
            if (!Available)
            {
                Debug.LogWarning("[Звук] audio/Sounds не найдены — сцена без звука. Скопируйте audio/ в Assets/Calabash/Audio.");
                return null;
            }
            var clips = LoadClips();

            var root = new GameObject("Audio");
            var sfx = root.AddComponent<CalabashSfx>();
            sfx.banks = new List<CalabashSfx.Bank>();
            foreach (var g in clips.Where(c => !c.Key.StartsWith("music") && c.Key != "fire_loop").OrderBy(c => c.Key))
            {
                var b = new CalabashSfx.Bank { id = g.Key, clips = g.Value.ToArray() };
                if (Defaults.TryGetValue(g.Key, out var d))
                {
                    b.volume = d.vol; b.pitchMin = d.pmin; b.pitchMax = d.pmax; b.spatial = d.spatial; b.minDistance = d.minDist;
                }
                sfx.banks.Add(b);
            }

            var music = new GameObject("Music").AddComponent<CalabashMusic>();
            music.transform.SetParent(root.transform, false);
            if (clips.TryGetValue("music_battle", out var m)) music.music = m[0];

            if (player != null)
            {
                var z = player.GetComponent<ZhdanSfx>();
                if (z == null) z = player.AddComponent<ZhdanSfx>();
                if (clips.TryGetValue("fire_loop", out var f)) z.fireLoop = f[0];
            }
            Debug.Log($"[Звук] Банков: {sfx.banks.Count}, клипов: {clips.Sum(c => c.Value.Count)}");
            return sfx;
        }

        /// <summary>Клипы по банкам; заодно настраивает импорт (короткие — в памяти, длинные — потоком).</summary>
        static Dictionary<string, List<AudioClip>> LoadClips()
        {
            var result = new Dictionary<string, List<AudioClip>>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SoundsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                int us = name.LastIndexOf('_');
                string id = us > 0 ? name.Substring(0, us) : name;
                bool longClip = id.StartsWith("music") || id == "fire_loop";

                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp != null)
                {
                    var s = imp.defaultSampleSettings;
                    var want = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                    bool mono = !id.StartsWith("music");
                    if (s.loadType != want || imp.forceToMono != mono)
                    {
                        s.loadType = want;
                        s.compressionFormat = AudioCompressionFormat.Vorbis;
                        s.quality = longClip ? 0.5f : 0.7f;
                        imp.defaultSampleSettings = s;
                        imp.forceToMono = mono;
                        imp.loadInBackground = longClip;
                        imp.SaveAndReimport();
                    }
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                if (!result.TryGetValue(id, out var list)) result[id] = list = new List<AudioClip>();
                list.Add(clip);
            }
            foreach (var l in result.Values) l.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }
    }
}
