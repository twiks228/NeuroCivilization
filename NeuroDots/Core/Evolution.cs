using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Управление эволюцией: естественный отбор, генетический дрейф,
    /// половой отбор, групповой отбор
    /// </summary>
    public class EvolutionManager
    {
        public int Generation;
        public double MutationRate = 0.15;
        public double MutationStrength = 0.12;

        // Статистика по поколениям
        public List<GenerationStats> History = new();

        Random rng;

        public EvolutionManager(Random random = null)
        {
            rng = random ?? new Random();
        }

        /// <summary>
        /// Выбрать партнёра для размножения (половой отбор)
        /// </summary>
        public Human SelectMate(Human seeker, List<Human> candidates)
        {
            if (candidates.Count == 0) return null;

            // Фильтр: другой пол, взрослый, не родственник, одного вида
            var eligible = candidates.Where(c =>
                c.IsAlive &&
                c.Sex != seeker.Sex &&
                c.IsAdult &&
                !c.IsPregnant &&
                c.Genes.SameSpecies(seeker.Genes) &&
                seeker.GetGeneticDistance(c) > 0.05 // Избегание инцеста
            ).ToList();

            if (eligible.Count == 0) return null;

            // Половой отбор — оценка привлекательности
            var scored = eligible.Select(c => new
            {
                Human = c,
                Score = CalculateAttractiveness(c, seeker)
            }).OrderByDescending(s => s.Score).ToList();

            // Турнирный отбор из топ-3
            int topN = Math.Min(3, scored.Count);
            return scored[rng.Next(topN)].Human;
        }

        double CalculateAttractiveness(Human candidate, Human evaluator)
        {
            double score = 0;

            // Физическое здоровье
            score += (candidate.Health / candidate.MaxHealth) * 0.2;
            score += candidate.Genes.PhysicalStrength * 0.1;
            score += candidate.Genes.EffMaxSpeed * 0.001;

            // Ресурсы и статус
            score += candidate.Reputation * 0.15;
            if (candidate.IsLeader) score += 0.2;
            score += candidate.Inventory.Count * 0.05;

            // Навыки
            score += (candidate.SkillHunting + candidate.SkillBuilding) * 0.1;

            // Генетическое разнообразие (MHC-подобная система)
            double geneticDist = evaluator.GetGeneticDistance(candidate);
            score += Math.Min(geneticDist * 2, 0.3); // Умеренное различие лучше

            // Социальность
            score += candidate.Genes.EffSociability * 0.1;

            // Возраст (предпочтение взрослых в расцвете)
            if (candidate.Age > 16 && candidate.Age < 40) score += 0.1;

            // Отношения
            score += evaluator.GetRelationStrength(candidate.Id) * 0.15;

            return score;
        }

        /// <summary>
        /// Групповой отбор — более успешные племена размножаются лучше
        /// </summary>
        public void ApplyGroupSelection(List<Tribe> tribes, List<Human> humans)
        {
            foreach (var tribe in tribes)
            {
                if (tribe.MemberIds.Count == 0) continue;

                double tribeFitness = tribe.AverageFitness;
                var members = humans.Where(h => tribe.MemberIds.Contains(h.Id) && h.IsAlive);

                foreach (var m in members)
                {
                    // Высокоуспешные племена → мозги членов обучаются быстрее
                    if (tribeFitness > 50)
                        m.Brain.Reward(0.01);
                }
            }
        }

        /// <summary>
        /// Анализ поколения
        /// </summary>
        public void RecordGeneration(List<Human> humans, List<Tribe> tribes)
        {
            var alive = humans.Where(h => h.IsAlive).ToList();
            if (alive.Count == 0) return;

            var stats = new GenerationStats
            {
                Generation = Generation,
                Population = alive.Count,
                AvgFitness = alive.Average(h => h.CalculateFitness()),
                AvgIntelligence = alive.Average(h => h.Genes.EffIntelligence),
                AvgSociability = alive.Average(h => h.Genes.EffSociability),
                AvgCreativity = alive.Average(h => h.Genes.EffCreativity),
                AvgSpeed = alive.Average(h => h.Genes.EffMaxSpeed),
                AvgLifeSpan = alive.Average(h => h.Genes.EffLifeSpan),
                TotalTechs = tribes.SelectMany(t => t.Technologies).Distinct().Count(),
                TribeCount = tribes.Count(t => t.MemberIds.Count > 0),
                AvgStressEpigenetic = alive.Average(h => h.Genes.StressEpigenetic),
                AvgTeloLength = alive.Average(h => h.Genes.TeloLength),
                GeneticDiversity = CalculateGeneticDiversity(alive)
            };

            History.Add(stats);
            Generation++;
        }

        double CalculateGeneticDiversity(List<Human> population)
        {
            if (population.Count < 2) return 0;

            double totalDist = 0;
            int comparisons = 0;
            int sampleSize = Math.Min(population.Count, 20);

            for (int i = 0; i < sampleSize; i++)
            {
                for (int j = i + 1; j < sampleSize; j++)
                {
                    totalDist += population[i].GetGeneticDistance(population[j]);
                    comparisons++;
                }
            }

            return comparisons > 0 ? totalDist / comparisons : 0;
        }

        /// <summary>
        /// Генетический дрейф (случайное изменение частот аллелей при малой популяции)
        /// </summary>
        public void ApplyGeneticDrift(List<Human> population)
        {
            if (population.Count > 50) return; // Дрейф только при малой популяции

            double driftStrength = 0.1 / Math.Max(1, population.Count * 0.1);

            foreach (var h in population)
            {
                if (!h.IsAlive) continue;
                if (rng.NextDouble() < driftStrength)
                    h.Genes.Mutate(0.05, 0.05); // Слабые случайные мутации
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