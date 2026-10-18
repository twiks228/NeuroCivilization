using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Управление эволюцией: естественный отбор, генетический дрейф,
    /// половой отбор, групповой отбор.
    /// </summary>
    public class EvolutionManager
    {
        public int Generation;
        public double MutationRate = 0.15;
        public double MutationStrength = 0.12;

        public List<GenerationStats> History = new();

        private readonly Random _rng;

        public EvolutionManager(Random? random = null)
        {
            _rng = random ?? Random.Shared;
        }

        public Human? SelectMate(Human seeker, List<Human>? candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            Human? best1 = null, best2 = null, best3 = null;
            double s1 = -1e9, s2 = -1e9, s3 = -1e9;

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.IsAlive || c.Sex == seeker.Sex || !c.IsAdult || c.IsPregnant) continue;
                if (!c.Genes.SameSpecies(seeker.Genes) || seeker.GetGeneticDistance(c) <= 0.05) continue;

                double score = CalculateAttractiveness(c, seeker);
                if (score > s1)
                {
                    s3 = s2; best3 = best2;
                    s2 = s1; best2 = best1;
                    s1 = score; best1 = c;
                }
                else if (score > s2)
                {
                    s3 = s2; best3 = best2;
                    s2 = score; best2 = c;
                }
                else if (score > s3)
                {
                    s3 = score; best3 = c;
                }
            }

            if (best1 == null) return null;
            int topN = best3 != null ? 3 : best2 != null ? 2 : 1;
            int pick = _rng.Next(topN);
            return pick == 0 ? best1 : pick == 1 ? best2 : best3;
        }

        private static double CalculateAttractiveness(Human candidate, Human evaluator)
        {
            double score = 0;
            score += (candidate.Health / candidate.MaxHealth) * 0.2;
            score += candidate.Genes.PhysicalStrength * 0.1;
            score += candidate.Genes.EffMaxSpeed * 0.001;
            score += candidate.Reputation * 0.15;
            if (candidate.IsLeader) score += 0.2;
            score += candidate.Inventory.Count * 0.05;
            score += (candidate.SkillHunting + candidate.SkillBuilding) * 0.1;

            double geneticDist = evaluator.GetGeneticDistance(candidate);
            score += Math.Min(geneticDist * 2, 0.3);
            score += candidate.Genes.EffSociability * 0.1;
            if (candidate.Age > 16 && candidate.Age < 40) score += 0.1;
            score += evaluator.GetRelationStrength(candidate.Id) * 0.15;

            return score;
        }

        public void ApplyGroupSelection(List<Tribe> tribes, List<Human> humans)
        {
            for (int t = 0; t < tribes.Count; t++)
            {
                var tribe = tribes[t];
                if (tribe.MemberIds.Count == 0 || tribe.AverageFitness <= 50) continue;

                for (int i = 0; i < humans.Count; i++)
                {
                    var m = humans[i];
                    if (m.IsAlive && m.TribeId == tribe.Id)
                        m.Brain.Reward(0.02);
                }
            }
        }

        public void RecordGeneration(List<Human> humans, List<Tribe> tribes)
        {
            int count = 0;
            double sumFit = 0, sumInt = 0, sumSoc = 0, sumCre = 0, sumSpd = 0, sumLife = 0, sumStress = 0, sumTelo = 0;

            for (int i = 0; i < humans.Count; i++)
            {
                var h = humans[i];
                if (!h.IsAlive) continue;
                count++;
                sumFit += h.CalculateFitness();
                sumInt += h.Genes.EffIntelligence;
                sumSoc += h.Genes.EffSociability;
                sumCre += h.Genes.EffCreativity;
                sumSpd += h.Genes.EffMaxSpeed;
                sumLife += h.Genes.EffLifeSpan;
                sumStress += h.Genes.StressEpigenetic;
                sumTelo += h.Genes.TeloLength;
            }

            if (count == 0) return;

            var distinctTechs = new HashSet<string>();
            int activeTribes = 0;
            for (int t = 0; t < tribes.Count; t++)
            {
                if (tribes[t].MemberIds.Count > 0)
                {
                    activeTribes++;
                    foreach (var tech in tribes[t].Technologies)
                        distinctTechs.Add(tech);
                }
            }

            var stats = new GenerationStats
            {
                Generation = Generation,
                Population = count,
                AvgFitness = sumFit / count,
                AvgIntelligence = sumInt / count,
                AvgSociability = sumSoc / count,
                AvgCreativity = sumCre / count,
                AvgSpeed = sumSpd / count,
                AvgLifeSpan = sumLife / count,
                TotalTechs = distinctTechs.Count,
                TribeCount = activeTribes,
                AvgStressEpigenetic = sumStress / count,
                AvgTeloLength = sumTelo / count,
                GeneticDiversity = CalculateGeneticDiversity(humans)
            };

            History.Add(stats);
            if (History.Count > 120) History.RemoveAt(0);
            Generation++;
        }

        private static double CalculateGeneticDiversity(List<Human> population)
        {
            double totalDist = 0;
            int comparisons = 0;
            int sampled = 0;

            for (int i = 0; i < population.Count && sampled < 20; i++)
            {
                var h1 = population[i];
                if (!h1.IsAlive) continue;
                sampled++;

                int sampled2 = 0;
                for (int j = i + 1; j < population.Count && sampled2 < 20; j++)
                {
                    var h2 = population[j];
                    if (!h2.IsAlive) continue;
                    sampled2++;
                    totalDist += h1.GetGeneticDistance(h2);
                    comparisons++;
                }
            }

            return comparisons > 0 ? totalDist / comparisons : 0;
        }

        public void ApplyGeneticDrift(List<Human> population)
        {
            int aliveCount = 0;
            for (int i = 0; i < population.Count; i++)
                if (population[i].IsAlive) aliveCount++;

            if (aliveCount > 50 || aliveCount == 0) return;

            double driftStrength = 0.08 / Math.Max(1.0, aliveCount * 0.1);
            for (int i = 0; i < population.Count; i++)
            {
                var h = population[i];
                if (!h.IsAlive) continue;
                if (_rng.NextDouble() < driftStrength)
                    h.Genes.Mutate(0.05, 0.04);
            }
        }
    }

    public class GenerationStats
    {
        public int Generation;
        public int Population;
        public double AvgFitness;
        public double AvgIntelligence;
        public double AvgSociability;
        public double AvgCreativity;
        public double AvgSpeed;
        public double AvgLifeSpan;
        public int TotalTechs;
        public int TribeCount;
        public double AvgStressEpigenetic;
        public double AvgTeloLength;
        public double GeneticDiversity;
    }
}
