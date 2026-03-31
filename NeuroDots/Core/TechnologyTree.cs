using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Технологическое дерево от каменных орудий до примитивного земледелия
    /// </summary>
    public static class TechnologyTree
    {
        static Dictionary<string, TechInfo> Techs = new();

        static TechnologyTree()
        {
            // === КАМЕННЫЙ ВЕК ===
            Add("fire_control", "Контроль огня", TechEra.EarlyStone,
                new string[0],
                "Защита от хищников, тепло, приготовление пищи");

            Add("stone_tools", "Каменные орудия", TechEra.EarlyStone,
                new string[0],
                "Рубила, скребки — улучшение обработки пищи");

            Add("shelter_basic", "Простое укрытие", TechEra.EarlyStone,
                new string[0],
                "Ветровой заслон, примитивный шалаш");

            Add("language_proto", "Протоязык", TechEra.EarlyStone,
                new string[0],
                "Базовые жесты и звуки для общения");

            // === СРЕДНИЙ КАМЕННЫЙ ВЕК ===
            Add("spear", "Копьё", TechEra.MiddleStone,
                new[] { "stone_tools" },
                "Охотничье оружие дальнего боя");

            Add("cooking", "Приготовление пищи", TechEra.MiddleStone,
                new[] { "fire_control" },
                "Жарка мяса — лучше усвоение, меньше болезней");

            Add("clothing", "Одежда из шкур", TechEra.MiddleStone,
                new[] { "stone_tools" },
                "Защита от холода");

            Add("building", "Строительство", TechEra.MiddleStone,
                new[] { "shelter_basic", "stone_tools" },
                "Хижины из веток и камней");

            Add("tools", "Улучшенные инструменты", TechEra.MiddleStone,
                new[] { "stone_tools" },
                "Ручные топоры, ножи");

            Add("gathering_advanced", "Сбор с инструментами", TechEra.MiddleStone,
                new[] { "tools" },
                "Палка-копалка, корзины");

            Add("burial", "Погребение мёртвых", TechEra.MiddleStone,
                new[] { "language_proto" },
                "Начало ритуалов, культуры смерти");

            // === ПОЗДНИЙ КАМЕННЫЙ ВЕК ===
            Add("bow", "Лук и стрелы", TechEra.LateStone,
                new[] { "spear", "tools" },
                "Охота на расстоянии");

            Add("fishing", "Рыбалка", TechEra.LateStone,
                new[] { "tools" },
                "Удочка, сети — новый источник пищи");

            Add("tanning", "Выделка шкур", TechEra.LateStone,
                new[] { "clothing", "tools" },
                "Более долговечная одежда и материалы");

            Add("medicine_herbal", "Травяная медицина", TechEra.LateStone,
                new[] { "gathering_advanced", "cooking" },
                "Лечение травами, снижение смертности");

            Add("animal_taming", "Приручение животных", TechEra.LateStone,
                new[] { "language_proto", "cooking" },
                "Одомашнивание волков, коз");

            Add("pottery", "Гончарство", TechEra.LateStone,
                new[] { "fire_control", "building" },
                "Хранение пищи и воды");

            Add("language_complex", "Развитой язык", TechEra.LateStone,
                new[] { "language_proto", "burial" },
                "Сложные предложения, истории, передача знаний");

            // === НЕОЛИТ ===
            Add("agriculture", "Земледелие", TechEra.Neolithic,
                new[] { "gathering_advanced", "tools", "pottery" },
                "Выращивание злаков и овощей");

            Add("animal_husbandry", "Скотоводство", TechEra.Neolithic,
                new[] { "animal_taming", "building" },
                "Разведение животных для еды и материалов");

            Add("weaving", "Ткачество", TechEra.Neolithic,
                new[] { "tanning", "tools" },
                "Ткани из растительных волокон");

            Add("stone_wall", "Каменные стены", TechEra.Neolithic,
                new[] { "building", "tools" },
                "Укрепления, защита от хищников и врагов");

            Add("trade_system", "Торговля", TechEra.Neolithic,
                new[] { "language_complex", "pottery" },
                "Обмен ресурсами между группами");

            Add("fermentation", "Ферментация", TechEra.Neolithic,
                new[] { "pottery", "agriculture" },
                "Хранение пищи, напитки");

            Add("astronomy_basic", "Наблюдение за звёздами", TechEra.Neolithic,
                new[] { "language_complex" },
                "Отслеживание сезонов, навигация");

            Add("art", "Искусство", TechEra.Neolithic,
                new[] { "language_complex" },
                "Наскальная живопись, скульптура, музыка");
        }

        static void Add(string id, string name, TechEra era,
                        string[] prereqs, string desc)
        {
            Techs[id] = new TechInfo
            {
                Id = id,
                Name = name,
                Era = era,
                Prerequisites = prereqs,
                Description = desc,
                DiscoveryDifficulty = (int)era * 0.3 + prereqs.Length * 0.2
            };
        }

        public static string[] GetPrerequisites(string techId)
        {
            return Techs.ContainsKey(techId) ? Techs[techId].Prerequisites : new string[0];
        }

        public static TechInfo GetInfo(string techId)
        {
            return Techs.ContainsKey(techId) ? Techs[techId] : null;
        }

        public static List<TechInfo> GetAll() => Techs.Values.ToList();

        /// <summary>
        /// Какие технологии доступны для открытия (все пререквизиты есть)
        /// </summary>
        public static List<string> GetAvailable(HashSet<string> known)
        {
            var available = new List<string>();
            foreach (var kvp in Techs)
            {
                if (known.Contains(kvp.Key)) continue;
                if (kvp.Value.Prerequisites.All(p => known.Contains(p)))
                    available.Add(kvp.Key);
            }
            return available;
        }

        /// <summary>
        /// Попытка спонтанного открытия технологии
        /// </summary>
        public static string TryDiscover(Human human, Random rng)
        {
            var available = GetAvailable(human.KnownTechnologies);
            if (available.Count == 0) return null;

            foreach (var techId in available)
            {
                var tech = Techs[techId];
                double chance = 0.0001 *
                    human.Genes.EffCreativity *
                    human.Genes.EffIntelligence *
                    (1 + human.SkillCrafting * 0.5) /
                    (1 + tech.DiscoveryDifficulty);

                // Больше людей рядом = больше шанс (коллективный разум)
                // Обрабатывается в World.cs

                if (rng.NextDouble() < chance)
                {
                    human.KnownTechnologies.Add(techId);
                    return techId;
                }
            }
            return null;
        }

        /// <summary>
        /// Бонусы от технологии
        /// </summary>
        public static TechBonus GetBonus(string techId)
        {
            return techId switch
            {
                "fire_control" => new TechBonus { WarmthBonus = 0.3, PredatorDefense = 0.3 },
                "stone_tools" => new TechBonus { GatheringBonus = 0.3, CraftingBonus = 0.2 },
                "cooking" => new TechBonus { FoodEfficiency = 0.4, DiseaseResistance = 0.2 },
                "spear" => new TechBonus { HuntingBonus = 0.5, AttackBonus = 0.3 },
                "clothing" => new TechBonus { WarmthBonus = 0.4 },
                "building" => new TechBonus { ShelterQuality = 0.5 },
                "bow" => new TechBonus { HuntingBonus = 0.7, AttackBonus = 0.4 },
                "medicine_herbal" => new TechBonus { HealingBonus = 0.4, DiseaseResistance = 0.3 },
                "agriculture" => new TechBonus { FoodProduction = 0.8, Sedentism = 0.5 },
                "animal_husbandry" => new TechBonus { FoodProduction = 0.5, MaterialBonus = 0.3 },
                "pottery" => new TechBonus { FoodStorage = 0.5 },
                "trade_system" => new TechBonus { TradeBonus = 0.5 },
                _ => new TechBonus()
            };
        }
    }

    public class TechInfo
    {
        public string Id;
        public string Name;
        public TechEra Era;
        public string[] Prerequisites;
        public string Description;
        public double DiscoveryDifficulty;
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