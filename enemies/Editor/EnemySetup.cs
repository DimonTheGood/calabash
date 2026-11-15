using System.Collections.Generic;
using System.IO;
using Calabash.Animation;
using Calabash.Animation.EditorTools;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Calabash.Enemies.EditorTools
{
    /// <summary>
    /// Враги и волны в сцене: материалы (предупреждения, стрелы, вода), выключенные шаблоны врагов
    /// из моделей animation/ (стражник, мужик с вилами, стражник с вёдрами, лучник, палач Корней),
    /// точки появления на улицах, WaveDirector и GameHud. Вызывается сборщиком деревни.
    /// </summary>
    public static class EnemySetup
    {
        public static string RootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("EnemySetup t:Script"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == "EnemySetup")
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets/Calabash/Enemies";
        }

        /// <summary>Вид врага → модель и настройки.</summary>
        static readonly (EnemyKind kind, string cid)[] Roster =
        {
            (EnemyKind.Spearman, "guard"),
            (EnemyKind.Pitchfork, "peasant"),
            (EnemyKind.Bucket, "bucketguard"),
            (EnemyKind.Archer, "archer"),
            (EnemyKind.Executioner, "korney"),
        };

        /// <summary>
        /// Собрать врагов и волны. streetNames — части имён объектов улиц, по которым ищутся точки появления.
        /// Возвращает директор или null, если моделей персонажей нет.
        /// </summary>
        public static WaveDirector Setup(Bounds area, string[] streetNames)
        {
            if (!CharacterAnimSetup.Available)
            {
                Debug.LogWarning("[Враги] Нет моделей animation/ — волн не будет.");
                return null;
            }
            string mats = $"{RootFolder()}/Materials";
            ThirdPersonSetup.EnsureFolder(mats);
            var telegraph = Mat($"{mats}/M_Telegraph.mat", new Color32(0xA3, 0x12, 0x1C, 0xFF));
            var arrow = Mat($"{mats}/M_Arrow.mat", new Color32(0x0B, 0x0B, 0x0B, 0xFF));
            var water = Mat($"{mats}/M_Water.mat", new Color32(0xC9, 0xD8, 0xE4, 0xFF));

            var root = new GameObject("Enemies");
            var templatesRoot = new GameObject("Templates").transform;
            templatesRoot.SetParent(root.transform, false);
            var templates = new List<Enemy>();
            foreach (var (kind, cid) in Roster)
            {
                var go = CharacterAnimSetup.CreateNpc(cid, templatesRoot, new Vector3(0f, -200f, 0f), Vector3.forward,
                                                      VillagerNpc.Role.Stander, null, 100f);
                if (go == null) { Debug.LogWarning($"[Враги] Нет модели {cid}"); continue; }
                Object.DestroyImmediate(go.GetComponent<VillagerNpc>());
                go.name = $"Template_{kind}";
                var e = go.AddComponent<Enemy>();
                e.kind = kind;
                e.animator = go.GetComponentInChildren<Animator>();
                e.telegraphMaterial = telegraph;
                e.arrowMaterial = arrow;
                e.waterMaterial = water;
                Configure(e);
                go.SetActive(false);
                templates.Add(e);
            }

            var dir = root.AddComponent<WaveDirector>();
            dir.templates = templates.ToArray();
            dir.spawnPoints = SpawnPoints(area, streetNames);
            dir.waves = WaveDirector.DefaultWaves();
            root.AddComponent<GameHud>().director = dir;
            Debug.Log($"[Враги] Шаблонов: {templates.Count}, точек появления: {dir.spawnPoints.Length}, волн: {dir.waves.Count}");
            return dir;
        }

        static void Configure(Enemy e)
        {
            switch (e.kind)
            {
                case EnemyKind.Spearman:
                    e.maxHealth = 90f; e.mass = 1.6f; e.attackClip = "Chop"; e.clipStrikeAt = 0.4f;
                    e.attackRange = 2.8f; e.windup = 0.6f; e.recovery = 0.55f; e.cooldown = new Vector2(1.0f, 1.8f);
                    e.damage = 12f; e.knockback = 7f; e.lunge = 5f; e.hitWidth = 1.0f; e.runSpeed = 4.8f;
                    break;
                case EnemyKind.Pitchfork:
                    e.maxHealth = 40f; e.mass = 1f; e.attackClip = "Thrust"; e.clipStrikeAt = 0.4f;
                    e.attackRange = 2.3f; e.windup = 0.42f; e.recovery = 0.5f; e.cooldown = new Vector2(0.8f, 1.6f);
                    e.damage = 7f; e.knockback = 4f; e.lunge = 7f; e.hitWidth = 0.9f; e.runSpeed = 5.4f;
                    break;
                case EnemyKind.Bucket:
                    e.maxHealth = 70f; e.mass = 1.5f; e.attackClip = "Throw"; e.clipStrikeAt = 0.4f;
                    e.keepDistance = new Vector2(6f, 13f); e.windup = 0.65f; e.recovery = 0.5f; e.cooldown = new Vector2(2.2f, 3.4f);
                    e.damage = 7f; e.knockback = 5f; e.lunge = 0f; e.runSpeed = 4.2f; e.splashRadius = 1.8f;
                    break;
                case EnemyKind.Archer:
                    e.maxHealth = 55f; e.mass = 1.2f; e.attackClip = "Shoot"; e.clipStrikeAt = 0.5f;
                    e.keepDistance = new Vector2(10f, 20f); e.windup = 1.0f; e.recovery = 0.5f; e.cooldown = new Vector2(2.0f, 3.2f);
                    e.damage = 10f; e.knockback = 3f; e.lunge = 0f; e.runSpeed = 4.6f; e.projectileSpeed = 38f;
                    break;
                case EnemyKind.Executioner:
                    e.maxHealth = 450f; e.mass = 4f; e.poise = 30f; e.attackClip = "Chop"; e.clipStrikeAt = 0.4f;
                    e.attackRange = 3.4f; e.windup = 1.0f; e.recovery = 0.9f; e.cooldown = new Vector2(1.2f, 2.0f);
                    e.damage = 26f; e.knockback = 12f; e.lunge = 0f; e.runSpeed = 3.4f; e.walkSpeed = 1.6f; e.turnSpeed = 220f;
                    e.hitStun = 0.6f;
                    break;
            }
        }

        /// <summary>Точки на улицах (луч сверху попадает в объект улицы, место под человека свободно), шаг 5 м.</summary>
        static Vector3[] SpawnPoints(Bounds area, string[] streetNames)
        {
            Physics.SyncTransforms();
            var pts = new List<Vector3>();
            for (float x = area.min.x; x <= area.max.x; x += 5f)
                for (float z = area.min.z; z <= area.max.z; z += 5f)
                {
                    var from = new Vector3(x, area.max.y + 5f, z);
                    if (!Physics.Raycast(from, Vector3.down, out var hit, area.size.y + 20f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    bool street = false;
                    foreach (string s in streetNames) if (hit.collider.name.Contains(s)) { street = true; break; }
                    if (!street) continue;
                    Vector3 p = hit.point + Vector3.up * 0.05f;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.7f, p + Vector3.up * 1.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    pts.Add(p);
                }
            return pts.ToArray();
        }

        static Material Mat(string path, Color color)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.color = color;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
