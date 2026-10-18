using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NeuroCivilization.Core
{
    public enum EpisodeType { FoundFood, FoundWater, Danger, Social, Mating, Discovery, Pain, Death, Building, Crafting }

    /// <summary>
    /// Гибридный нейро-когнитивный мозг агента (24 -> 20 -> 16 -> 10).
    /// Zero-allocation Forward pass, подкрепление по правилу Хебба + Дофаминовый сигнал (RPE),
    /// нейромодуляция (Дофамин, Серотонин, Кортизол, Окситоцин, Ацетилхолин, Норадреналин),
    /// эпизодическая и пространственная память.
    /// </summary>
    public class FastBrain
    {
        public const int INPUTS = 24, H1 = 20, H2 = 16, OUTPUTS = 10;

        // Плоские массивы весов для максимальной кэш-локальности CPU
        public readonly double[] W1 = new double[INPUTS * H1];
        public readonly double[] B1 = new double[H1];
        public readonly double[] W2 = new double[H1 * H2];
        public readonly double[] B2 = new double[H2];
        public readonly double[] W3 = new double[H2 * OUTPUTS];
        public readonly double[] B3 = new double[OUTPUTS];

        // Преаллоцированные буферы активаций (0 аллокаций в секунду)
        private readonly double[] _lastSensors = new double[INPUTS];
        private readonly double[] _h1 = new double[H1];
        private readonly double[] _h2 = new double[H2];
        private readonly double[] _neuralOut = new double[OUTPUTS];
        private readonly double[] _instinctOut = new double[OUTPUTS];
        public readonly double[] LastOutput = new double[OUTPUTS];

        // Нейромодуляторы (0..1)
        public double Dopamine = 0.25;
        public double Serotonin = 0.5;
        public double Cortisol = 0.1;
        public double Acetylcholine = 0.3;
        public double Oxytocin = 0.2;
        public double Norepinephrine = 0.15;

        public bool IsSleeping;
        public double SleepDrive, CircadianPhase, TraumaLevel, Hunger;
        public double Experience;

        // Баланс между врождёнными инстинктами и обученной нейросетью
        public double InstinctWeight => Math.Max(0.15, 1.0 - Experience * 0.004);
        public double NeuralWeight => Math.Min(0.85, 0.15 + Experience * 0.004);

        // Состояние плавного блуждания
        public double WanderTimer;
        public double WanderTurnTarget;
        public bool WanderIsTurning;

        public string LastDecision = "";

        // Кольцевой буфер эпизодической памяти (без сдвига списка RemoveAt(0))
        public const int MAX_EPISODES = 32;
        public readonly Episode[] Episodes = new Episode[MAX_EPISODES];
        public int EpisodeCount;
        private int _episodeHead;

        public const int MAP_SIZE = 12;
        public readonly double[,] SpatialMap = new double[MAP_SIZE, MAP_SIZE];

        private static Random R => Random.Shared;

        public FastBrain()
        {
            Randomize();
            WanderTimer = 30 + R.NextDouble() * 50;
        }

        public void Randomize()
        {
            InitWeights(W1, INPUTS, H1);
            InitBias(B1);
            InitWeights(W2, H1, H2);
            InitBias(B2);
            InitWeights(W3, H2, OUTPUTS);
            InitBias(B3);
        }

        private static void InitWeights(double[] w, int rows, int cols)
        {
            double scale = Math.Sqrt(2.0 / rows);
            int len = rows * cols;
            for (int i = 0; i < len; i++)
                w[i] = (R.NextDouble() - 0.5) * 2.0 * scale;
        }

        private static void InitBias(double[] b)
        {
            for (int i = 0; i < b.Length; i++)
                b[i] = (R.NextDouble() - 0.5) * 0.1;
        }

        /// <summary>
        /// Выходы:
        /// [0] MoveSpeed (-1..1)
        /// [1] TurnDelta (-1..1)
        /// [2] Voice/Communicate
        /// [3] Eat/Forage
        /// [4] Attack/Hunt
        /// [5] Mate Desire
        /// [6] Rest/Heal
        /// [7] Build Desire
        /// [8] Craft Desire
        /// [9] Social/Teach Desire
        /// </summary>
        public double[] ThinkWithInstincts(
            ReadOnlySpan<double> sensors,
            double hunger, double health, double maxHealth,
            double fatigue, double pain,
            double wx, double wy, double ww, double wh, double dt)
        {
            Forward(sensors);
            ComputeInstincts(sensors, hunger, health, maxHealth, fatigue, pain, wx, wy, ww, wh, dt);

            double iw = InstinctWeight;
            double nw = NeuralWeight;

            // Нейромодуляция выходов
            double arousal = 1.0 + Norepinephrine * 0.25 - FatigueMod(fatigue);
            double caution = 1.0 + Cortisol * 0.35;
            double socialDrive = 1.0 + Oxytocin * 0.4;
            double curiosity = 1.0 + Dopamine * 0.3 + Acetylcholine * 0.2;

            for (int i = 0; i < OUTPUTS; i++)
            {
                double val = _instinctOut[i] * iw + _neuralOut[i] * nw;
                if (i == 0) val *= arousal;
                else if (i == 4) val *= (1.0 + Norepinephrine * 0.3 - Cortisol * 0.2);
                else if (i == 5 || i == 9) val *= socialDrive;
                else if (i == 7 || i == 8) val *= curiosity;
                else if (i == 6) val *= caution;

                LastOutput[i] = Math.Clamp(val, -1.0, 1.0);
            }

            // Гомеостаз нейромедиаторов
            Experience += 0.0004 * dt * (1.0 + Acetylcholine * 0.5);
            Dopamine = Math.Clamp(Dopamine + (0.20 - Dopamine) * 0.02 * dt, 0.0, 1.0);
            Serotonin = Math.Clamp(Serotonin + (0.50 - Serotonin) * 0.01 * dt - Cortisol * 0.003 * dt, 0.0, 1.0);
            Cortisol = Math.Clamp(Cortisol * Math.Pow(0.985, dt), 0.0, 1.0);
            Acetylcholine = Math.Clamp(Acetylcholine * Math.Pow(0.98, dt), 0.0, 1.0);
            Oxytocin = Math.Clamp(Oxytocin * Math.Pow(0.988, dt), 0.0, 1.0);
            Norepinephrine = Math.Clamp(Norepinephrine * Math.Pow(0.97, dt), 0.0, 1.0);

            return LastOutput;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double FatigueMod(double f) => f > 0.6 ? (f - 0.6) * 0.6 : 0.0;

        public double[] Think(ReadOnlySpan<double> sensors)
        {
            Forward(sensors);
            Array.Copy(_neuralOut, LastOutput, OUTPUTS);
            return LastOutput;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void Forward(ReadOnlySpan<double> sensors)
        {
            int len = Math.Min(INPUTS, sensors.Length);
            for (int i = 0; i < len; i++)
                _lastSensors[i] = sensors[i];

            // Layer 1: INPUTS -> H1
            for (int j = 0; j < H1; j++)
            {
                double sum = B1[j];
                int offset = j;
                for (int i = 0; i < len; i++, offset += H1)
                    sum += _lastSensors[i] * W1[offset];
                _h1[j] = FastTanh(sum);
            }

            // Layer 2: H1 -> H2
            for (int j = 0; j < H2; j++)
            {
                double sum = B2[j];
                int offset = j;
                for (int i = 0; i < H1; i++, offset += H2)
                    sum += _h1[i] * W2[offset];
                _h2[j] = FastTanh(sum);
            }

            // Layer 3: H2 -> OUTPUTS
            for (int j = 0; j < OUTPUTS; j++)
            {
                double sum = B3[j];
                int offset = j;
                for (int i = 0; i < H2; i++, offset += OUTPUTS)
                    sum += _h2[i] * W3[offset];
                _neuralOut[j] = FastTanh(sum);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double FastTanh(double x)
        {
            if (x > 4.5) return 1.0;
            if (x < -4.5) return -1.0;
            double x2 = x * x;
            return x * (27.0 + x2) / (27.0 + 9.0 * x2);
        }

        private void ComputeInstincts(
            ReadOnlySpan<double> s,
            double hunger, double hp, double maxHp,
            double fatigue, double pain,
            double wx, double wy, double ww, double wh, double dt)
        {
            Array.Clear(_instinctOut, 0, OUTPUTS);
            double hpRatio = hp / Math.Max(1.0, maxHp);

            // П1: Границы мира — мягкое отталкивание
            const double border = 95.0;
            double pushX = 0, pushY = 0;
            if (wx < border) pushX = (border - wx) / border;
            else if (wx > ww - border) pushX = -(wx - (ww - border)) / border;
            if (wy < border) pushY = (border - wy) / border;
            else if (wy > wh - border) pushY = -(wy - (wh - border)) / border;

            if (pushX * pushX + pushY * pushY > 0.04)
            {
                LastDecision = "WALL";
                _instinctOut[0] = 0.55;
                _instinctOut[1] = Math.Clamp(pushX * 0.5 + pushY * 0.35, -0.65, 0.65);
                return;
            }

            // П2: Хищник рядом (s[10] > 0.5 = хищник, s[9] = норм. дистанция, s[8] = относит. угол)
            if (s[10] > 0.5 && s[9] < 0.45)
            {
                double danger = (0.45 - s[9]) / 0.45;
                LastDecision = $"FLEE d={danger:F2}";
                _instinctOut[0] = 0.75 + danger * 0.25;
                _instinctOut[1] = Math.Abs(s[8]) < 0.2 ? (s[8] >= 0 ? -0.75 : 0.75) : -s[8] * 0.7;
                _instinctOut[2] = danger > 0.3 ? 0.8 : 0.2;
                return;
            }

            // П3: Голод — поиск еды или охота
            if (hunger > 0.22)
            {
                double urg = Math.Clamp((hunger - 0.22) / 0.78, 0.0, 1.0);
                if (s[5] < 0.85) // Растительная еда видна
                {
                    double relFoodAngle = s[4];
                    if (s[5] < 0.12)
                    {
                        LastDecision = "EAT";
                        _instinctOut[3] = 0.9;
                        _instinctOut[0] = 0.05;
                        _instinctOut[1] = relFoodAngle * 0.15;
                        return;
                    }
                    LastDecision = "SEEK_FOOD";
                    _instinctOut[0] = 0.35 + urg * 0.45;
                    _instinctOut[1] = relFoodAngle * 0.55;
                    _instinctOut[3] = 0.5;
                    return;
                }
                else if (s[10] < 0.5 && s[9] < 0.75 && hpRatio > 0.45) // Видна травоядная добыча
                {
                    LastDecision = "HUNT";
                    _instinctOut[0] = 0.5 + urg * 0.4;
                    _instinctOut[1] = s[8] * 0.6;
                    _instinctOut[4] = 0.8;
                    return;
                }
                else
                {
                    LastDecision = "FORAGE_WANDER";
                    _instinctOut[0] = 0.3 + urg * 0.25;
                    _instinctOut[1] = GetWanderTurn(dt);
                    return;
                }
            }

            // П4: Низкое здоровье или сильная усталость
            if (hpRatio < 0.35 || fatigue > 0.72)
            {
                LastDecision = "REST";
                _instinctOut[6] = 0.85;
                _instinctOut[0] = 0.02;
                return;
            }

            // П5: Сытый и бодрый — социализация, размножение, созидание
            if (hunger < 0.3 && hpRatio > 0.55 && fatigue < 0.55)
            {
                if (s[7] < 0.35)
                {
                    LastDecision = "SOCIAL";
                    _instinctOut[9] = 0.65;
                    _instinctOut[2] = 0.5;
                    _instinctOut[0] = 0.05;
                    if (hunger < 0.22 && hpRatio > 0.7)
                        _instinctOut[5] = 0.7;
                    return;
                }
                if (s[7] < 0.75)
                {
                    LastDecision = "APPROACH";
                    _instinctOut[0] = 0.25;
                    _instinctOut[1] = s[6] * 0.45;
                    _instinctOut[9] = 0.4;
                    return;
                }
                _instinctOut[7] = 0.35; // Build
                _instinctOut[8] = 0.35; // Craft
            }

            LastDecision = "WANDER";
            _instinctOut[0] = 0.25;
            _instinctOut[1] = GetWanderTurn(dt);
            if (s[5] < 0.1) _instinctOut[3] = 0.5;
        }

        private double GetWanderTurn(double dt)
        {
            WanderTimer -= dt;
            if (WanderIsTurning)
            {
                double turnStep = Math.Sign(WanderTurnTarget) * 0.035;
                WanderTurnTarget -= turnStep;
                if (Math.Abs(WanderTurnTarget) < 0.05)
                {
                    WanderIsTurning = false;
                    WanderTimer = 40 + R.NextDouble() * 80;
                }
                return turnStep;
            }

            if (WanderTimer <= 0)
            {
                WanderIsTurning = true;
                WanderTurnTarget = (R.NextDouble() - 0.5) * Math.PI;
            }
            return 0;
        }

        /// <summary>
        /// Онлайн-обучение с подкреплением (Reward-Modulated Hebbian Plasticity).
        /// Усиливает синаптические веса текущей активной конфигурации при положительном сигнале
        /// и ослабляет при наказании/боли.
        /// </summary>
        public void Reinforce(double rewardSignal, double plasticity = 1.0)
        {
            double lr = Math.Clamp(rewardSignal * 0.008 * plasticity * (0.5 + Acetylcholine), -0.02, 0.02);
            if (Math.Abs(lr) < 1e-6) return;

            for (int i = 0; i < H2; i++)
            {
                double h2v = _h2[i];
                int row = i * OUTPUTS;
                for (int j = 0; j < OUTPUTS; j++)
                {
                    W3[row + j] = Math.Clamp(W3[row + j] + lr * h2v * LastOutput[j], -3.5, 3.5);
                }
            }
            for (int j = 0; j < OUTPUTS; j++)
                B3[j] = Math.Clamp(B3[j] + lr * 0.3 * LastOutput[j], -1.5, 1.5);
        }

        public void UpdateCircadian(double dayLight)
        {
            CircadianPhase = dayLight;
            if (dayLight < 0.18) SleepDrive = Math.Min(1.0, SleepDrive + 0.0012);
            else SleepDrive = Math.Max(0.0, SleepDrive - 0.0025);

            if (!IsSleeping && SleepDrive > 0.85 && dayLight < 0.22) IsSleeping = true;
            if (IsSleeping && (SleepDrive < 0.08 || dayLight > 0.45)) IsSleeping = false;

            if (IsSleeping)
            {
                Cortisol *= 0.96;
                SleepDrive = Math.Max(0.0, SleepDrive - 0.005);
                TraumaLevel *= 0.998;
                Serotonin = Math.Min(1.0, Serotonin + 0.001);
            }
        }

        public void Reward(double a)
        {
            Dopamine = Math.Min(1.0, Dopamine + a);
            Serotonin = Math.Min(1.0, Serotonin + a * 0.3);
            Experience += a * 0.6;
            Reinforce(a);
        }

        public void Social(double a)
        {
            Oxytocin = Math.Min(1.0, Oxytocin + a);
            Cortisol = Math.Max(0.0, Cortisol - a * 0.15);
            Serotonin = Math.Min(1.0, Serotonin + a * 0.1);
        }

        public void Fear(double a)
        {
            Cortisol = Math.Min(1.0, Cortisol + a);
            Norepinephrine = Math.Min(1.0, Norepinephrine + a * 0.85);
        }

        public void Pain(double a)
        {
            Cortisol = Math.Min(1.0, Cortisol + a * 0.6);
            Reinforce(-a * 0.6);
        }

        public void Satisfy(double a) => Dopamine = Math.Min(1.0, Dopamine + a * 0.5);
        public void Scare(double a) => Fear(a);

        public void TraumaticEvent(string s)
        {
            TraumaLevel = Math.Min(1.0, TraumaLevel + 0.12);
            Fear(0.5);
        }

        public void RememberEpisode(double x, double y, EpisodeType t, double v)
        {
            Episodes[_episodeHead] = new Episode { X = x, Y = y, Type = t, Value = v };
            _episodeHead = (_episodeHead + 1) % MAX_EPISODES;
            if (EpisodeCount < MAX_EPISODES) EpisodeCount++;
            Acetylcholine = Math.Min(1.0, Acetylcholine + 0.1);
        }

        public bool TryRecallEpisode(EpisodeType type, out Episode best)
        {
            best = default;
            double bestVal = -1;
            for (int i = 0; i < EpisodeCount; i++)
            {
                var ep = Episodes[i];
                if (ep.Type == type && ep.Value > bestVal)
                {
                    bestVal = ep.Value;
                    best = ep;
                }
            }
            return bestVal >= 0;
        }

        public void UpdateSpatialMap(double x, double y, double w, double h)
        {
            int gx = Math.Clamp((int)(x / w * MAP_SIZE), 0, MAP_SIZE - 1);
            int gy = Math.Clamp((int)(y / h * MAP_SIZE), 0, MAP_SIZE - 1);
            SpatialMap[gx, gy] = Math.Min(1.0, SpatialMap[gx, gy] + 0.012);
        }

        public void LearnFromObservation(double[] other, double oF, double mF)
        {
            if (other == null || oF <= mF) return;
            int len = Math.Min(OUTPUTS, other.Length);
            for (int j = 0; j < len; j++)
                B3[j] = Math.Clamp(B3[j] + (other[j] - B3[j]) * 0.005, -1.5, 1.5);
            Experience += 0.025;
        }

        public void TransferKnowledge(FastBrain src, double a)
        {
            BlendArray(W1, src.W1, a);
            BlendArray(B1, src.B1, a);
            BlendArray(W2, src.W2, a);
            BlendArray(B2, src.B2, a);
            BlendArray(W3, src.W3, a);
            BlendArray(B3, src.B3, a);
        }

        private static void BlendArray(double[] dst, double[] src, double a)
        {
            double inv = 1.0 - a;
            for (int i = 0; i < dst.Length; i++)
                dst[i] = dst[i] * inv + src[i] * a;
        }

        public void Mutate(double rate, double str)
        {
            MutateArray(W1, rate, str);
            MutateArray(B1, rate, str * 0.5);
            MutateArray(W2, rate, str);
            MutateArray(B2, rate, str * 0.5);
            MutateArray(W3, rate, str);
            MutateArray(B3, rate, str * 0.5);
        }

        private static void MutateArray(double[] arr, double rt, double s)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (R.NextDouble() < rt)
                    arr[i] = Math.Clamp(arr[i] + (R.NextDouble() - 0.5) * 2.0 * s, -4.0, 4.0);
            }
        }
    }

    public struct Episode
    {
        public double X, Y;
        public EpisodeType Type;
        public double Value;
    }
}
