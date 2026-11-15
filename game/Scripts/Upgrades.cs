using System.Collections.Generic;
using System.Linq;
using Calabash.Combat;
using Calabash.Enemies;
using Calabash.Movement;
using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Улучшения огня Ждана: после каждой отбитой волны — выбор одного из трёх.
    /// Взятые хранятся в GameProgress и применяются заново при каждой загрузке деревни (смерть их не отнимает).
    /// </summary>
    public static class Upgrades
    {
        public class Def
        {
            public string id, title, text;
            public int max = 1;
            public System.Action<GameObject> apply;
        }

        public static readonly List<Def> All = new List<Def>
        {
            new Def { id = "heavy_hand", title = "Тяжёлая рука", text = "Все удары бьют на 20% сильнее", max = 3,
                apply = p => Combat(p, c => ForStrikes(c, s => s.damage *= 1.2f, () => c.heavyMaxDamage *= 1.2f)) },
            new Def { id = "greedy_fire", title = "Жадный огонь", text = "Убийства дают на 60% больше топлива", max = 2,
                apply = p => Combat(p, c => c.fuelPerKill *= 1.6f) },
            new Def { id = "arsonist", title = "Поджигатель", text = "Любой удар поджигает врага",
                apply = p => PlayerTarget.igniteAll = true },
            new Def { id = "second_wind", title = "Второе дыхание", text = "Ещё один заряд дэша", max = 2,
                apply = p => { var m = p.GetComponent<ZhdanMovement>(); if (m != null) { m.staminaCharges += 1; m.RefundStamina(1f); } } },
            new Def { id = "wide_heat", title = "Широкий жар", text = "Удар оземь накрывает на 40% шире", max = 2,
                apply = p => Combat(p, c => { c.slamWaveRadius *= 1.4f; c.slamWaveMaxRadius *= 1.4f; }) },
            new Def { id = "thick_ash", title = "Толстая зола", text = "Топлива больше на 30", max = 3,
                apply = p => { var f = p.GetComponent<ZhdanFuel>(); if (f != null) { f.maxFuel += 30f; f.Add(30f); } } },
            new Def { id = "slow_burn", title = "Медленное тление", text = "В бою огонь прогорает на 40% медленнее",
                apply = p => { var d = WaveDirector.Instance; if (d != null) d.fuelDecayMultiplier *= 0.6f; } },
            new Def { id = "wrath", title = "Вспышка гнева", text = "Убитый враг взрывается огнём и бьёт соседей",
                apply = p => PlayerTarget.killBlastDamage = 30f },
            new Def { id = "tempered", title = "Закалка", text = "Удары врагов гасят огонь на 20% слабее", max = 2,
                apply = p => PlayerTarget.damageMultiplier *= 0.8f },
        };

        public static Def Get(string id) => All.FirstOrDefault(d => d.id == id);

        public static int Count(string id) => GameProgress.Current.upgrades.Count(u => u == id);

        /// <summary>До трёх случайных улучшений, которые ещё можно взять.</summary>
        public static List<Def> Offer(int n = 3)
        {
            var pool = All.Where(d => Count(d.id) < d.max).ToList();
            var res = new List<Def>();
            while (res.Count < n && pool.Count > 0)
            {
                int i = Random.Range(0, pool.Count);
                res.Add(pool[i]);
                pool.RemoveAt(i);
            }
            return res;
        }

        /// <summary>Взять улучшение: применить к герою и запомнить в прогрессе.</summary>
        public static void Take(Def d, GameObject player)
        {
            GameProgress.Current.upgrades.Add(d.id);
            GameProgress.Save();
            d.apply?.Invoke(player);
        }

        /// <summary>Применить все взятые улучшения (при загрузке деревни).</summary>
        public static void ApplyAll(GameObject player)
        {
            foreach (string id in GameProgress.Current.upgrades)
                Get(id)?.apply?.Invoke(player);
        }

        static void Combat(GameObject p, System.Action<ZhdanCombat> a)
        {
            var c = p.GetComponent<ZhdanCombat>();
            if (c != null) a(c);
        }

        static void ForStrikes(ZhdanCombat c, System.Action<StrikeSettings> a, System.Action extra)
        {
            foreach (var s in new[] { c.light1, c.light2, c.light3, c.heavy, c.dashStrike, c.uppercut }) if (s != null) a(s);
            c.slamWaveDamage *= 1.2f;
            c.slamWaveMaxDamage *= 1.2f;
            extra();
        }
    }
}
