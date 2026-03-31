using System;
using System.Collections.Generic;

namespace NeuroCivilization.Core
{
    public enum EpisodeType { FoundFood, FoundWater, Danger, Social, Mating, Discovery, Pain, Death, Building, Crafting }

    public class FastBrain
    {
        double[,] W1, W2, W3;
        double[] B1, B2, B3;
        const int INPUTS = 24, H1 = 20, H2 = 16, OUTPUTS = 10;

        public double Dopamine, Serotonin = 0.5, Cortisol;
        public double Acetylcholine, Oxytocin, Norepinephrine;
        public bool IsSleeping;
        public double SleepDrive, CircadianPhase, TraumaLevel, Hunger;
        public double Experience;
        public double InstinctWeight => Math.Max(0.15, 1.0 - Experience * 0.003);
        public double NeuralWeight => Math.Min(0.85, Experience * 0.003);

        // FIX: Состояние блуждания — прямо, потом поворот, потом прямо
        public double WanderTimer;
        public double WanderTurnTarget; // Сколько ещё нужно повернуть
        public bool WanderIsTurning;

        public string LastDecision = ""; // Для отладки

        public List<Episode> Episodes = new();
        public double[,] SpatialMap;
        const int MAP_SIZE = 10;
        static Random R = new();

        public FastBrain()
        {
            W1 = new double[INPUTS, H1]; B1 = new double[H1];
            W2 = new double[H1, H2]; B2 = new double[H2];
            W3 = new double[H2, OUTPUTS]; B3 = new double[OUTPUTS];
            SpatialMap = new double[MAP_SIZE, MAP_SIZE];
            Randomize();
            WanderTimer = 30 + R.NextDouble() * 50;
        }

        void Randomize()
        {
            Init(W1, INPUTS, H1); IA(B1, H1);
            Init(W2, H1, H2); IA(B2, H2);
            Init(W3, H2, OUTPUTS); IA(B3, OUTPUTS);
        }
        void Init(double[,] m, int r, int c) { double s = Math.Sqrt(2.0 / r); for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) m[i, j] = (R.NextDouble() - 0.5) * 2 * s; }
        void IA(double[] a, int n) { for (int i = 0; i < n; i++) a[i] = (R.NextDouble() - 0.5) * 0.1; }

        public double[] ThinkWithInstincts(double[] sensors,
            double hunger, double health, double maxHealth,
            double fatigue, double pain,
            double wx, double wy, double ww, double wh, double dt)
        {
            double[] neural = Forward(sensors);
            double[] instinct = Instincts(sensors, hunger, health, maxHealth,
                fatigue, pain, wx, wy, ww, wh, dt);

            double iw = InstinctWeight, nw = NeuralWeight;
            double[] output = new double[OUTPUTS];
            for (int i = 0; i < OUTPUTS; i++)
                output[i] = Math.Clamp(instinct[i] * iw + neural[i] * nw, -1, 1);

            Experience += 0.0003;
            Dopamine *= 0.96; Serotonin += (0.5 - Serotonin) * 0.005;
            Cortisol *= 0.98; Acetylcholine *= 0.97;
            Oxytocin *= 0.98; Norepinephrine *= 0.96;
            return output;
        }

        public double[] Think(double[] s) => Forward(s);

        double[] Forward(double[] sensors)
        {
            double[] h1 = new double[H1];
            for (int j = 0; j < H1; j++)
            { double sum = B1[j]; for (int i = 0; i < Math.Min(INPUTS, sensors.Length); i++) sum += sensors[i] * W1[i, j]; h1[j] = Math.Tanh(sum); }
            double[] h2 = new double[H2];
            for (int j = 0; j < H2; j++)
            { double sum = B2[j]; for (int i = 0; i < H1; i++) sum += h1[i] * W2[i, j]; h2[j] = Math.Tanh(sum); }
            double[] o = new double[OUTPUTS];
            for (int j = 0; j < OUTPUTS; j++)
            { double sum = B3[j]; for (int i = 0; i < H2; i++) sum += h2[i] * W3[i, j]; o[j] = Math.Tanh(sum); }
            return o;
        }

        /// <summary>
        /// Выходы: [0]move [1]turn [2]voice [3]eat [4]attack [5]mate [6]rest [7]build [8]craft [9]teach
        /// Все углы в сенсорах ОТНОСИТЕЛЬНЫЕ!
        /// </summary>
        double[] Instincts(double[] s, double hunger, double hp, double maxHp,
            double fatigue, double pain,
            double wx, double wy, double ww, double wh, double dt)
        {
            double[] o = new double[OUTPUTS];
            double hpRatio = hp / Math.Max(1, maxHp);

            // ===== П1: СТЕНЫ — мягкое отталкивание =====
            double border = 100;
            double pushX = 0, pushY = 0;
            if (wx < border) pushX = (border - wx) / border;
            if (wx > ww - border) pushX = -(wx - (ww - border)) / border;
            if (wy < border) pushY = (border - wy) / border;
            if (wy > wh - border) pushY = -(wy - (wh - border)) / border;
            double pushForce = Math.Sqrt(pushX * pushX + pushY * pushY);

            if (pushForce > 0.2)
            {
                LastDecision = "WALL";
                o[0] = 0.5;
                // Направление отталкивания — преобразовать в поворот
                // pushX, pushY — куда нужно двигаться
                o[1] = Math.Clamp(pushX * 0.5 + pushY * 0.3, -0.6, 0.6);
                return o;
            }

            // ===== П2: ХИЩНИК РЯДОМ =====
            if (s[10] > 0.5 && s[9] < 0.35)
            {
                double danger = (0.35 - s[9]) / 0.35;
                LastDecision = $"FLEE d={danger:F2}";
                o[0] = 0.7 + danger * 0.3; // БЕЖАТЬ
                // s[8] — ОТНОСИТЕЛЬНЫЙ угол к хищнику. Бежим прочь.
                // Если хищник прямо впереди (s[8]≈0) — разворот
                // Если хищник сбоку — уклоняемся
                if (Math.Abs(s[8]) < 0.2) // Прямо впереди
                    o[1] = 0.6 * (s[8] > 0 ? -1 : 1); // Резкий разворот
                else
                    o[1] = -s[8] * 0.6; // Отвернуть от хищника
                o[2] = danger > 0.3 ? 0.7 : 0;
                return o;
            }

            // ===== П3: ГОЛОД — ИСКАТЬ ЕДУ =====
            if (hunger > 0.2)
            {
                double urg = Math.Clamp((hunger - 0.2) / 0.8, 0, 1);

                // s[5] = расстояние к еде (0=рядом, 1=далеко/не видно)
                if (s[5] < 0.8) // Еда ВИДНА
                {
                    // s[4] = ОТНОСИТЕЛЬНЫЙ угол к еде (-1..+1)
                    double relFoodAngle = s[4]; // уже нормализован

                    if (s[5] < 0.1) // РЯДОМ С ЕДОЙ — ЕСТЬ
                    {
                        LastDecision = $"EAT dist={s[5]:F2}";
                        o[3] = 0.8; // ЕСТЬ
                        o[0] = 0.02; // Остановиться
                        o[1] = relFoodAngle * 0.1; // Чуть довернуть
                        return o;
                    }
                    else // ИДТИ К ЕДЕ
                    {
                        LastDecision = $"FOOD a={relFoodAngle:F2} d={s[5]:F2}";
                        o[0] = 0.3 + urg * 0.4; // Идти / бежать к еде

                        // Пропорциональный поворот к еде
                        // relFoodAngle: -1 = еда слева, +1 = еда справа
                        o[1] = relFoodAngle * 0.5; // Поворот к еде
                        return o;
                    }
                }
                else // Еда НЕ ВИДНА — исследовать
                {
                    LastDecision = $"EXPLORE h={hunger:F2}";
                    o[0] = 0.25 + urg * 0.2;
                    // Блуждание — см. ниже
                    o[1] = GetWanderTurn(dt);
                    return o;
                }
            }

            // ===== П4: МАЛО HP =====
            if (hpRatio < 0.3)
            {
                LastDecision = "REST(hp)";
                o[6] = 0.6;
                o[0] = 0.01;
                return o;
            }

            // ===== П5: УСТАЛОСТЬ =====
            if (fatigue > 0.7)
            {
                LastDecision = "REST(tired)";
                o[6] = 0.5;
                o[0] = 0.01;
                return o;
            }

            // ===== П6: СЫТЫЙ + ЗДОРОВЫЙ — СОЦИАЛЬНОСТЬ =====
            if (hunger < 0.2 && hpRatio > 0.6 && fatigue < 0.5)
            {
                if (s[7] < 0.25) // Человек совсем рядом
                {
                    LastDecision = "SOCIAL";
                    o[9] = 0.3; // Общаться
                    o[0] = 0.03; // Стоять рядом
                    if (hunger < 0.1 && hpRatio > 0.8)
                        o[5] = 0.35; // Размножение
                    return o;
                }
                else if (s[7] < 0.6) // Человек виден — подойти
                {
                    LastDecision = "APPROACH";
                    o[0] = 0.2;
                    o[1] = s[6] * 0.4; // Повернуться к человеку
                    return o;
                }
            }

            // ===== БАЗОВОЕ: БЛУЖДАНИЕ =====
            LastDecision = "WANDER";
            o[0] = 0.2; // Идти вперёд
            o[1] = GetWanderTurn(dt); // Периодические повороты

            // Попутно пытаться есть если рядом
            if (s[5] < 0.08) o[3] = 0.5;

            return o;
        }

        /// <summary>
        /// FIX: Блуждание = идти ПРЯМО 30-80 тиков, потом ПОВЕРНУТЬ на случайный угол
        /// Никаких sin/cos — чистый прямолинейный ход с поворотами
        /// </summary>
        double GetWanderTurn(double dt)
        {
            WanderTimer -= dt;

            if (WanderIsTurning)
            {
                // Поворачиваемся к цели
                double turnStep = Math.Sign(WanderTurnTarget) * 0.03;
                WanderTurnTarget -= turnStep;

                // Закончили поворот?
                if (Math.Abs(WanderTurnTarget) < 0.05)
                {
                    WanderIsTurning = false;
                    WanderTimer = 40 + R.NextDouble() * 80; // Следующий прямой участок
                }
                return turnStep;
            }
            else
            {
                // Идём прямо
                if (WanderTimer <= 0)
                {
                    // Время повернуть!
                    WanderIsTurning = true;
                    // Случайный поворот от -90° до +90°
                    WanderTurnTarget = (R.NextDouble() - 0.5) * Math.PI;
                }
                return 0; // Прямо — нулевой поворот
            }
        }

        public void UpdateCircadian(double dayLight)
        {
            CircadianPhase = dayLight;
            if (dayLight < 0.12) SleepDrive = Math.Min(1, SleepDrive + 0.0008);
            else SleepDrive = Math.Max(0, SleepDrive - 0.002);
            if (!IsSleeping && SleepDrive > 0.9 && dayLight < 0.12) IsSleeping = true;
            if (IsSleeping && (SleepDrive < 0.08 || dayLight > 0.4)) IsSleeping = false;
            if (IsSleeping) { Cortisol *= 0.95; SleepDrive -= 0.004; TraumaLevel *= 0.999; }
        }

        public void Reward(double a) { Dopamine = Math.Min(1, Dopamine + a); Serotonin = Math.Min(1, Serotonin + a * 0.3); Experience += a * 0.5; }
        public void Social(double a) { Oxytocin = Math.Min(1, Oxytocin + a); Cortisol = Math.Max(0, Cortisol - a * 0.1); }
        public void Fear(double a) { Cortisol = Math.Min(1, Cortisol + a); Norepinephrine = Math.Min(1, Norepinephrine + a * 0.8); }
        public void Pain(double a) { Cortisol = Math.Min(1, Cortisol + a * 0.5); }
        public void Satisfy(double a) { Dopamine = Math.Min(1, Dopamine + a * 0.5); }
        public void Scare(double a) => Fear(a);
        public void TraumaticEvent(string s) { TraumaLevel = Math.Min(1, TraumaLevel + 0.1); Fear(0.5); }
        public void RememberEpisode(double x, double y, EpisodeType t, double v) { Episodes.Add(new Episode { X = x, Y = y, Type = t, Value = v }); if (Episodes.Count > 50) Episodes.RemoveAt(0); Acetylcholine = Math.Min(1, Acetylcholine + 0.1); }
        public void UpdateSpatialMap(double x, double y, double w, double h) { int gx = Math.Clamp((int)(x / w * MAP_SIZE), 0, MAP_SIZE - 1); int gy = Math.Clamp((int)(y / h * MAP_SIZE), 0, MAP_SIZE - 1); SpatialMap[gx, gy] = Math.Min(1, SpatialMap[gx, gy] + 0.01); }
        public void LearnFromObservation(double[] other, double oF, double mF) { if (other == null || oF <= mF) return; for (int j = 0; j < Math.Min(OUTPUTS, other.Length); j++) B3[j] += (other[j] - B3[j]) * 0.002; Experience += 0.02; }
        public void TransferKnowledge(FastBrain src, double a) { Bl(W1, src.W1, INPUTS, H1, a); Bl(W2, src.W2, H1, H2, a); Bl(W3, src.W3, H2, OUTPUTS, a); }
        void Bl(double[,] d, double[,] s, int r, int c, double a) { for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) d[i, j] = d[i, j] * (1 - a) + s[i, j] * a; }
        public void Mutate(double rate, double str) { Mt(W1, INPUTS, H1, rate, str); MA(B1, H1, rate, str); Mt(W2, H1, H2, rate, str); MA(B2, H2, rate, str); Mt(W3, H2, OUTPUTS, rate, str); MA(B3, OUTPUTS, rate, str); }
        void Mt(double[,] m, int r, int c, double rt, double s) { for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) if (R.NextDouble() < rt) m[i, j] += (R.NextDouble() - 0.5) * 2 * s; }
        void MA(double[] a, int n, double rt, double s) { for (int i = 0; i < n; i++) if (R.NextDouble() < rt) a[i] += (R.NextDouble() - 0.5) * 2 * s; }
    }

    public class Episode { public double X, Y; public EpisodeType Type; public double Value; }
}