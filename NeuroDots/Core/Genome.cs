using System;
using System.Collections.Generic;
using System.Drawing;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Геном человека с эпигенетикой, метилированием, импринтингом,
    /// возрастными изменениями экспрессии генов и трансгенерационным наследованием
    /// </summary>
    public class Genome
    {
        // ====== БАЗОВЫЕ ГЕНЫ (ДНК — не меняются в течение жизни) ======
        public double BodySize, MaxSpeed, Metabolism, VisionRange;
        public double ThermoTolerance, Stamina;
        public double SkinR, SkinG, SkinB, HairStyle;
        public double Sociability, Aggression, Carefulness;
        public double Intelligence, Leadership;
        public double HuntingSkill, BuildingSkill;
        public double Creativity, Resilience;
        public double LifeSpan;           // Генетический предел жизни
        public double FertilityBase;      // Базовая фертильность
        public double ImmuneStrength;     // Сила иммунной системы
        public double PainTolerance;      // Болевой порог
        public double AdaptationRate;     // Скорость адаптации
        public double MuscleDensity;      // Мускулатура
        public double BoneStrength;       // Крепость костей
        public double LungCapacity;       // Объём лёгких
        public double NeuralPlasticity;   // Пластичность мозга (обучаемость)

        public double[] Species;          // Видовые маркеры (4 гена)

        // ====== ЭПИГЕНЕТИКА (меняется при жизни и частично наследуется) ======
        public double[] Methylation;      // Метилирование генов (подавление)
        public double[] GeneExpression;   // Текущая экспрессия генов
        public double[] HistoneState;     // Состояние гистонов (доступность ДНК)

        // Импринтинг (родительское влияние)
        public double MaternalImprint;    // Материнский импринтинг
        public double PaternalImprint;    // Отцовский импринтинг

        // Стрессовая эпигенетика
        public double StressEpigenetic;       // Накопленный эпигенетический стресс
        public double FamineEpigenetic;       // Эпигенетика голодания
        public double TraumaEpigenetic;       // Травматическая эпигенетика
        public double SocialDeprivation;      // Социальная депривация

        // Возрастная эпигенетика
        public double TeloLength;             // Длина теломер (биологические часы)
        public double EpigeneticAge;          // Эпигенетический возраст
        public double DNARepairEfficiency;    // Эффективность репарации ДНК

        // Питательная эпигенетика
        public double NutritionalStatus;      // Нутрициологический статус
        public double VitaminDEpigenetic;     // Витамин D влияние
        public double OmegaRatio;             // Омега-3/6 баланс

        // Адаптивные эпигенетические маркеры
        public double ColdAdaptation;         // Адаптация к холоду
        public double HeatAdaptation;         // Адаптация к жаре
        public double AltitudeAdaptation;     // Высотная адаптация
        public double DiseaseResistance;      // Приобретённая устойчивость к болезням

        // Количество генов для метилирования/экспрессии
        public const int GENE_COUNT = 24;

        // Имена генов для отладки
        public static readonly string[] GeneNames = {
            "BodySize", "MaxSpeed", "Metabolism", "VisionRange",
            "ThermoTol", "Stamina", "Intelligence", "Sociability",
            "Aggression", "Carefulness", "Leadership", "HuntingSkill",
            "BuildingSkill", "Creativity", "Resilience", "LifeSpan",
            "Fertility", "ImmuneStr", "PainTol", "Adaptation",
            "Muscle", "Bone", "Lung", "NeuralPlast"
        };

        static Random R = new();

        public Genome()
        {
            Methylation = new double[GENE_COUNT];
            GeneExpression = new double[GENE_COUNT];
            HistoneState = new double[GENE_COUNT];
            Species = new double[4];
            TeloLength = 1.0;
            DNARepairEfficiency = 1.0;
            Randomize();
            InitEpigenetics();
        }

        public void Randomize()
        {
            BodySize = 0.7 + R.NextDouble();
            MaxSpeed = 80 + R.NextDouble() * 100;
            Metabolism = 0.6 + R.NextDouble() * 0.8;
            VisionRange = 140 + R.NextDouble() * 220;
            ThermoTolerance = R.NextDouble();
            Stamina = 0.6 + R.NextDouble();
            SkinR = 0.5 + R.NextDouble() * 0.5;
            SkinG = 0.3 + R.NextDouble() * 0.5;
            SkinB = 0.2 + R.NextDouble() * 0.4;
            HairStyle = R.NextDouble();
            Sociability = R.NextDouble();
            Aggression = R.NextDouble() * 0.4;
            Carefulness = R.NextDouble();
            Intelligence = 0.3 + R.NextDouble() * 0.7;
            Leadership = R.NextDouble();
            HuntingSkill = R.NextDouble() * 0.5;
            BuildingSkill = R.NextDouble() * 0.5;
            Creativity = R.NextDouble() * 0.6;
            Resilience = 0.3 + R.NextDouble() * 0.7;
            LifeSpan = 0.6 + R.NextDouble() * 0.4;
            FertilityBase = 0.4 + R.NextDouble() * 0.6;
            ImmuneStrength = 0.3 + R.NextDouble() * 0.7;
            PainTolerance = R.NextDouble();
            AdaptationRate = 0.3 + R.NextDouble() * 0.7;
            MuscleDensity = 0.4 + R.NextDouble() * 0.6;
            BoneStrength = 0.5 + R.NextDouble() * 0.5;
            LungCapacity = 0.5 + R.NextDouble() * 0.5;
            NeuralPlasticity = 0.3 + R.NextDouble() * 0.7;

            for (int i = 0; i < 4; i++)
                Species[i] = R.NextDouble();
        }

        void InitEpigenetics()
        {
            for (int i = 0; i < GENE_COUNT; i++)
            {
                Methylation[i] = R.NextDouble() * 0.2;       // Низкое начальное метилирование
                GeneExpression[i] = 0.8 + R.NextDouble() * 0.2; // Высокая начальная экспрессия
                HistoneState[i] = 0.7 + R.NextDouble() * 0.3;   // Открытый хроматин
            }

            NutritionalStatus = 0.7;
            VitaminDEpigenetic = 0.5;
            OmegaRatio = 0.5;
        }

        // ====== ПОЛУЧИТЬ ЭФФЕКТИВНОЕ ЗНАЧЕНИЕ ГЕНА (ДНК × Эпигенетика) ======

        /// <summary>
        /// Эффективное значение гена = базовый ген × экспрессия × (1 - метилирование)
        /// </summary>
        public double GetEffective(int geneIndex, double baseValue)
        {
            double expression = GeneExpression[geneIndex];
            double methylation = Methylation[geneIndex];
            double histone = HistoneState[geneIndex];

            // Экспрессия = базовая × доступность хроматина × (1 - метилирование)
            double effective = baseValue * expression * histone * (1.0 - methylation * 0.7);

            // Возрастная коррекция
            effective *= GetAgeFactor(geneIndex);

            return effective;
        }

        /// <summary>
        /// Возрастной фактор для разных генов
        /// </summary>
        double GetAgeFactor(int geneIndex)
        {
            double ageFactor = 1.0 - EpigeneticAge * 0.002;

            // Разные гены по-разному деградируют с возрастом
            switch (geneIndex)
            {
                case 1: // MaxSpeed — сильно падает с возрастом
                    return Math.Max(0.3, ageFactor * (1.0 - EpigeneticAge * 0.003));
                case 5: // Stamina — падает
                    return Math.Max(0.4, ageFactor);
                case 6: // Intelligence — может расти до пика
                    double peak = EpigeneticAge < 40 ? 1.0 + EpigeneticAge * 0.005 : 1.2 - (EpigeneticAge - 40) * 0.004;
                    return Math.Clamp(peak, 0.5, 1.3);
                case 15: // LifeSpan — укорачивается
                    return Math.Max(0.1, 1.0 - EpigeneticAge * 0.008);
                case 16: // Fertility — окно фертильности
                    if (EpigeneticAge < 14) return 0;
                    if (EpigeneticAge < 25) return 0.5 + (EpigeneticAge - 14) * 0.045;
                    if (EpigeneticAge < 40) return 1.0;
                    return Math.Max(0, 1.0 - (EpigeneticAge - 40) * 0.05);
                case 23: // NeuralPlasticity — максимум в детстве
                    if (EpigeneticAge < 5) return 1.5;
                    if (EpigeneticAge < 15) return 1.3;
                    if (EpigeneticAge < 25) return 1.0;
                    return Math.Max(0.3, 1.0 - (EpigeneticAge - 25) * 0.005);
                default:
                    return Math.Clamp(ageFactor, 0.3, 1.2);
            }
        }

        // ====== ЭФФЕКТИВНЫЕ СВОЙСТВА (с учётом эпигенетики) ======

        public double EffBodySize => GetEffective(0, BodySize);
        public double EffMaxSpeed => GetEffective(1, MaxSpeed);
        public double EffMetabolism => GetEffective(2, Metabolism);
        public double EffVisionRange => GetEffective(3, VisionRange);
        public double EffThermoTolerance => GetEffective(4, ThermoTolerance) + ColdAdaptation * 0.2 + HeatAdaptation * 0.2;
        public double EffStamina => GetEffective(5, Stamina);
        public double EffIntelligence => GetEffective(6, Intelligence);
        public double EffSociability => GetEffective(7, Sociability);
        public double EffAggression => GetEffective(8, Aggression) * (1 + StressEpigenetic * 0.3);
        public double EffCarefulness => GetEffective(9, Carefulness);
        public double EffLeadership => GetEffective(10, Leadership);
        public double EffHuntingSkill => GetEffective(11, HuntingSkill);
        public double EffBuildingSkill => GetEffective(12, BuildingSkill);
        public double EffCreativity => GetEffective(13, Creativity);
        public double EffResilience => GetEffective(14, Resilience) * (1 + TraumaEpigenetic * 0.1); // Посттравматический рост
        public double EffLifeSpan => GetEffective(15, LifeSpan) * (1 - StressEpigenetic * 0.15);
        public double EffFertility => GetEffective(16, FertilityBase) * (1 - FamineEpigenetic * 0.4) * (1 - StressEpigenetic * 0.2);
        public double EffImmuneStrength => GetEffective(17, ImmuneStrength) * (1 + DiseaseResistance * 0.3);
        public double EffPainTolerance => GetEffective(18, PainTolerance);
        public double EffAdaptation => GetEffective(19, AdaptationRate);
        public double EffMuscle => GetEffective(20, MuscleDensity);
        public double EffBone => GetEffective(21, BoneStrength);
        public double EffLung => GetEffective(22, LungCapacity) * (1 + AltitudeAdaptation * 0.3);
        public double EffNeuralPlasticity => GetEffective(23, NeuralPlasticity);

        // ====== ОБНОВЛЕНИЕ ЭПИГЕНЕТИКИ ======

        /// <summary>
        /// Вызывать каждый тик симуляции. Обновляет эпигенетику на основе условий
        /// </summary>
        public void UpdateEpigenetics(double dt, EpigeneticContext ctx)
        {
            // Старение теломер
            double telomerLoss = dt * 0.0001 * (1 + ctx.Stress * 0.5);
            telomerLoss *= (1 - DNARepairEfficiency * 0.3);
            TeloLength = Math.Max(0, TeloLength - telomerLoss);
            EpigeneticAge += dt * (1 + ctx.Stress * 0.2) * 0.01;

            // Деградация репарации ДНК с возрастом
            DNARepairEfficiency = Math.Max(0.1,
                DNARepairEfficiency - dt * 0.00001 * (1 + ctx.Toxins * 0.3));

            // === СТРЕССОВОЕ МЕТИЛИРОВАНИЕ ===
            if (ctx.Stress > 0.5)
            {
                // Стресс метилирует гены нейропластичности, серотонина, иммунитета
                Methylation[6] += dt * ctx.Stress * 0.001;  // Intelligence
                Methylation[7] += dt * ctx.Stress * 0.002;  // Sociability
                Methylation[17] += dt * ctx.Stress * 0.001; // Immune

                StressEpigenetic = Math.Min(1, StressEpigenetic + dt * ctx.Stress * 0.0005);
            }
            else
            {
                // Восстановление при низком стрессе (деметилирование)
                Methylation[6] = Math.Max(0, Methylation[6] - dt * 0.0002);
                Methylation[7] = Math.Max(0, Methylation[7] - dt * 0.0003);
                StressEpigenetic = Math.Max(0, StressEpigenetic - dt * 0.0001);
            }

            // === ГОЛОДАНИЕ ===
            if (ctx.Hunger > 0.7)
            {
                // Голодание включает гены экономии энергии (thrifty phenotype)
                Methylation[2] -= dt * 0.001;  // Усиление метаболизма запасания
                GeneExpression[2] *= 1 + dt * 0.001; // Повышение экспрессии метаболизма

                FamineEpigenetic = Math.Min(1, FamineEpigenetic + dt * ctx.Hunger * 0.0003);

                // Подавление роста при голоде
                Methylation[0] += dt * 0.0005; // BodySize
                Methylation[5] += dt * 0.0005; // Stamina
            }
            else if (ctx.Hunger < 0.3)
            {
                FamineEpigenetic = Math.Max(0, FamineEpigenetic - dt * 0.0001);
            }

            // === ТРАВМА ===
            if (ctx.Trauma > 0.5)
            {
                // Травма метилирует гены стрессоустойчивости и социальности
                Methylation[8] -= dt * ctx.Trauma * 0.001; // Деметилирование агрессии (активация)
                Methylation[14] += dt * ctx.Trauma * 0.0005; // Метилирование resilience

                TraumaEpigenetic = Math.Min(1, TraumaEpigenetic + dt * ctx.Trauma * 0.0004);

                // Гиперметилирование FKBP5 (стресс-ответ)
                Methylation[9] += dt * ctx.Trauma * 0.001; // Carefulness
            }

            // === СОЦИАЛЬНАЯ СРЕДА ===
            if (ctx.SocialSupport > 0.5)
            {
                // Социальная поддержка = деметилирование генов окситоцина
                Methylation[7] = Math.Max(0, Methylation[7] - dt * ctx.SocialSupport * 0.001);
                SocialDeprivation = Math.Max(0, SocialDeprivation - dt * 0.0003);

                // Улучшение иммунитета через социальность
                GeneExpression[17] = Math.Min(1.2, GeneExpression[17] + dt * 0.0002);
            }
            else if (ctx.SocialSupport < 0.2)
            {
                SocialDeprivation = Math.Min(1, SocialDeprivation + dt * 0.0002);
                Methylation[7] += dt * 0.0005; // Подавление социальности
            }

            // === ТЕМПЕРАТУРНАЯ АДАПТАЦИЯ ===
            if (ctx.Temperature < 0.3)
            {
                ColdAdaptation = Math.Min(1, ColdAdaptation + dt * 0.0002 * AdaptationRate);
                // Экспрессия бурого жира, термогенез
                GeneExpression[4] = Math.Min(1.3, GeneExpression[4] + dt * 0.0003);
            }
            else if (ctx.Temperature > 0.7)
            {
                HeatAdaptation = Math.Min(1, HeatAdaptation + dt * 0.0002 * AdaptationRate);
                // Потоотделение, вазодилатация
                GeneExpression[4] = Math.Min(1.3, GeneExpression[4] + dt * 0.0002);
            }

            // === ВЫСОТНАЯ АДАПТАЦИЯ (эритропоэтин) ===
            if (ctx.Altitude > 0.6)
            {
                AltitudeAdaptation = Math.Min(1,
                    AltitudeAdaptation + dt * 0.0001 * AdaptationRate);
                GeneExpression[22] = Math.Min(1.4, GeneExpression[22] + dt * 0.0002);
            }

            // === ПИТАНИЕ ===
            NutritionalStatus = NutritionalStatus * 0.999 + ctx.NutritionQuality * 0.001;
            if (NutritionalStatus > 0.6)
            {
                // Хорошее питание = деметилирование, лучшая экспрессия
                for (int i = 0; i < GENE_COUNT; i++)
                    Methylation[i] = Math.Max(0, Methylation[i] - dt * 0.00005);
            }

            // === БОЛЕЗНИ → устойчивость ===
            if (ctx.DiseaseExposure > 0.3)
            {
                DiseaseResistance = Math.Min(1,
                    DiseaseResistance + dt * ctx.DiseaseExposure * ImmuneStrength * 0.0003);
            }

            // === ОБУЧЕНИЕ → нейропластичность ===
            if (ctx.LearningIntensity > 0.3)
            {
                // BDNF экспрессия при обучении
                GeneExpression[23] = Math.Min(1.5,
                    GeneExpression[23] + dt * ctx.LearningIntensity * 0.0003);
                Methylation[6] = Math.Max(0,
                    Methylation[6] - dt * ctx.LearningIntensity * 0.0002);
            }

            // === ФИЗИЧЕСКАЯ АКТИВНОСТЬ ===
            if (ctx.PhysicalActivity > 0.5)
            {
                // Упражнения деметилируют гены мышц, выносливости
                Methylation[20] = Math.Max(0, Methylation[20] - dt * 0.0003);
                Methylation[5] = Math.Max(0, Methylation[5] - dt * 0.0002);
                GeneExpression[22] = Math.Min(1.3, GeneExpression[22] + dt * 0.0002);

                // Нейрогенез от физической активности
                GeneExpression[23] = Math.Min(1.3, GeneExpression[23] + dt * 0.0001);
            }

            // === НОРМАЛИЗАЦИЯ ===
            for (int i = 0; i < GENE_COUNT; i++)
            {
                Methylation[i] = Math.Clamp(Methylation[i], 0, 0.95);
                GeneExpression[i] = Math.Clamp(GeneExpression[i], 0.1, 2.0);

                // Гистоны связаны с метилированием
                HistoneState[i] = Math.Clamp(1.0 - Methylation[i] * 0.5, 0.3, 1.0);
            }
        }

        /// <summary>
        /// Критический период развития (детство) — усиленное влияние среды
        /// </summary>
        public void CriticalPeriodUpdate(double dt, EpigeneticContext ctx)
        {
            if (EpigeneticAge > 18) return; // Критический период до 18 лет

            double sensitivity = EpigeneticAge < 5 ? 3.0 :
                                 EpigeneticAge < 12 ? 2.0 : 1.5;

            // Усиленное влияние всех факторов
            var amplifiedCtx = new EpigeneticContext
            {
                Stress = ctx.Stress * sensitivity,
                Hunger = ctx.Hunger * sensitivity,
                Trauma = ctx.Trauma * sensitivity,
                SocialSupport = ctx.SocialSupport * sensitivity,
                Temperature = ctx.Temperature,
                NutritionQuality = ctx.NutritionQuality,
                LearningIntensity = ctx.LearningIntensity * sensitivity,
                PhysicalActivity = ctx.PhysicalActivity,
                Altitude = ctx.Altitude,
                DiseaseExposure = ctx.DiseaseExposure,
                Toxins = ctx.Toxins
            };

            UpdateEpigenetics(dt, amplifiedCtx);

            // Материнский/отцовский импринтинг влияет на развитие
            if (EpigeneticAge < 3)
            {
                GeneExpression[7] *= 1 + MaternalImprint * 0.01 * dt;  // Социальность от матери
                GeneExpression[6] *= 1 + PaternalImprint * 0.01 * dt;  // Интеллект от отца
            }
        }

        // ====== ЦВЕТА ======

        public Color Skin => Color.FromArgb(
            Clamp((int)(SkinR * 255), 70, 255),
            Clamp((int)(SkinG * 200), 50, 200),
            Clamp((int)(SkinB * 180), 40, 180));

        public Color Hair
        {
            get
            {
                // Седина с возрастом
                double gray = EpigeneticAge > 40 ? Math.Min(1, (EpigeneticAge - 40) * 0.02) : 0;
                return Color.FromArgb(
                    Clamp((int)(SkinR * 100 + gray * 140), 15, 200),
                    Clamp((int)(SkinG * 70 + gray * 130), 10, 190),
                    Clamp((int)(SkinB * 50 + gray * 120), 5, 180));
            }
        }

        public Color Clothes => Color.FromArgb(
            Clamp((int)(Species[0] * 200 + 55), 55, 255),
            Clamp((int)(Species[1] * 200 + 55), 55, 255),
            Clamp((int)(Species[2] * 200 + 55), 55, 255));

        // ====== ФИТНЕС ======

        public double GeneticFitness =>
            EffIntelligence * 0.15 + EffStamina * 0.12 + EffMaxSpeed * 0.001 +
            EffSociability * 0.1 + EffHuntingSkill * 0.08 + EffBuildingSkill * 0.08 +
            EffResilience * 0.12 + EffVisionRange * 0.0004 +
            EffImmuneStrength * 0.1 + EffFertility * 0.08 +
            EffNeuralPlasticity * 0.07 + TeloLength * 0.1;

        /// <summary>
        /// Максимальный возраст в годах симуляции
        /// </summary>
        public double MaxAge => 50 + EffLifeSpan * 40 + TeloLength * 10;

        /// <summary>
        /// Физическая сила
        /// </summary>
        public double PhysicalStrength =>
            EffMuscle * EffBodySize * 0.7 + EffBone * 0.3;

        /// <summary>
        /// Скорость обучения
        /// </summary>
        public double LearningSpeed =>
            EffIntelligence * 0.4 + EffNeuralPlasticity * 0.4 + EffCreativity * 0.2;

        // ====== КЛОНИРОВАНИЕ ======

        public Genome Clone()
        {
            var g = new Genome
            {
                BodySize = BodySize,
                MaxSpeed = MaxSpeed,
                Metabolism = Metabolism,
                VisionRange = VisionRange,
                ThermoTolerance = ThermoTolerance,
                Stamina = Stamina,
                SkinR = SkinR,
                SkinG = SkinG,
                SkinB = SkinB,
                HairStyle = HairStyle,
                Sociability = Sociability,
                Aggression = Aggression,
                Carefulness = Carefulness,
                Intelligence = Intelligence,
                Leadership = Leadership,
                HuntingSkill = HuntingSkill,
                BuildingSkill = BuildingSkill,
                Creativity = Creativity,
                Resilience = Resilience,
                LifeSpan = LifeSpan,
                FertilityBase = FertilityBase,
                ImmuneStrength = ImmuneStrength,
                PainTolerance = PainTolerance,
                AdaptationRate = AdaptationRate,
                MuscleDensity = MuscleDensity,
                BoneStrength = BoneStrength,
                LungCapacity = LungCapacity,
                NeuralPlasticity = NeuralPlasticity,
                Species = (double[])Species.Clone(),
                TeloLength = TeloLength,
                DNARepairEfficiency = DNARepairEfficiency,
                MaternalImprint = MaternalImprint,
                PaternalImprint = PaternalImprint
            };

            // Копируем эпигенетику
            Array.Copy(Methylation, g.Methylation, GENE_COUNT);
            Array.Copy(GeneExpression, g.GeneExpression, GENE_COUNT);
            Array.Copy(HistoneState, g.HistoneState, GENE_COUNT);

            g.StressEpigenetic = StressEpigenetic;
            g.FamineEpigenetic = FamineEpigenetic;
            g.TraumaEpigenetic = TraumaEpigenetic;
            g.SocialDeprivation = SocialDeprivation;
            g.ColdAdaptation = ColdAdaptation;
            g.HeatAdaptation = HeatAdaptation;
            g.AltitudeAdaptation = AltitudeAdaptation;
            g.DiseaseResistance = DiseaseResistance;
            g.NutritionalStatus = NutritionalStatus;
            g.EpigeneticAge = EpigeneticAge;

            return g;
        }

        // ====== КРОССОВЕР С ЭПИГЕНЕТИЧЕСКИМ НАСЛЕДОВАНИЕМ ======

        public static Genome Cross(Genome mother, Genome father)
        {
            var c = new Genome
            {
                // Менделевская генетика — случайный выбор аллели
                BodySize = Pick(mother.BodySize, father.BodySize),
                MaxSpeed = Mix(mother.MaxSpeed, father.MaxSpeed),
                Metabolism = Pick(mother.Metabolism, father.Metabolism),
                VisionRange = Mix(mother.VisionRange, father.VisionRange),
                ThermoTolerance = Mix(mother.ThermoTolerance, father.ThermoTolerance),
                Stamina = Pick(mother.Stamina, father.Stamina),
                SkinR = Mix(mother.SkinR, father.SkinR),
                SkinG = Mix(mother.SkinG, father.SkinG),
                SkinB = Mix(mother.SkinB, father.SkinB),
                HairStyle = Pick(mother.HairStyle, father.HairStyle),
                Sociability = Mix(mother.Sociability, father.Sociability),
                Aggression = Mix(mother.Aggression, father.Aggression),
                Carefulness = Mix(mother.Carefulness, father.Carefulness),
                Intelligence = Mix(mother.Intelligence, father.Intelligence),
                Leadership = Mix(mother.Leadership, father.Leadership),
                HuntingSkill = Mix(mother.HuntingSkill, father.HuntingSkill),
                BuildingSkill = Mix(mother.BuildingSkill, father.BuildingSkill),
                Creativity = Mix(mother.Creativity, father.Creativity),
                Resilience = Mix(mother.Resilience, father.Resilience),
                LifeSpan = Mix(mother.LifeSpan, father.LifeSpan),
                FertilityBase = Mix(mother.FertilityBase, father.FertilityBase),
                ImmuneStrength = Mix(mother.ImmuneStrength, father.ImmuneStrength),
                PainTolerance = Mix(mother.PainTolerance, father.PainTolerance),
                AdaptationRate = Mix(mother.AdaptationRate, father.AdaptationRate),
                MuscleDensity = Mix(mother.MuscleDensity, father.MuscleDensity),
                BoneStrength = Mix(mother.BoneStrength, father.BoneStrength),
                LungCapacity = Mix(mother.LungCapacity, father.LungCapacity),
                NeuralPlasticity = Mix(mother.NeuralPlasticity, father.NeuralPlasticity)
            };

            // Видовые маркеры
            c.Species = new double[4];
            for (int i = 0; i < 4; i++)
                c.Species[i] = Pick(mother.Species[i], father.Species[i]);

            // === ТРАНСГЕНЕРАЦИОННОЕ ЭПИГЕНЕТИЧЕСКОЕ НАСЛЕДОВАНИЕ ===
            // Часть эпигенетических меток наследуется (реальный феномен!)

            for (int i = 0; i < GENE_COUNT; i++)
            {
                // 30% метилирования матери + 20% метилирования отца наследуется
                c.Methylation[i] = mother.Methylation[i] * 0.3 + father.Methylation[i] * 0.2;
                // Но большая часть стирается (эпигенетическое репрограммирование)
                c.Methylation[i] *= 0.4; // 60% стирается
                c.Methylation[i] += R.NextDouble() * 0.05; // Стохастический шум

                // Экспрессия начинается заново но с наследственным влиянием
                c.GeneExpression[i] = 0.9 + R.NextDouble() * 0.1;
                c.HistoneState[i] = 0.8 + R.NextDouble() * 0.2;
            }

            // Импринтинг
            c.MaternalImprint = mother.Sociability * 0.5 + mother.Carefulness * 0.3;
            c.PaternalImprint = father.Intelligence * 0.4 + father.Leadership * 0.3;

            // Стрессовая эпигенетика частично наследуется (Голландская голодная зима!)
            c.StressEpigenetic = mother.StressEpigenetic * 0.3;
            c.FamineEpigenetic = mother.FamineEpigenetic * 0.4; // Голод матери → ожирение ребёнка
            c.TraumaEpigenetic = (mother.TraumaEpigenetic + father.TraumaEpigenetic) * 0.15;

            // Адаптации частично наследуются
            c.ColdAdaptation = (mother.ColdAdaptation + father.ColdAdaptation) * 0.2;
            c.HeatAdaptation = (mother.HeatAdaptation + father.HeatAdaptation) * 0.2;
            c.AltitudeAdaptation = (mother.AltitudeAdaptation + father.AltitudeAdaptation) * 0.15;
            c.DiseaseResistance = (mother.DiseaseResistance + father.DiseaseResistance) * 0.1;

            // Новорождённый
            c.TeloLength = 1.0; // Полные теломеры
            c.DNARepairEfficiency = 1.0;
            c.EpigeneticAge = 0;
            c.NutritionalStatus = mother.NutritionalStatus * 0.8; // Питание матери влияет

            return c;
        }

        // ====== МУТАЦИЯ ======

        public void Mutate(double rate = 0.2, double str = 0.15)
        {
            // Мутации ДНК (необратимые)
            if (R.NextDouble() < rate) BodySize = Cl(BodySize + Rn(str), 0.4, 2.2);
            if (R.NextDouble() < rate) MaxSpeed = Cl(MaxSpeed + Rn(str * 25), 50, 220);
            if (R.NextDouble() < rate) Metabolism = Cl(Metabolism + Rn(str), 0.3, 2);
            if (R.NextDouble() < rate) VisionRange = Cl(VisionRange + Rn(str * 40), 80, 420);
            if (R.NextDouble() < rate) ThermoTolerance = Cl(ThermoTolerance + Rn(str), 0, 1);
            if (R.NextDouble() < rate) Stamina = Cl(Stamina + Rn(str), 0.3, 2);
            if (R.NextDouble() < rate * 0.5) SkinR = Cl(SkinR + Rn(str * 0.15), 0, 1);
            if (R.NextDouble() < rate * 0.5) SkinG = Cl(SkinG + Rn(str * 0.15), 0, 1);
            if (R.NextDouble() < rate * 0.5) SkinB = Cl(SkinB + Rn(str * 0.15), 0, 1);
            if (R.NextDouble() < rate) Sociability = Cl(Sociability + Rn(str), 0, 1);
            if (R.NextDouble() < rate) Aggression = Cl(Aggression + Rn(str), 0, 1);
            if (R.NextDouble() < rate) Carefulness = Cl(Carefulness + Rn(str), 0, 1);
            if (R.NextDouble() < rate) Intelligence = Cl(Intelligence + Rn(str), 0, 1);
            if (R.NextDouble() < rate) Leadership = Cl(Leadership + Rn(str), 0, 1);
            if (R.NextDouble() < rate) HuntingSkill = Cl(HuntingSkill + Rn(str), 0, 1);
            if (R.NextDouble() < rate) BuildingSkill = Cl(BuildingSkill + Rn(str), 0, 1);
            if (R.NextDouble() < rate) Creativity = Cl(Creativity + Rn(str), 0, 1);
            if (R.NextDouble() < rate) Resilience = Cl(Resilience + Rn(str), 0, 1);
            if (R.NextDouble() < rate) LifeSpan = Cl(LifeSpan + Rn(str), 0.3, 1);
            if (R.NextDouble() < rate) FertilityBase = Cl(FertilityBase + Rn(str), 0.1, 1);
            if (R.NextDouble() < rate) ImmuneStrength = Cl(ImmuneStrength + Rn(str), 0, 1);
            if (R.NextDouble() < rate) PainTolerance = Cl(PainTolerance + Rn(str), 0, 1);
            if (R.NextDouble() < rate) AdaptationRate = Cl(AdaptationRate + Rn(str), 0, 1);
            if (R.NextDouble() < rate) MuscleDensity = Cl(MuscleDensity + Rn(str), 0, 1);
            if (R.NextDouble() < rate) BoneStrength = Cl(BoneStrength + Rn(str), 0, 1);
            if (R.NextDouble() < rate) LungCapacity = Cl(LungCapacity + Rn(str), 0, 1);
            if (R.NextDouble() < rate) NeuralPlasticity = Cl(NeuralPlasticity + Rn(str), 0, 1);

            for (int i = 0; i < 4; i++)
                if (R.NextDouble() < rate * 0.2)
                    Species[i] = Cl(Species[i] + Rn(str * 0.1), 0, 1);

            // Редкая хромосомная аберрация
            if (R.NextDouble() < rate * 0.01)
            {
                // Дупликация гена — случайное удвоение экспрессии одного гена
                int gene = R.Next(GENE_COUNT);
                GeneExpression[gene] *= 1.5;
            }
        }

        public bool SameSpecies(Genome o, double th = 0.3)
        {
            double d = 0;
            for (int i = 0; i < 4; i++)
                d += Math.Abs(Species[i] - o.Species[i]);
            return d / 4.0 < th;
        }

        // ====== УТИЛИТЫ ======
        static double Pick(double a, double b) => R.NextDouble() < 0.5 ? a : b;
        static double Mix(double a, double b) => (a + b) / 2 + (R.NextDouble() - 0.5) * 0.03;
        static double Rn(double s) => (R.NextDouble() - 0.5) * 2 * s;
        static double Cl(double v, double a, double b) => Math.Clamp(v, a, b);
        static int Clamp(int v, int a, int b) => Math.Clamp(v, a, b);
    }

    /// <summary>
    /// Контекст среды для эпигенетических изменений
    /// </summary>
    public class EpigeneticContext
    {
        public double Stress;            // 0-1
        public double Hunger;            // 0-1
        public double Trauma;            // 0-1
        public double SocialSupport;     // 0-1
        public double Temperature;       // 0-1 (0 = холод, 1 = жара)
        public double NutritionQuality;  // 0-1
        public double LearningIntensity; // 0-1
        public double PhysicalActivity;  // 0-1
        public double Altitude;          // 0-1
        public double DiseaseExposure;   // 0-1
        public double Toxins;            // 0-1
    }
}