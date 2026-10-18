using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Технологическое дерево от каменных орудий до примитивного земледелия.
    /// Бонусы закэшированы в статическом словаре (0 аллокаций при запросе).
    /// </summary>
    public static class TechnologyTree
    {
        private static readonly Dictionary<string, TechInfo> Techs = new();
        private static readonly Dictionary<string, TechBonus> CachedBonuses = new();
        private static readonly TechBonus DefaultBonus = new();
        private static readonly List<TechInfo> AllTechList = new();

        static TechnologyTree()
        {
            // === КАМЕННЫЙ ВЕК ===
            Add("fire_control", "Контроль огня", TechEra.EarlyStone, Array.Empty<string>(),
                "Защита от хищников, тепло, приготовление пищи",
                new TechBonus { WarmthBonus = 0.35, PredatorDefense = 0.3 });

            Add("stone_tools", "Каменные орудия", TechEra.EarlyStone, Array.Empty<string>(),
                "Рубила, скребки — улучшение обработки пищи",
                new TechBonus { GatheringBonus = 0.3, CraftingBonus = 0.25 });

            Add("shelter_basic", "Простое укрытие", TechEra.EarlyStone, Array.Empty<string>(),
                "Ветровой заслон, примитивный шалаш",
                new TechBonus { ShelterQuality = 0.25, WarmthBonus = 0.15 });

            Add("language_proto", "Протоязык", TechEra.EarlyStone, Array.Empty<string>(),
                "Базовые жесты и звуки для общения",
                new TechBonus { TradeBonus = 0.15 });

            // === СРЕДНИЙ КАМЕННЫЙ ВЕК ===
            Add("spear", "Копьё", TechEra.MiddleStone, new[] { "stone_tools" },
                "Охотничье оружие дальнего боя",
                new TechBonus { HuntingBonus = 0.5, AttackBonus = 0.35 });

            Add("cooking", "Приготовление пищи", TechEra.MiddleStone, new[] { "fire_control" },
                "Жарка мяса — лучше усвоение, меньше болезней",
                new TechBonus { FoodEfficiency = 0.45, DiseaseResistance = 0.2 });

            Add("clothing", "Одежда из шкур", TechEra.MiddleStone, new[] { "stone_tools" },
                "Защита от холода",
                new TechBonus { WarmthBonus = 0.45 });

            Add("building", "Строительство", TechEra.MiddleStone, new[] { "shelter_basic", "stone_tools" },
                "Хижины из веток и камней",
                new TechBonus { ShelterQuality = 0.5 });

            Add("tools", "Улучшенные инструменты", TechEra.MiddleStone, new[] { "stone_tools" },
                "Ручные топоры, ножи",
                new TechBonus { CraftingBonus = 0.4, GatheringBonus = 0.25 });

            Add("gathering_advanced", "Сбор с инструментами", TechEra.MiddleStone, new[] { "tools" },
                "Палка-копалка, корзины",
                new TechBonus { GatheringBonus = 0.5 });

            Add("burial", "Погребение мёртвых", TechEra.MiddleStone, new[] { "language_proto" },
                "Начало ритуалов, культуры смерти",
                new TechBonus { DiseaseResistance = 0.15 });

            // === ПОЗДНИЙ КАМЕННЫЙ ВЕК ===
            Add("bow", "Лук и стрелы", TechEra.LateStone, new[] { "spear", "tools" },
                "Охота на расстоянии",
                new TechBonus { HuntingBonus = 0.75, AttackBonus = 0.45 });

            Add("fishing", "Рыбалка", TechEra.LateStone, new[] { "tools" },
                "Удочка, сети — новый источник пищи",
                new TechBonus { FoodProduction = 0.35 });

            Add("tanning", "Выделка шкур", TechEra.LateStone, new[] { "clothing", "tools" },
                "Более долговечная одежда и материалы",
                new TechBonus { WarmthBonus = 0.3, MaterialBonus = 0.3 });

            Add("medicine_herbal", "Травяная медицина", TechEra.LateStone, new[] { "gathering_advanced", "cooking" },
                "Лечение травами, снижение смертности",
                new TechBonus { HealingBonus = 0.5, DiseaseResistance = 0.35 });

            Add("animal_taming", "Приручение животных", TechEra.LateStone, new[] { "language_proto", "cooking" },
                "Одомашнивание волков, коз",
                new TechBonus { HuntingBonus = 0.25, PredatorDefense = 0.25 });

            Add("pottery", "Гончарство", TechEra.LateStone, new[] { "fire_control", "building" },
                "Хранение пищи и воды",
                new TechBonus { FoodStorage = 0.55 });

            Add("language_complex", "Развитой язык", TechEra.LateStone, new[] { "language_proto", "burial" },
                "Сложные предложения, истории, передача знаний",
                new TechBonus { TradeBonus = 0.35 });

            // === НЕОЛИТ ===
            Add("agriculture", "Земледелие", TechEra.Neolithic, new[] { "gathering_advanced", "tools", "pottery" },
                "Выращивание злаков и овощей",
                new TechBonus { FoodProduction = 0.85, Sedentism = 0.6 });

            Add("animal_husbandry", "Скотоводство", TechEra.Neolithic, new[] { "animal_taming", "building" },
                "Разведение животных для еды и материалов",
                new TechBonus { FoodProduction = 0.55, MaterialBonus = 0.35 });

            Add("weaving", "Ткачество", TechEra.Neolithic, new[] { "tanning", "tools" },
                "Ткани из растительных волокон",
                new TechBonus { WarmthBonus = 0.35, TradeBonus = 0.2 });

            Add("stone_wall", "Каменные стены", TechEra.Neolithic, new[] { "building", "tools" },
                "Укрепления, защита от хищников и врагов",
                new TechBonus { PredatorDefense = 0.6, ShelterQuality = 0.4 });

            Add("trade_system", "Торговля", TechEra.Neolithic, new[] { "language_complex", "pottery" },
                "Обмен ресурсами между группами",
                new TechBonus { TradeBonus = 0.6 });

            Add("fermentation", "Ферментация", TechEra.Neolithic, new[] { "pottery", "agriculture" },
                "Хранение пищи, напитки",
                new TechBonus { FoodStorage = 0.4, DiseaseResistance = 0.2 });

            Add("astronomy_basic", "Наблюдение за звёздами", TechEra.Neolithic, new[] { "language_complex" },
                "Отслеживание сезонов, навигация",
                new TechBonus { FoodProduction = 0.25 });

            Add("art", "Искусство", TechEra.Neolithic, new[] { "language_complex" },
                "Наскальная живопись, скульптура, музыка",
                new TechBonus { TradeBonus = 0.2 });
        }

        private static void Add(string id, string name, TechEra era, string[] prereqs, string desc, TechBonus bonus)
        {
            var info = new TechInfo
            {
                Id = id,
                Name = name,
                Era = era,
                Prerequisites = prereqs,
                Description = desc,
                DiscoveryDifficulty = (int)era * 0.28 + prereqs.Length * 0.18,
                Bonus = bonus
            };
            Techs[id] = info;
            CachedBonuses[id] = bonus;
            AllTechList.Add(info);
        }

        public static string[] GetPrerequisites(string techId) =>
            Techs.TryGetValue(techId, out var t) ? t.Prerequisites : Array.Empty<string>();

        public static TechInfo? GetInfo(string techId) =>
            Techs.TryGetValue(techId, out var t) ? t : null;

        public static List<TechInfo> GetAll() => AllTechList;

        public static List<string> GetAvailable(HashSet<string> known)
        {
            var available = new List<string>(8);
            for (int i = 0; i < AllTechList.Count; i++)
            {
                var t = AllTechList[i];
                if (known.Contains(t.Id)) continue;
                bool ok = true;
                for (int p = 0; p < t.Prerequisites.Length; p++)
                {
                    if (!known.Contains(t.Prerequisites[p]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) available.Add(t.Id);
            }
            return available;
        }

        public static string? TryDiscover(Human human, Random? rng, int nearbyPeers = 0)
        {
            rng ??= Random.Shared;
            for (int i = 0; i < AllTechList.Count; i++)
            {
                var tech = AllTechList[i];
                if (human.KnownTechnologies.Contains(tech.Id)) continue;

                bool prereqsMet = true;
                for (int p = 0; p < tech.Prerequisites.Length; p++)
                {
                    if (!human.KnownTechnologies.Contains(tech.Prerequisites[p]))
                    {
                        prereqsMet = false;
                        break;
                    }
                }
                if (!prereqsMet) continue;

                double collectiveBoost = 1.0 + Math.Min(nearbyPeers, 8) * 0.18;
                double chance = 0.00035 *
                    human.Genes.EffCreativity *
                    human.Genes.EffIntelligence *
                    (1.0 + human.SkillCrafting * 0.6 + human.Brain.Dopamine * 0.3) *
                    collectiveBoost /
                    (1.0 + tech.DiscoveryDifficulty);

                if (rng.NextDouble() < chance)
                {
                    human.LearnTechnology(tech.Id);
                    return tech.Id;
                }
            }
            return null;
        }

        public static TechBonus GetBonus(string techId) =>
            CachedBonuses.TryGetValue(techId, out var b) ? b : DefaultBonus;
    }

    public class TechInfo
    {
        public string Id = "";
        public string Name = "";
        public TechEra Era;
        public string[] Prerequisites = Array.Empty<string>();
        public string Description = "";
        public double DiscoveryDifficulty;
        public TechBonus Bonus = new();
    }

    public enum TechEra
    {
        EarlyStone = 1,
        MiddleStone = 2,
        LateStone = 3,
        Neolithic = 4
    }

    public class TechBonus
    {
        public double WarmthBonus;
        public double PredatorDefense;
        public double GatheringBonus;
        public double CraftingBonus;
        public double FoodEfficiency;
        public double HuntingBonus;
        public double AttackBonus;
        public double ShelterQuality;
        public double HealingBonus;
        public double DiseaseResistance;
        public double FoodProduction;
        public double FoodStorage;
        public double MaterialBonus;
        public double TradeBonus;
        public double Sedentism;
    }
}
