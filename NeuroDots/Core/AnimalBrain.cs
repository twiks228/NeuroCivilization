using System;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Нейросеть животного: 16 входов → 12 скрытых → 6 выходов
    /// </summary>
    public class AnimalBrain
    {
        double[,] W1, W2;
        double[] B1, B2;

        const int INPUTS = 16;
        const int HIDDEN = 12;
        const int OUTPUTS = 6;

        public double Fear;
        public double Hunger;
        public double Satisfaction;
        public double AngerLevel;

        static Random R = new();

        public AnimalBrain()
        {
            W1 = new double[INPUTS, HIDDEN];
            B1 = new double[HIDDEN];
            W2 = new double[HIDDEN, OUTPUTS];
            B2 = new double[OUTPUTS];
            Randomize();
        }

        void Randomize()
        {
            double s1 = Math.Sqrt(2.0 / INPUTS);
            for (int i = 0; i < INPUTS; i++)
                for (int j = 0; j < HIDDEN; j++)
                    W1[i, j] = (R.NextDouble() - 0.5) * 2 * s1;
            for (int j = 0; j < HIDDEN; j++)
                B1[j] = (R.NextDouble() - 0.5) * 0.1;

            double s2 = Math.Sqrt(2.0 / HIDDEN);
            for (int i = 0; i < HIDDEN; i++)
                for (int j = 0; j < OUTPUTS; j++)
                    W2[i, j] = (R.NextDouble() - 0.5) * 2 * s2;
            for (int j = 0; j < OUTPUTS; j++)
                B2[j] = (R.NextDouble() - 0.5) * 0.1;
        }

        public double[] Think(double[] sensors)
        {
            double[] h = new double[HIDDEN];
            for (int j = 0; j < HIDDEN; j++)
            {
                double sum = B1[j];
                for (int i = 0; i < INPUTS; i++)
                    sum += sensors[i] * W1[i, j];
                h[j] = Math.Tanh(sum);
            }

            double[] o = new double[OUTPUTS];
            for (int j = 0; j < OUTPUTS; j++)
            {
                double sum = B2[j];
                for (int i = 0; i < HIDDEN; i++)
                    sum += h[i] * W2[i, j];
                o[j] = Math.Tanh(sum);
            }

            Fear *= 0.95;
            Satisfaction *= 0.97;
            AngerLevel *= 0.96;
            return o;
        }

        public void Satisfy(double amount)
        {
            Satisfaction = Math.Min(1, Satisfaction + amount);
            Fear = Math.Max(0, Fear - amount * 0.2);
        }

        public void Scare(double amount) =>
            Fear = Math.Min(1, Fear + amount);

        public void Anger(double amount) =>
            AngerLevel = Math.Min(1, AngerLevel + amount);

        public static AnimalBrain Crossover(AnimalBrain a, AnimalBrain b, Random rng)
        {
            var c = new AnimalBrain();
            for (int i = 0; i < INPUTS; i++)
                for (int j = 0; j < HIDDEN; j++)
                    c.W1[i, j] = rng.NextDouble() < 0.5 ? a.W1[i, j] : b.W1[i, j];
            for (int j = 0; j < HIDDEN; j++)
                c.B1[j] = rng.NextDouble() < 0.5 ? a.B1[j] : b.B1[j];
            for (int i = 0; i < HIDDEN; i++)
                for (int j = 0; j < OUTPUTS; j++)
                    c.W2[i, j] = rng.NextDouble() < 0.5 ? a.W2[i, j] : b.W2[i, j];
            for (int j = 0; j < OUTPUTS; j++)
                c.B2[j] = rng.NextDouble() < 0.5 ? a.B2[j] : b.B2[j];
            return c;
        }

        public void Mutate(double rate, double strength)
        {
            for (int i = 0; i < INPUTS; i++)
                for (int j = 0; j < HIDDEN; j++)
                    if (R.NextDouble() < rate)
                        W1[i, j] += (R.NextDouble() - 0.5) * 2 * strength;
            for (int j = 0; j < HIDDEN; j++)
                if (R.NextDouble() < rate)
                    B1[j] += (R.NextDouble() - 0.5) * 2 * strength;
            for (int i = 0; i < HIDDEN; i++)
                for (int j = 0; j < OUTPUTS; j++)
                    if (R.NextDouble() < rate)
                        W2[i, j] += (R.NextDouble() - 0.5) * 2 * strength;
            for (int j = 0; j < OUTPUTS; j++)
                if (R.NextDouble() < rate)
                    B2[j] += (R.NextDouble() - 0.5) * 2 * strength;
        }
    }
}