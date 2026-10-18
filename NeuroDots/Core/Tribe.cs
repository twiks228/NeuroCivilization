using System;
using System.Collections.Generic;
using System.Drawing;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Племя — социальная структура с иерархией, культурой,
    /// технологиями, территорией, общим хранилищем и дипломатией.
    /// Полностью оптимизировано (без O(N^2) LINQ-поисков).
    /// </summary>
    public class Tribe
    {
        public int Id;
        public string Name;
        public Color BannerColor;

        public List<int> MemberIds = new();
        private readonly HashSet<int> _memberSet = new();
        private readonly List<Human> _cachedMembers = new(32);

        public int LeaderId = -1;
        public int Population => MemberIds.Count;

        public double CenterX, CenterY;
        public double TerritoryRadius = 280;
        public List<(double X, double Y)> TerritoryMarkers = new();

        public double FoodStorage = 20;
        public double MaterialStorage = 10;
        public double ToolCount;
        public double WeaponCount;

        public HashSet<string> Technologies = new();
        public double CultureLevel;
        public double TechLevel;
        public List<int> SharedVocabulary = new();
        private readonly HashSet<int> _sharedVocabSet = new();

        public double CooperationNorm;
        public double AggressionNorm;
        public double SharingNorm;

        public Dictionary<int, TribeDiplomacy> Diplomacy = new();

        public double AverageFitness;
        public double AverageAge;
        public int TotalBirths;
        public int TotalDeaths;
        public int GenerationCount;
        public double ExistenceDuration;

        private double _leaderElectionTimer;

        private static int _nextId;
        private static Random R => Random.Shared;

        public Tribe(string? name = null)
        {
            Id = _nextId++;
            Name = name ?? GenerateTribeName();
            BannerColor = Color.FromArgb(
                65 + R.Next(180), 65 + R.Next(180), 65 + R.Next(180));

            CooperationNorm = 0.55;
            AggressionNorm = 0.25;
            SharingNorm = 0.50;
        }

        public static void ResetIds() => _nextId = 0;

        private static string GenerateTribeName()
        {
            string[] prefixes = { "Ak", "Bor", "Kal", "Dur", "Esh", "Gar", "Hal", "Isk", "Vor", "Tal", "Nar", "Zor" };
            string[] suffixes = { "ani", "ori", "oka", "umi", "ena", "ara", "iri", "ota", "ari", "una" };
            return prefixes[R.Next(prefixes.Length)] + suffixes[R.Next(suffixes.Length)];
        }

        public void Update(double dt, Dictionary<int, Human> humanMap)
        {
            ExistenceDuration += dt;
            _leaderElectionTimer += dt;

            _cachedMembers.Clear();
            for (int i = MemberIds.Count - 1; i >= 0; i--)
            {
                int id = MemberIds[i];
                if (humanMap.TryGetValue(id, out var h) && h.IsAlive)
                {
                    _cachedMembers.Add(h);
                }
                else
                {
                    MemberIds.RemoveAt(i);
                    _memberSet.Remove(id);
                    if (LeaderId == id) LeaderId = -1;
                }
            }

            int count = _cachedMembers.Count;
            if (count == 0) return;

            double sumX = 0, sumY = 0, sumFit = 0, sumAge = 0;
            for (int i = 0; i < count; i++)
            {
                var m = _cachedMembers[i];
                sumX += m.X;
                sumY += m.Y;
                sumFit += m.CalculateFitness();
                sumAge += m.Age;
            }

            // Плавное смещение центра территории к членам племени
            double targetCX = sumX / count;
            double targetCY = sumY / count;
            if (CenterX == 0 && CenterY == 0)
            {
                CenterX = targetCX;
                CenterY = targetCY;
            }
            else
            {
                CenterX += (targetCX - CenterX) * Math.Min(1.0, dt * 0.03);
                CenterY += (targetCY - CenterY) * Math.Min(1.0, dt * 0.03);
            }

            TerritoryRadius = Math.Clamp(220 + count * 10 + Technologies.Count * 8, 220, 480);

            double newAvgFit = sumFit / count;
            AverageAge = sumAge / count;

            // Lineage rule: elections ONLY when the leader is gone; the named heir inherits otherwise.
            if (LeaderId == -1 || !_memberSet.Contains(LeaderId))
            {
                ElectLeader(_cachedMembers);
                _leaderElectionTimer = 0;
            }

            SpreadTechnology(_cachedMembers, dt);
            UpdateCulture(_cachedMembers, newAvgFit, dt);
            AverageFitness = newAvgFit;

            DistributeResources(_cachedMembers, dt);
            UpdateDiplomacy(dt);
        }

        /// <summary>Named heir of the current leader (the runner-up of the last election). Inherits without re-election.</summary>
        public int HeirId = -1;

        private void ElectLeader(List<Human> members)
        {
            if (members.Count == 0) return;

            // The named heir takes over automatically — stable lineage, no daily re-election.
            if (HeirId != -1)
                for (int i = 0; i < members.Count; i++)
                    if (members[i].Id == HeirId && !members[i].IsChild)
                    {
                        for (int k = 0; k < members.Count; k++)
                            if (members[k].Id == LeaderId) members[k].IsLeader = false;
                        LeaderId = members[i].Id;
                        members[i].IsLeader = true;
                        HeirId = -1;
                        return;
                    }

            Human bestLeader = members[0];
            double bestScore = -1e9;
            Human secondLeader = members[0];
            double secondScore = -1e9;

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.IsChild) continue;
                double score =
                    m.Genes.EffLeadership * 0.35 +
                    m.SkillLeading * 0.25 +
                    m.Reputation * 0.2 +
                    Math.Min(m.Age, 50) * 0.01 +
                    m.CalculateFitness() * 0.002 +
                    (m.Genes.EffSociability > 0.5 ? 0.1 : 0) +
                    m.ChildrenBorn * 0.05;

                if (score > bestScore)
                {
                    secondScore = bestScore; secondLeader = bestLeader;
                    bestScore = score; bestLeader = m;
                }
                else if (score > secondScore) { secondScore = score; secondLeader = m; }
            }

            HeirId = secondLeader.Id != bestLeader.Id ? secondLeader.Id : -1;

            if (LeaderId != bestLeader.Id)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i].Id == LeaderId)
                        members[i].IsLeader = false;
                }

                LeaderId = bestLeader.Id;
                bestLeader.IsLeader = true;
                bestLeader.SkillLeading = Math.Min(1.0, bestLeader.SkillLeading + 0.03);
            }
        }

        private void SpreadTechnology(List<Human> members, double dt)
        {
            for (int i = 0; i < members.Count; i++)
            {
                foreach (var tech in members[i].KnownTechnologies)
                    Technologies.Add(tech);
            }

            if (Technologies.Count == 0) return;

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.KnownTechnologies.Count >= Technologies.Count) continue;
                double learnProb = dt * 0.008 * m.Genes.LearningSpeed * (0.5 + CooperationNorm);
                foreach (var tech in Technologies)
                {
                    if (!m.KnownTechnologies.Contains(tech) && R.NextDouble() < learnProb)
                        m.LearnTechnology(tech);
                }
            }

            TechLevel = Technologies.Count * 0.5;
        }

        public bool TryDiscoverTech(string tech, Human discoverer)
        {
            if (Technologies.Contains(tech)) return false;

            var prereqs = TechnologyTree.GetPrerequisites(tech);
            for (int i = 0; i < prereqs.Length; i++)
                if (!Technologies.Contains(prereqs[i])) return false;

            double chance = discoverer.Genes.EffCreativity * discoverer.Genes.EffIntelligence * 0.015;
            if (R.NextDouble() < chance)
            {
                Technologies.Add(tech);
                discoverer.LearnTechnology(tech);
                discoverer.Brain.Reward(0.85);
                discoverer.Reputation += 0.25;
                return true;
            }
            return false;
        }

        private void UpdateCulture(List<Human> members, double currentAvgFitness, double dt)
        {
            if (currentAvgFitness >= AverageFitness)
            {
                CultureLevel += dt * 0.002 * (1.0 + Technologies.Count * 0.1);
                CooperationNorm = Math.Min(1.0, CooperationNorm + dt * 0.0004);
                SharingNorm = Math.Min(1.0, SharingNorm + dt * 0.0003);
            }
            else
            {
                CooperationNorm += (R.NextDouble() - 0.48) * dt * 0.001;
                AggressionNorm += (R.NextDouble() - 0.5) * dt * 0.001;
                SharingNorm += (R.NextDouble() - 0.48) * dt * 0.001;
            }

            CooperationNorm = Math.Clamp(CooperationNorm, 0.1, 1.0);
            AggressionNorm = Math.Clamp(AggressionNorm, 0.05, 1.0);
            SharingNorm = Math.Clamp(SharingNorm, 0.1, 1.0);

            for (int i = 0; i < members.Count; i++)
            {
                var words = members[i].KnownWords;
                for (int w = 0; w < words.Count; w++)
                {
                    int wc = words[w];
                    if (_sharedVocabSet.Add(wc))
                        SharedVocabulary.Add(wc);
                }
            }
        }

        private void DistributeResources(List<Human> members, double dt)
        {
            // Земледелие и скотоводство генерируют пассивный приток еды в хранилище племени
            if (Technologies.Contains("agriculture"))
                FoodStorage = Math.Min(MaxFoodStorage, FoodStorage + dt * 0.04 * Math.Min(members.Count, 15));
            if (Technologies.Contains("animal_husbandry"))
                FoodStorage = Math.Min(MaxFoodStorage, FoodStorage + dt * 0.025 * Math.Min(members.Count, 15));

            if (SharingNorm < 0.2 || FoodStorage < 1.0) return;

            for (int i = 0; i < members.Count; i++)
            {
                var h = members[i];
                if (h.Hunger > 0.50 && FoodStorage >= 1.5)
                {
                    double give = Math.Min(FoodStorage, 5.0);
                    h.Eat(give);
                    FoodStorage -= give * 0.5;
                    h.Brain.Social(0.08);
                }
            }
        }

        public double MaxFoodStorage => Technologies.Contains("pottery") ? 1500 : 600;

        public void ContributeFood(double amount) =>
            FoodStorage = Math.Min(MaxFoodStorage, FoodStorage + amount);

        public void ContributeMaterial(double amount) =>
            MaterialStorage = Math.Min(600, MaterialStorage + amount);

        private void UpdateDiplomacy(double dt)
        {
            foreach (var kvp in Diplomacy)
            {
                var d = kvp.Value;
                d.Relation += (0 - d.Relation) * dt * 0.0005;
                d.TradeBenefit *= 0.9995;
            }
        }

        public TribeDiplomacy GetDiplomacy(int otherTribeId)
        {
            if (!Diplomacy.TryGetValue(otherTribeId, out var d))
            {
                d = new TribeDiplomacy { TribeId = otherTribeId };
                Diplomacy[otherTribeId] = d;
            }
            return d;
        }

        public void ImproveRelations(int otherTribeId, double amount)
        {
            var d = GetDiplomacy(otherTribeId);
            d.Relation = Math.Min(1.0, d.Relation + amount);
        }

        public void WorsenRelations(int otherTribeId, double amount)
        {
            var d = GetDiplomacy(otherTribeId);
            d.Relation = Math.Max(-1.0, d.Relation - amount);
        }

        public bool IsAtWar(int otherTribeId) =>
            Diplomacy.TryGetValue(otherTribeId, out var d) && d.Relation < -0.55;

        public bool IsAllied(int otherTribeId) =>
            Diplomacy.TryGetValue(otherTribeId, out var d) && d.Relation > 0.45;

        public void AddMember(Human human)
        {
            if (_memberSet.Add(human.Id))
            {
                MemberIds.Add(human.Id);
                human.TribeId = Id;

                foreach (var tech in Technologies)
                {
                    if (R.NextDouble() < 0.65)
                        human.LearnTechnology(tech);
                }
            }
        }

        public void RemoveMember(int humanId)
        {
            if (_memberSet.Remove(humanId))
            {
                MemberIds.Remove(humanId);
                if (LeaderId == humanId)
                {
                    LeaderId = -1;
                    _leaderElectionTimer = 999;
                }
            }
        }

        public bool HasMember(int humanId) => _memberSet.Contains(humanId);

        public bool IsInTerritory(double x, double y)
        {
            double dx = x - CenterX, dy = y - CenterY;
            return dx * dx + dy * dy < TerritoryRadius * TerritoryRadius;
        }

        public double MilitaryPower(List<Human> all)
        {
            double power = 0;
            for (int i = 0; i < _cachedMembers.Count; i++)
            {
                var m = _cachedMembers[i];
                if (!m.IsAlive) continue;
                power += m.Genes.PhysicalStrength * (1.0 + m.SkillFighting) *
                         (m.EquippedWeapon != null ? m.EquippedWeapon.Modifier : 1.0);
            }
            return power;
        }
    }

    public class TribeDiplomacy
    {
        public int TribeId;
        public double Relation;
        public double TradeBenefit;
        public int Battles;
        public int TradeCount;

        /// <summary>5 visible stages shown in the tribe panel and chronicle.</summary>
        public string Stage =>
            Relation < -0.55 ? "War" :
            Relation < -0.15 ? "Hostile" :
            Relation > 0.45 ? "Alliance" :
            Relation > 0.15 ? "Trade" : "Neutral";

        /// <summary>Diplomacy stage color for the UI.</summary>
        public static System.Drawing.Color StageColor(string stage) => stage switch
        {
            "War" => System.Drawing.Color.IndianRed,
            "Hostile" => System.Drawing.Color.Orange,
            "Trade" => System.Drawing.Color.LightSkyBlue,
            "Alliance" => System.Drawing.Color.LimeGreen,
            _ => System.Drawing.Color.Silver
        };
    }
}
