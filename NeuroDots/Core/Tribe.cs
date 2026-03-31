using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Племя — социальная структура с иерархией, культурой,
    /// технологиями, территорией, дипломатией
    /// </summary>
    public class Tribe
    {
        public int Id;
        public string Name;
        public Color BannerColor;

        // Члены
        public List<int> MemberIds = new();
        public int LeaderId = -1;
        public int Population => MemberIds.Count;

        // Территория
        public double CenterX, CenterY;
        public double TerritoryRadius = 300;
        public List<(double X, double Y)> TerritoryMarkers = new();

        // Ресурсы
        public double FoodStorage;
        public double MaterialStorage;
        public double ToolCount;
        public double WeaponCount;

        // Культура
        public HashSet<string> Technologies = new();
        public double CultureLevel;           // Общий уровень культуры
        public double TechLevel;              // Уровень технологий (0-10)
        public List<int> SharedVocabulary = new(); // Общие слова
        public double CooperationNorm;        // Норма кооперации (0-1)
        public double AggressionNorm;         // Норма агрессии (0-1)
        public double SharingNorm;            // Норма делиться (0-1)

        // Дипломатия
        public Dictionary<int, TribeDiplomacy> Diplomacy = new();

        // Статистика
        public double AverageFitness;
        public double AverageAge;
        public int TotalBirths;
        public int TotalDeaths;
        public int GenerationCount;
        public double ExistenceDuration;

        // Внутренние
        double leaderElectionTimer;

        static int nextId = 0;
        static Random R = new();

        public Tribe(string name = null)
        {
            Id = nextId++;
            Name = name ?? GenerateTribeName();
            BannerColor = Color.FromArgb(
                50 + R.Next(200), 50 + R.Next(200), 50 + R.Next(200));

            CooperationNorm = 0.5;
            AggressionNorm = 0.3;
            SharingNorm = 0.4;
        }

        string GenerateTribeName()
        {
            string[] prefixes = { "Ak", "Bor", "Kal", "Dur", "Esh", "Gar", "Hal", "Isk" };
            string[] suffixes = { "ani", "ori", "oka", "umi", "ena", "ara", "iri", "ota" };
            return prefixes[R.Next(prefixes.Length)] + suffixes[R.Next(suffixes.Length)];
        }

        // ====== ОБНОВЛЕНИЕ ======

        public void Update(double dt, List<Human> allHumans)
        {
            ExistenceDuration += dt;
            leaderElectionTimer += dt;

            // Удалить мёртвых
            MemberIds.RemoveAll(id =>
            {
                var h = allHumans.FirstOrDefault(h => h.Id == id);
                return h == null || !h.IsAlive;
            });

            if (MemberIds.Count == 0) return;

            // Обновить центр
            var members = GetMembers(allHumans);
            CenterX = members.Average(m => m.X);
            CenterY = members.Average(m => m.Y);

            // Средние показатели
            AverageFitness = members.Average(m => m.CalculateFitness());
            AverageAge = members.Average(m => m.Age);

            // Выборы лидера каждые N секунд
            if (leaderElectionTimer > 30 || LeaderId == -1 ||
                !MemberIds.Contains(LeaderId))
            {
                ElectLeader(members);
                leaderElectionTimer = 0;
            }

            // Распространение технологий внутри племени
            SpreadTechnology(members, dt);

            // Эволюция культурных норм
            UpdateCulture(members, dt);

            // Распределение ресурсов
            DistributeResources(members, dt);

            // Обновление дипломатии
            UpdateDiplomacy(dt);
        }

        // ====== ЛИДЕРСТВО ======

        void ElectLeader(List<Human> members)
        {
            if (members.Count == 0) return;

            // Лидер = кто имеет лучшую комбинацию лидерских качеств
            Human bestLeader = members.OrderByDescending(m =>
                m.Genes.EffLeadership * 0.3 +
                m.SkillLeading * 0.2 +
                m.Reputation * 0.2 +
                m.Age * 0.01 +
                m.CalculateFitness() * 0.001 +
                (m.Genes.EffSociability > 0.5 ? 0.1 : 0) +
                m.ChildrenBorn * 0.05
            ).First();

            // Смена лидера
            if (LeaderId != bestLeader.Id)
            {
                // Старый лидер теряет статус
                var oldLeader = members.FirstOrDefault(m => m.Id == LeaderId);
                if (oldLeader != null) oldLeader.IsLeader = false;

                LeaderId = bestLeader.Id;
                bestLeader.IsLeader = true;
                bestLeader.SkillLeading = Math.Min(1, bestLeader.SkillLeading + 0.02);
            }
        }

        // ====== ТЕХНОЛОГИИ ======

        void SpreadTechnology(List<Human> members, double dt)
        {
            // Собрать все технологии от членов
            foreach (var m in members)
                foreach (var tech in m.KnownTechnologies)
                    Technologies.Add(tech);

            // Распространить общие технологии обратно к членам
            foreach (var m in members)
                foreach (var tech in Technologies)
                    if (!m.KnownTechnologies.Contains(tech))
                        if (R.NextDouble() < dt * 0.002 * m.Genes.LearningSpeed)
                            m.KnownTechnologies.Add(tech);

            TechLevel = Technologies.Count * 0.5;
        }

        /// <summary>
        /// Попытка открытия новой технологии
        /// </summary>
        public bool TryDiscoverTech(string tech, Human discoverer)
        {
            if (Technologies.Contains(tech)) return false;

            var prereqs = TechnologyTree.GetPrerequisites(tech);
            if (!prereqs.All(p => Technologies.Contains(p))) return false;

            double chance = discoverer.Genes.EffCreativity *
                            discoverer.Genes.EffIntelligence * 0.01;
            if (R.NextDouble() < chance)
            {
                Technologies.Add(tech);
                discoverer.KnownTechnologies.Add(tech);
                discoverer.Brain.Reward(0.8);
                discoverer.Reputation += 0.2;
                return true;
            }
            return false;
        }

        // ====== КУЛЬТУРА ======

        void UpdateCulture(List<Human> members, double dt)
        {
            // Нормы эволюционируют на основе успешности
            double avgSuccess = members.Average(m => m.CalculateFitness());

            // Если племя процветает → нормы закрепляются
            if (avgSuccess > AverageFitness)
            {
                // Положительное подкрепление текущих норм
                CultureLevel += dt * 0.001;
            }
            else
            {
                // Нормы дрейфуют
                CooperationNorm += (R.NextDouble() - 0.5) * dt * 0.001;
                AggressionNorm += (R.NextDouble() - 0.5) * dt * 0.001;
                SharingNorm += (R.NextDouble() - 0.5) * dt * 0.001;
            }

            CooperationNorm = Math.Clamp(CooperationNorm, 0, 1);
            AggressionNorm = Math.Clamp(AggressionNorm, 0, 1);
            SharingNorm = Math.Clamp(SharingNorm, 0, 1);

            // Общий словарь
            foreach (var m in members)
                foreach (var word in m.KnownWords)
                    if (!SharedVocabulary.Contains(word))
                        SharedVocabulary.Add(word);
        }

        // ====== РЕСУРСЫ ======

        void DistributeResources(List<Human> members, double dt)
        {
            if (SharingNorm < 0.3) return; // Не делятся

            // Лидер координирует распределение
            var hungry = members.Where(m => m.Hunger > 0.6).OrderByDescending(m => m.Hunger);
            foreach (var h in hungry)
            {
                if (FoodStorage > 1)
                {
                    h.Eat(0.3);
                    FoodStorage -= 1;
                    h.Brain.Social(0.1);
                }
            }
        }

        /// <summary>
        /// Добавить ресурсы в хранилище племени
        /// </summary>
        public void ContributeFood(double amount) =>
            FoodStorage = Math.Min(1000, FoodStorage + amount);
        public void ContributeMaterial(double amount) =>
            MaterialStorage = Math.Min(500, MaterialStorage + amount);

        // ====== ДИПЛОМАТИЯ ======

        void UpdateDiplomacy(double dt)
        {
            foreach (var kvp in Diplomacy)
            {
                var d = kvp.Value;
                // Отношения затухают к нейтральным
                d.Relation += (0 - d.Relation) * dt * 0.001;
                d.TradeBenefit *= 0.999;
            }
        }

        public TribeDiplomacy GetDiplomacy(int otherTribeId)
        {
            if (!Diplomacy.ContainsKey(otherTribeId))
                Diplomacy[otherTribeId] = new TribeDiplomacy { TribeId = otherTribeId };
            return Diplomacy[otherTribeId];
        }

        public void ImproveRelations(int otherTribeId, double amount)
        {
            var d = GetDiplomacy(otherTribeId);
            d.Relation = Math.Min(1, d.Relation + amount);
        }

        public void WorsenRelations(int otherTribeId, double amount)
        {
            var d = GetDiplomacy(otherTribeId);
            d.Relation = Math.Max(-1, d.Relation - amount);
        }

        public bool IsAtWar(int otherTribeId)
        {
            return Diplomacy.ContainsKey(otherTribeId) &&
                   Diplomacy[otherTribeId].Relation < -0.6;
        }

        public bool IsAllied(int otherTribeId)
        {
            return Diplomacy.ContainsKey(otherTribeId) &&
                   Diplomacy[otherTribeId].Relation > 0.5;
        }

        // ====== ДОБАВЛЕНИЕ/УДАЛЕНИЕ ЧЛЕНОВ ======

        public void AddMember(Human human)
        {
            if (!MemberIds.Contains(human.Id))
            {
                MemberIds.Add(human.Id);
                human.TribeId = Id;

                // Новый член получает культурные нормы
                foreach (var tech in Technologies)
                    if (R.NextDouble() < 0.5)
                        human.KnownTechnologies.Add(tech);
            }
        }

        public void RemoveMember(int humanId)
        {
            MemberIds.Remove(humanId);
            if (LeaderId == humanId)
            {
                LeaderId = -1;
                leaderElectionTimer = 999; // Немедленные перевыборы
            }
        }

        // ====== УТИЛИТЫ ======

        List<Human> GetMembers(List<Human> all) =>
            all.Where(h => MemberIds.Contains(h.Id) && h.IsAlive).ToList();

        public bool IsInTerritory(double x, double y)
        {
            double dx = x - CenterX, dy = y - CenterY;
            return dx * dx + dy * dy < TerritoryRadius * TerritoryRadius;
        }

        /// <summary>
        /// Военная мощь
        /// </summary>
        public double MilitaryPower(List<Human> all)
        {
            var members = GetMembers(all);
            return members.Sum(m =>
                m.Genes.PhysicalStrength * (1 + m.SkillFighting) *
                (m.EquippedWeapon != null ? m.EquippedWeapon.Modifier : 1));
        }
    }

    public class TribeDiplomacy
    {
        public int TribeId;
        public double Relation;       // -1 (война) ... 0 (нейтралитет) ... +1 (союз)
        public double TradeBenefit;   // Выгода от торговли
        public int Battles;           // Кол-во сражений
        public int TradeCount;        // Кол-во обменов
    }
}