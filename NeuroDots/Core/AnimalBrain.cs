using System;
using System.Runtime.CompilerServices;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Нейросеть животного: 16 входов → 12 скрытых → 6 выходов.
    /// Zero-allocation Forward pass и плоское хранение весов.
    /// </summary>
    public class AnimalBrain
    {
        public const int INPUTS = 16;
        public const int HIDDEN = 12;
        public const int OUTPUTS = 6;

        public readonly double[] W1 = new double[INPUTS * HIDDEN];
        public readonly double[] B1 = new double[HIDDEN];
        public readonly double[] W2 = new double[HIDDEN * OUTPUTS];
        public readonly double[] B2 = new double[OUTPUTS];

        private readonly double[] _h = new double[HIDDEN];
        public readonly double[] LastOutput = new double[OUTPUTS];

        public double Fear;
        public double Hunger;
        public double Satisfaction;
        public double AngerLevel;

        private static Random R => Random.Shared;

        public AnimalBrain()
        {
            Randomize();
        }

        public void Randomize()
        {
            double s1 = Math.Sqrt(2.0 / INPUTS);
            for (int i = 0; i < W1.Length; i++)
                W1[i] = (R.NextDouble() - 0.5) * 2.0 * s1;
            for (int j = 0; j < HIDDEN; j++)
                B1[j] = (R.NextDouble() - 0.5) * 0.1;

            double s2 = Math.Sqrt(2.0 / HIDDEN);
            for (int i = 0; i < W2.Length; i++)
                W2[i] = (R.NextDouble() - 0.5) * 2.0 * s2;
            for (int j = 0; j < OUTPUTS; j++)
                B2[j] = (R.NextDouble() - 0.5) * 0.1;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public double[] Think(ReadOnlySpan<double> sensors)
        {
            int len = Math.Min(INPUTS, sensors.Length);
            for (int j = 0; j < HIDDEN; j++)
            {
                double sum = B1[j];
                int offset = j;
                for (int i = 0; i < len; i++, offset += HIDDEN)
                    sum += sensors[i] * W1[offset];
                _h[j] = FastTanh(sum);
            }

            for (int j = 0; j < OUTPUTS; j++)
            {
                double sum = B2[j];
                int offset = j;
                for (int i = 0; i < HIDDEN; i++, offset += OUTPUTS)
                    sum += _h[i] * W2[offset];
                LastOutput[j] = FastTanh(sum);
            }

            Fear *= 0.96;
            Satisfaction *= 0.975;
            AngerLevel *= 0.965;
            return LastOutput;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double FastTanh(double x)
        {
            if (x > 4.5) return 1.0;
            if (x < -4.5) return -1.0;
            double x2 = x * x;
            return x * (27.0 + x2) / (27.0 + 9.0 * x2);
        }

        public void Satisfy(double amount)
        {
            Satisfaction = Math.Min(1.0, Satisfaction + amount);
            Fear = Math.Max(0.0, Fear - amount * 0.25);
        }

        public void Scare(double amount) =>
            Fear = Math.Min(1.0, Fear + amount);

        public void Anger(double amount) =>
            AngerLevel = Math.Min(1.0, AngerLevel + amount);

        public static AnimalBrain Crossover(AnimalBrain a, AnimalBrain b, Random rng)
        {
            rng ??= R;
            var c = new AnimalBrain();
            for (int i = 0; i < c.W1.Length; i++)
                c.W1[i] = rng.NextDouble() < 0.5 ? a.W1[i] : b.W1[i];
            for (int j = 0; j < HIDDEN; j++)
                c.B1[j] = rng.NextDouble() < 0.5 ? a.B1[j] : b.B1[j];
            for (int i = 0; i < c.W2.Length; i++)
                c.W2[i] = rng.NextDouble() < 0.5 ? a.W2[i] : b.W2[i];
            for (int j = 0; j < OUTPUTS; j++)
                c.B2[j] = rng.NextDouble() < 0.5 ? a.B2[j] : b.B2[j];
            return c;
        }

        public void Mutate(double rate, double strength)
        {
            for (int i = 0; i < W1.Length; i++)
                if (R.NextDouble() < rate)
                    W1[i] = Math.Clamp(W1[i] + (R.NextDouble() - 0.5) * 2.0 * strength, -4.0, 4.0);
            for (int j = 0; j < HIDDEN; j++)
                if (R.NextDouble() < rate)
                    B1[j] = Math.Clamp(B1[j] + (R.NextDouble() - 0.5) * strength, -1.5, 1.5);
            for (int i = 0; i < W2.Length; i++)
                if (R.NextDouble() < rate)
                    W2[i] = Math.Clamp(W2[i] + (R.NextDouble() - 0.5) * 2.0 * strength, -4.0, 4.0);
            for (int j = 0; j < OUTPUTS; j++)
                if (R.NextDouble() < rate)
                    B2[j] = Math.Clamp(B2[j] + (R.NextDouble() - 0.5) * strength, -1.5, 1.5);
        }
    }
}
