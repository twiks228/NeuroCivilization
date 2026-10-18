using System;
using System.Drawing;

namespace NeuroCivilization.Core
{
    public class AnimalGenome
    {
        public double Size;
        public double Speed;
        public double VisionRange;
        public double Aggression;
        public double AttackPower;
        public double Fear;
        public double EnergyDrain;
        public double MaxEnergy;
        public double DefensePower;
        public double ThermoTolerance;
        public double NocturnalActivity;
        public double ColorR, ColorG, ColorB;

        private static Random R => Random.Shared;

        public AnimalGenome()
        {
            Size = 0.5 + R.NextDouble();
            Speed = 60 + R.NextDouble() * 100;
            VisionRange = 100 + R.NextDouble() * 200;
            Aggression = R.NextDouble();
            AttackPower = R.NextDouble();
            Fear = R.NextDouble();
            EnergyDrain = 0.5 + R.NextDouble() * 0.5;
            MaxEnergy = 80 + R.NextDouble() * 40;
            DefensePower = R.NextDouble() * 0.5;
            ThermoTolerance = R.NextDouble();
            NocturnalActivity = R.NextDouble();
            ColorR = R.NextDouble();
            ColorG = R.NextDouble();
            ColorB = R.NextDouble();
        }

        public double DamageOutput => AttackPower * Size * 15 + Aggression * 5;

        public Color GetColor() => Color.FromArgb(
            Math.Clamp((int)(ColorR * 200 + 55), 55, 255),
            Math.Clamp((int)(ColorG * 200 + 55), 55, 255),
            Math.Clamp((int)(ColorB * 200 + 55), 55, 255));

        public static AnimalGenome Cross(AnimalGenome a, AnimalGenome b)
        {
            return new AnimalGenome
            {
                Size = R.NextDouble() < 0.5 ? a.Size : b.Size,
                Speed = (a.Speed + b.Speed) * 0.5 + (R.NextDouble() - 0.5) * 5,
                VisionRange = (a.VisionRange + b.VisionRange) * 0.5,
                Aggression = R.NextDouble() < 0.5 ? a.Aggression : b.Aggression,
                AttackPower = (a.AttackPower + b.AttackPower) * 0.5,
                Fear = (a.Fear + b.Fear) * 0.5,
                EnergyDrain = R.NextDouble() < 0.5 ? a.EnergyDrain : b.EnergyDrain,
                MaxEnergy = (a.MaxEnergy + b.MaxEnergy) * 0.5,
                DefensePower = (a.DefensePower + b.DefensePower) * 0.5,
                ThermoTolerance = (a.ThermoTolerance + b.ThermoTolerance) * 0.5,
                NocturnalActivity = R.NextDouble() < 0.5 ? a.NocturnalActivity : b.NocturnalActivity,
                ColorR = (a.ColorR + b.ColorR) * 0.5,
                ColorG = (a.ColorG + b.ColorG) * 0.5,
                ColorB = (a.ColorB + b.ColorB) * 0.5
            };
        }

        public void Mutate(double rate, double strength)
        {
            if (R.NextDouble() < rate) Size = Math.Clamp(Size + Rn(strength), 0.25, 2.5);
            if (R.NextDouble() < rate) Speed = Math.Clamp(Speed + Rn(strength * 20), 30, 200);
            if (R.NextDouble() < rate) VisionRange = Math.Clamp(VisionRange + Rn(strength * 30), 60, 400);
            if (R.NextDouble() < rate) Aggression = Math.Clamp(Aggression + Rn(strength), 0, 1);
            if (R.NextDouble() < rate) AttackPower = Math.Clamp(AttackPower + Rn(strength), 0, 1);
            if (R.NextDouble() < rate) Fear = Math.Clamp(Fear + Rn(strength), 0, 1);
            if (R.NextDouble() < rate) EnergyDrain = Math.Clamp(EnergyDrain + Rn(strength), 0.2, 1.5);
            if (R.NextDouble() < rate) MaxEnergy = Math.Clamp(MaxEnergy + Rn(strength * 10), 40, 150);
            if (R.NextDouble() < rate) DefensePower = Math.Clamp(DefensePower + Rn(strength), 0, 1);
            if (R.NextDouble() < rate) ThermoTolerance = Math.Clamp(ThermoTolerance + Rn(strength), 0, 1);
            if (R.NextDouble() < rate) ColorR = Math.Clamp(ColorR + Rn(strength * 0.2), 0, 1);
            if (R.NextDouble() < rate) ColorG = Math.Clamp(ColorG + Rn(strength * 0.2), 0, 1);
            if (R.NextDouble() < rate) ColorB = Math.Clamp(ColorB + Rn(strength * 0.2), 0, 1);
        }

        private static double Rn(double s) => (R.NextDouble() - 0.5) * 2.0 * s;
    }
}
