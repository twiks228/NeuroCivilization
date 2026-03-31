using System;
using System.Collections.Generic;
using System.Drawing;

namespace NeuroCivilization.Core
{
    public enum AnimalGoal { None, Flee, Eat, Hunt, Sleep, Wander, FollowPack, Migrate }

    public class Animal
    {
        public int Id; public bool IsAlive = true; public string SpeciesName;
        public double X, Y, Angle, Vx, Vy;
        public AnimalGenome Genes; public AnimalBrain Brain;
        public double Health, Energy, Hunger, Age;
        public AnimalState State = AnimalState.Idle;
        public bool IsPredator, IsHerbivore, IsOmnivore;
        public int PackId = -1; public bool IsPackLeader;
        public double PackLoyalty;
        public List<int> PackMembers = new();
        public bool HasTerritory, IsDomesticated;
        public double TerritoryX, TerritoryY, TerritoryRadius = 200;
        public int OwnerId = -1; public double Tameness, LoyaltyToOwner;
        public bool IsMigrating; public double MigrationTargetX, MigrationTargetY;
        public bool IsPregnant; public double PregnancyProgress, MatingCooldown;
        public Gender Sex; public int FoodEaten, OffspringCount;
        public string CauseOfDeath;
        public bool IsNocturnal, IsSleeping;

        // Целевая система
        public AnimalGoal CurrentGoal = AnimalGoal.None;
        public double TargetX, TargetY;
        public bool HasTarget;
        public double WanderTimer;
        public string GoalDebug = "";

        const double SPEED = 0.007;
        const double TURN = 0.1;
        const double FRIC = 0.90;

        static int nextId = 0;
        static Random R = new();
        static double NA(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
        static double Dist(double x1, double y1, double x2, double y2) { double dx = x1 - x2, dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy); }

        public Animal(double x, double y, AnimalGenome genes = null, AnimalBrain brain = null, string species = "Unknown")
        {
            Id = nextId++; X = x; Y = y;
            Angle = R.NextDouble() * Math.PI * 2;
            Sex = R.NextDouble() < 0.5 ? Gender.Male : Gender.Female;
            Genes = genes ?? new AnimalGenome();
            Brain = brain ?? new AnimalBrain();
            SpeciesName = species;
            Health = MaxHealth; Energy = Genes.MaxEnergy;
            IsNocturnal = Genes.NocturnalActivity > 0.6;
            if (Genes.Aggression > 0.6 && Genes.AttackPower > 0.5) IsPredator = true;
            else if (Genes.Aggression < 0.3) IsHerbivore = true;
            else IsOmnivore = true;
            TerritoryX = x; TerritoryY = y;
            PickWander(2000, 1500);
        }

        public double MaxHealth => 60 + Genes.Size * 30 + Genes.DefensePower * 20;

        void PickWander(double ww, double wh)
        {
            double a = R.NextDouble() * Math.PI * 2;
            double d = 150 + R.NextDouble() * 350;
            TargetX = Math.Clamp(X + Math.Cos(a) * d, 50, ww - 50);
            TargetY = Math.Clamp(Y + Math.Sin(a) * d, 50, wh - 50);
            HasTarget = true;
            WanderTimer = 100 + R.NextDouble() * 200;
        }

        public void Update(double dt, WorldContext world)
        {
            if (!IsAlive) return;

            Age += dt * 0.003;
            if (Age > 25 + Genes.Size * 15) { Die("Old age"); return; }

            double hr = Genes.EnergyDrain * dt * 0.0006;
            Hunger = Math.Min(1, Hunger + hr);
            if (Hunger > 0.95) Health -= dt * 0.2;
            Energy = Math.Max(0, Genes.MaxEnergy * (1 - Hunger * 0.8));

            // Сон
            bool shouldSleep = IsNocturnal ? world.DayLight > 0.6 : world.DayLight < 0.12;
            if (shouldSleep && Hunger < 0.35 && CurrentGoal != AnimalGoal.Flee)
            {
                IsSleeping = true; CurrentGoal = AnimalGoal.Sleep;
                State = AnimalState.Sleeping;
                Health = Math.Min(MaxHealth, Health + dt * 0.3);
                Vx *= 0.95; Vy *= 0.95;
                return;
            }
            IsSleeping = false;

            // ВЫБРАТЬ ЦЕЛЬ
            SelectGoal(dt, world);

            // НАВИГАЦИЯ
            if (HasTarget && CurrentGoal != AnimalGoal.Sleep)
                Navigate(dt);

            // ДЕЙСТВИЯ
            Execute(dt, world);

            // ФИЗИКА
            X += Vx * dt; Y += Vy * dt;
            double m = 30;
            if (X < m) { X = m + 5; Vx = Math.Abs(Vx) * 0.2; PickWander(world.Width, world.Height); }
            if (X > world.Width - m) { X = world.Width - m - 5; Vx = -Math.Abs(Vx) * 0.2; PickWander(world.Width, world.Height); }
            if (Y < m) { Y = m + 5; Vy = Math.Abs(Vy) * 0.2; PickWander(world.Width, world.Height); }
            if (Y > world.Height - m) { Y = world.Height - m - 5; Vy = -Math.Abs(Vy) * 0.2; PickWander(world.Width, world.Height); }
            Vx *= FRIC; Vy *= FRIC;

            if (IsPregnant) { PregnancyProgress += dt * 0.005; if (PregnancyProgress >= 1) { IsPregnant = false; PregnancyProgress = 0; OffspringCount++; } }
            MatingCooldown = Math.Max(0, MatingCooldown - dt);

            // Миграция
            if (world.Season == Season.Autumn && !IsMigrating && Genes.ThermoTolerance < 0.4 && !IsDomesticated)
            { IsMigrating = true; MigrationTargetX = world.Width * 0.5 + (R.NextDouble() - 0.5) * 300; MigrationTargetY = world.Height * 0.8; }
            if (world.Season == Season.Spring && IsMigrating) IsMigrating = false;

            if (Health <= 0) Die("Injuries");
            if (Energy <= 0 && Hunger >= 1) Die("Starvation");
        }

        void SelectGoal(double dt, WorldContext world)
        {
            WanderTimer -= dt;

            // П1: БЕГСТВО (травоядные)
            if (!IsPredator)
            {
                var (tA, tD) = world.NearestThreat(X, Y, Genes.VisionRange, false);
                if (tD < Genes.VisionRange * 0.45)
                {
                    double fleeA = tA + Math.PI;
                    TargetX = Math.Clamp(X + Math.Cos(fleeA) * 250, 50, world.Width - 50);
                    TargetY = Math.Clamp(Y + Math.Sin(fleeA) * 250, 50, world.Height - 50);
                    HasTarget = true;
                    CurrentGoal = AnimalGoal.Flee;
                    State = AnimalState.Fleeing;
                    GoalDebug = $"FLEE d={tD:F0}";
                    return;
                }
            }

            // П2: ГОЛОД — травоядные ищут растения
            if (IsHerbivore && Hunger > 0.25)
            {
                var (fA, fD) = world.NearestFoodForAnimal(X, Y, Genes.VisionRange, false);
                if (fD < Genes.VisionRange * 0.85)
                {
                    TargetX = X + Math.Cos(fA) * fD;
                    TargetY = Y + Math.Sin(fA) * fD;
                    HasTarget = true;
                    CurrentGoal = AnimalGoal.Eat;
                    State = AnimalState.Walking;
                    GoalDebug = $"EAT d={fD:F0}";
                    return;
                }
            }

            // П2b: ГОЛОД — хищники охотятся
            if (IsPredator && Hunger > 0.3)
            {
                var (fA, fD) = world.NearestFoodForAnimal(X, Y, Genes.VisionRange, true);
                if (fD < Genes.VisionRange * 0.8)
                {
                    TargetX = X + Math.Cos(fA) * fD;
                    TargetY = Y + Math.Sin(fA) * fD;
                    HasTarget = true;
                    CurrentGoal = AnimalGoal.Hunt;
                    State = AnimalState.Hunting;
                    GoalDebug = $"HUNT d={fD:F0}";
                    return;
                }
            }

            // П3: Стая
            if (PackId >= 0)
            {
                var pc = world.GetPackCenter(PackId);
                if (pc.HasValue && Dist(X, Y, pc.Value.X, pc.Value.Y) > 120)
                {
                    TargetX = pc.Value.X; TargetY = pc.Value.Y;
                    HasTarget = true;
                    CurrentGoal = AnimalGoal.FollowPack;
                    State = AnimalState.Walking;
                    GoalDebug = "PACK";
                    return;
                }
            }

            // П4: Миграция
            if (IsMigrating)
            {
                TargetX = MigrationTargetX; TargetY = MigrationTargetY;
                HasTarget = true;
                CurrentGoal = AnimalGoal.Migrate;
                State = AnimalState.Walking;
                GoalDebug = "MIGRATE";
                return;
            }

            // БАЗОВОЕ: Блуждание
            if (WanderTimer <= 0) PickWander(world.Width, world.Height);
            CurrentGoal = AnimalGoal.Wander;
            State = AnimalState.Walking;
            GoalDebug = "WANDER";
        }

        void Navigate(double dt)
        {
            if (!HasTarget) return;
            double dx = TargetX - X, dy = TargetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 5) return;

            double targetA = Math.Atan2(dy, dx);
            double diff = NA(targetA - Angle);

            double tr = TURN;
            if (CurrentGoal == AnimalGoal.Flee) tr = 0.2;
            Angle += diff * tr;
            Angle = NA(Angle);

            double sp = Genes.Speed * SPEED;
            if (CurrentGoal == AnimalGoal.Flee) sp *= 1.8;
            else if (CurrentGoal == AnimalGoal.Hunt) sp *= 1.3;
            else if (CurrentGoal == AnimalGoal.Wander) sp *= 0.5;

            if (Math.Abs(diff) < Math.PI * 0.6)
            {
                Vx += Math.Cos(Angle) * sp * dt;
                Vy += Math.Sin(Angle) * sp * dt;
            }
        }

        void Execute(double dt, WorldContext world)
        {
            switch (CurrentGoal)
            {
                case AnimalGoal.Eat:
                    var food = world.TryGetPlantFood(X, Y, 45);
                    if (food != null)
                    {
                        Hunger = Math.Max(0, Hunger - food.Value * 0.15);
                        Brain.Satisfy(0.3); FoodEaten++;
                        State = AnimalState.Eating;
                        if (Hunger < 0.15) CurrentGoal = AnimalGoal.None;
                    }
                    break;

                case AnimalGoal.Hunt:
                    var prey = world.TryAnimalAttack(this, Genes.DamageOutput, 40);
                    if (prey != null)
                    {
                        Hunger = Math.Max(0, Hunger - 0.35);
                        Brain.Satisfy(0.5); FoodEaten++;
                        CurrentGoal = AnimalGoal.None;
                    }
                    break;

                case AnimalGoal.Flee:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 40)
                        CurrentGoal = AnimalGoal.None;
                    break;
            }
        }

        public void TakeDamage(double d, string s) { Health -= d * (1 - Genes.DefensePower * 0.4); Brain.Scare(0.5); if (Health <= 0) Die(s); }
        public void Die(string c) { if (!IsAlive) return; IsAlive = false; CauseOfDeath = c; State = AnimalState.Dead; }
        public bool TryTame(Human h, double sk) { if (IsPredator && Genes.Aggression > 0.8) return false; double ch = sk * 0.1 * (1 - Genes.Aggression); if (R.NextDouble() < ch) { Tameness = Math.Min(1, Tameness + 0.15); if (Tameness > 0.6) { IsDomesticated = true; OwnerId = h.Id; return true; } } return false; }
        public void Feed(double n, int f) { Hunger = Math.Max(0, Hunger - n); Brain.Satisfy(n); Tameness = Math.Min(1, Tameness + 0.02); }

        public static Animal CreateOffspring(Animal m, Animal f, double x, double y)
        {
            var cg = AnimalGenome.Cross(m.Genes, f.Genes); cg.Mutate(0.2, 0.12);
            var cb = AnimalBrain.Crossover(m.Brain, f.Brain, R); cb.Mutate(0.15, 0.1);
            var c = new Animal(x, y, cg, cb, m.SpeciesName) { Age = 0, PackId = m.PackId };
            c.Health = c.MaxHealth * 0.6;
            if (m.IsDomesticated) { c.Tameness = m.Tameness * 0.5; c.IsDomesticated = c.Tameness > 0.5; c.OwnerId = m.OwnerId; }
            return c;
        }

        public float DrawSize => (float)(6 + Genes.Size * 5);
        public Color GetColor() { if (!IsAlive) return Color.DarkGray; if (IsDomesticated) return Color.FromArgb(Math.Clamp((int)(Genes.ColorR * 200 + 55), 55, 255), Math.Clamp((int)(Genes.ColorG * 200 + 55), 55, 255), Math.Clamp((int)(Genes.ColorB * 160 + 95), 95, 255)); return Genes.GetColor(); }
        public double CalculateFitness() => Age * 2 + FoodEaten * 0.5 + OffspringCount * 20 + Health / MaxHealth * 10;
    }

    public enum AnimalState { Idle, Walking, Running, Eating, Sleeping, Hunting, Fleeing, Mating, Defending, Dead }
}