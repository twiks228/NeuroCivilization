using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace NeuroCivilization.Core
{
    public enum AnimalGoal { None, Flee, Eat, Hunt, Sleep, Wander, FollowPack, Migrate }

    public class Animal
    {
        public int Id;
        public bool IsAlive = true;
        public string SpeciesName;
        public double X, Y, Angle, Vx, Vy;
        public AnimalGenome Genes;
        public AnimalBrain Brain;
        public double Health, Energy, Hunger, Age;
        public AnimalState State = AnimalState.Idle;
        public bool IsPredator, IsHerbivore, IsOmnivore;
        public int PackId = -1;
        public bool IsPackLeader;
        public double PackLoyalty;
        public List<int> PackMembers = new();
        public bool HasTerritory, IsDomesticated;
        public double TerritoryX, TerritoryY, TerritoryRadius = 200;
        public int OwnerId = -1;
        public double Tameness, LoyaltyToOwner;
        public bool IsMigrating;
        public double MigrationTargetX, MigrationTargetY;
        public bool IsPregnant;
        public double PregnancyProgress, MatingCooldown;
        public Gender Sex;
        public int FoodEaten, OffspringCount;
        public string CauseOfDeath = "";
        public bool IsNocturnal, IsSleeping;

        public AnimalGoal CurrentGoal = AnimalGoal.None;
        public double TargetX, TargetY;
        public bool HasTarget;
        public double WanderTimer;
        public string GoalDebug = "";

        private readonly double[] _sensors = new double[AnimalBrain.INPUTS];

        private const double SPEED = 0.0056;
        private const double TURN = 0.11;
        private const double FRIC = 0.89;

        private static int _nextId;
        private static Random R => Random.Shared;

        public static void ResetIds() => _nextId = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NA(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Dist(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public Animal(double x, double y, AnimalGenome? genes = null, AnimalBrain? brain = null, string species = "Unknown")
        {
            Id = _nextId++;
            X = x;
            Y = y;
            Angle = R.NextDouble() * Math.PI * 2;
            Sex = R.NextDouble() < 0.5 ? Gender.Male : Gender.Female;
            Genes = genes ?? new AnimalGenome();
            Brain = brain ?? new AnimalBrain();
            SpeciesName = species;
            Health = MaxHealth;
            Energy = Genes.MaxEnergy;
            IsNocturnal = Genes.NocturnalActivity > 0.6;
            if (Genes.Aggression > 0.6 && Genes.AttackPower > 0.5) IsPredator = true;
            else if (Genes.Aggression < 0.35) IsHerbivore = true;
            else IsOmnivore = true;
            TerritoryX = x;
            TerritoryY = y;
            PickWander(2000, 1500);
        }

        public double MaxHealth => 65 + Genes.Size * 35 + Genes.DefensePower * 25;

        private void PickWander(double ww, double wh)
        {
            double a = R.NextDouble() * Math.PI * 2;
            double d = 150 + R.NextDouble() * 320;
            TargetX = Math.Clamp(X + Math.Cos(a) * d, 50, ww - 50);
            TargetY = Math.Clamp(Y + Math.Sin(a) * d, 50, wh - 50);
            HasTarget = true;
            WanderTimer = 90 + R.NextDouble() * 180;
        }

        public void Update(double dt, WorldContext world)
        {
            if (!IsAlive) return;

            Age += dt * 0.003;
            if (Age > 28 + Genes.Size * 15) { Die("Old age"); return; }

            double hr = Genes.EnergyDrain * dt * 0.00055;
            Hunger = Math.Min(1.0, Hunger + hr);
            Brain.Hunger = Hunger;
            if (Hunger > 0.95) Health -= dt * 0.18;
            Energy = Math.Max(0.0, Genes.MaxEnergy * (1.0 - Hunger * 0.8));

            // Если животное приручено и следует за хозяином
            if (IsDomesticated && OwnerId >= 0)
            {
                var owner = world.World.GetHumanById(OwnerId);
                if (owner != null && owner.IsAlive)
                {
                    double od = Dist(X, Y, owner.X, owner.Y);
                    if (od > 90 && Hunger < 0.65)
                    {
                        TargetX = owner.X + (R.NextDouble() - 0.5) * 35;
                        TargetY = owner.Y + (R.NextDouble() - 0.5) * 35;
                        HasTarget = true;
                        CurrentGoal = AnimalGoal.FollowPack;
                        State = AnimalState.Walking;
                        GoalDebug = $"FOLLOW {owner.Name}";
                    }
                }
                else
                {
                    OwnerId = -1;
                }
            }

            // Сон (не в воде — животное выбирается на берег)
            bool shouldSleep = IsNocturnal ? world.DayLight > 0.65 : world.DayLight < 0.12;
            if (IsSleeping && world.World.Env.IsWater(X, Y))
            {
                IsSleeping = false;
                CurrentGoal = AnimalGoal.Wander;
                State = AnimalState.Walking;
            }
            if (shouldSleep && Hunger < 0.40 && CurrentGoal != AnimalGoal.Flee
                && !world.World.Env.IsWater(X, Y))
            {
                IsSleeping = true;
                CurrentGoal = AnimalGoal.Sleep;
                State = AnimalState.Sleeping;
                Health = Math.Min(MaxHealth, Health + dt * 0.35);
                Vx *= 0.92;
                Vy *= 0.92;
                return;
            }
            IsSleeping = false;

            // Обновление нейросети животного (раз в 3 тика для экономии CPU)
            if ((Id + world.World.Tick) % 3 == 0)
            {
                UpdateSensorsAndBrain(world);
            }

            SelectGoal(dt, world);

            if (HasTarget && CurrentGoal != AnimalGoal.Sleep)
                Navigate(dt);

            Execute(dt, world);

            // Swimming: deep water slows animals down
            double adrag = world.World.Env.IsWater(X, Y) ? 0.32 : 1.0;
            X += Vx * dt * adrag;
            Y += Vy * dt * adrag;
            const double m = 30;
            if (X < m) { X = m + 5; Vx = Math.Abs(Vx) * 0.2; PickWander(world.Width, world.Height); }
            else if (X > world.Width - m) { X = world.Width - m - 5; Vx = -Math.Abs(Vx) * 0.2; PickWander(world.Width, world.Height); }
            if (Y < m) { Y = m + 5; Vy = Math.Abs(Vy) * 0.2; PickWander(world.Width, world.Height); }
            else if (Y > world.Height - m) { Y = world.Height - m - 5; Vy = -Math.Abs(Vy) * 0.2; PickWander(world.Width, world.Height); }
            Vx *= FRIC;
            Vy *= FRIC;

            if (IsPregnant)
            {
                PregnancyProgress += dt * 0.005;
            }
            MatingCooldown = Math.Max(0, MatingCooldown - dt);

            if (world.Season == Season.Autumn && !IsMigrating && Genes.ThermoTolerance < 0.4 && !IsDomesticated)
            {
                IsMigrating = true;
                MigrationTargetX = world.Width * 0.5 + (R.NextDouble() - 0.5) * 400;
                MigrationTargetY = world.Height * 0.8;
            }
            if (world.Season == Season.Spring && IsMigrating) IsMigrating = false;

            if (Health <= 0) Die("Injuries");
            else if (Energy <= 0 && Hunger >= 1.0) Die("Starvation");
        }

        private void UpdateSensorsAndBrain(WorldContext world)
        {
            double vr = Math.Max(1.0, Genes.VisionRange);
            var (fA, fD) = world.NearestFoodForAnimal(X, Y, vr, IsPredator);
            var (tA, tD) = world.NearestThreat(X, Y, vr, IsPredator);
            var (pA, pD) = world.NearestPackMember(X, Y, Id, PackId, vr);

            _sensors[0] = Hunger;
            _sensors[1] = Health / MaxHealth;
            _sensors[2] = Energy / Math.Max(1.0, Genes.MaxEnergy);
            _sensors[3] = Brain.Fear;
            _sensors[4] = NA(fA - Angle) / Math.PI;
            _sensors[5] = Math.Clamp(fD / vr, 0, 1);
            _sensors[6] = NA(tA - Angle) / Math.PI;
            _sensors[7] = Math.Clamp(tD / vr, 0, 1);
            _sensors[8] = NA(pA - Angle) / Math.PI;
            _sensors[9] = Math.Clamp(pD / vr, 0, 1);
            _sensors[10] = world.DayLight;
            _sensors[11] = world.Temperature;
            _sensors[12] = IsPredator ? 1.0 : -1.0;
            _sensors[13] = IsDomesticated ? 1.0 : 0.0;
            _sensors[14] = Math.Clamp(X / world.Width, 0, 1);
            _sensors[15] = Math.Clamp(Y / world.Height, 0, 1);

            Brain.Think(_sensors);
        }

        private void SelectGoal(double dt, WorldContext world)
        {
            WanderTimer -= dt;

            // П1: Бегство (для травоядных и всеядных)
            if (!IsPredator && !IsDomesticated)
            {
                var (tA, tD) = world.NearestThreat(X, Y, Genes.VisionRange, false);
                if (tD < Genes.VisionRange * 0.5)
                {
                    double fleeA = tA + Math.PI;
                    TargetX = Math.Clamp(X + Math.Cos(fleeA) * 260, 50, world.Width - 50);
                    TargetY = Math.Clamp(Y + Math.Sin(fleeA) * 260, 50, world.Height - 50);
                    HasTarget = true;
                    CurrentGoal = AnimalGoal.Flee;
                    State = AnimalState.Fleeing;
                    GoalDebug = $"FLEE d={tD:F0}";
                    Brain.Scare(0.3);
                    return;
                }
            }

            // П2: Травоядные и всеядные ищут растения
            if ((IsHerbivore || IsOmnivore) && Hunger > 0.22)
            {
                var (fA, fD) = world.NearestFoodForAnimal(X, Y, Genes.VisionRange, false);
                if (fD < Genes.VisionRange * 0.9)
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

            // П2b: Хищники охотятся
            if (IsPredator && Hunger > 0.28)
            {
                var (fA, fD) = world.NearestFoodForAnimal(X, Y, Genes.VisionRange, true);
                if (fD < Genes.VisionRange * 0.9)
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
                if (pc.HasValue && Dist(X, Y, pc.Value.X, pc.Value.Y) > 130)
                {
                    TargetX = pc.Value.X;
                    TargetY = pc.Value.Y;
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
                TargetX = MigrationTargetX;
                TargetY = MigrationTargetY;
                HasTarget = true;
                CurrentGoal = AnimalGoal.Migrate;
                State = AnimalState.Walking;
                GoalDebug = "MIGRATE";
                return;
            }

            if (WanderTimer <= 0 || (HasTarget && Dist(X, Y, TargetX, TargetY) < 25))
                PickWander(world.Width, world.Height);
            CurrentGoal = AnimalGoal.Wander;
            State = AnimalState.Walking;
            GoalDebug = "WANDER";
        }

        private void Navigate(double dt)
        {
            if (!HasTarget) return;
            double dx = TargetX - X, dy = TargetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 5) return;

            double targetA = Math.Atan2(dy, dx);
            double diff = NA(targetA - Angle);

            // Модуляция поворота нейросетью
            double neuralTurn = Brain.LastOutput[1] * 0.025;
            double tr = CurrentGoal == AnimalGoal.Flee ? 0.22 : TURN;
            Angle = NA(Angle + diff * tr + neuralTurn);

            double sp = Genes.Speed * SPEED * (1.0 + Math.Max(0, Brain.LastOutput[0]) * 0.15);
            if (CurrentGoal == AnimalGoal.Flee) sp *= 1.75;
            else if (CurrentGoal == AnimalGoal.Hunt) sp *= 1.35;
            else if (CurrentGoal == AnimalGoal.Wander) sp *= 0.55;

            if (Math.Abs(diff) < Math.PI * 0.65)
            {
                Vx += Math.Cos(Angle) * sp * dt;
                Vy += Math.Sin(Angle) * sp * dt;
            }
        }

        private void Execute(double dt, WorldContext world)
        {
            switch (CurrentGoal)
            {
                case AnimalGoal.Eat:
                    var food = world.TryGetPlantFood(X, Y, 45);
                    if (food != null)
                    {
                        Hunger = Math.Max(0, Hunger - food.Value * 0.16);
                        Health = Math.Min(MaxHealth, Health + food.Value * 0.2);
                        Brain.Satisfy(0.35);
                        FoodEaten++;
                        State = AnimalState.Eating;
                        if (Hunger < 0.12) CurrentGoal = AnimalGoal.None;
                    }
                    break;

                case AnimalGoal.Hunt:
                    var prey = world.TryAnimalAttack(this, Genes.DamageOutput * 0.85, 42);
                    if (prey != null)
                    {
                        Hunger = Math.Max(0, Hunger - 0.45);
                        Health = Math.Min(MaxHealth, Health + 12);
                        Brain.Satisfy(0.6);
                        FoodEaten++;
                        CurrentGoal = AnimalGoal.None;
                    }
                    break;

                case AnimalGoal.Flee:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 40)
                        CurrentGoal = AnimalGoal.None;
                    break;
            }
        }

        public void TakeDamage(double d, string s)
        {
            Health -= d * (1.0 - Genes.DefensePower * 0.4);
            Brain.Scare(0.5);
            if (Health <= 0) Die(s);
        }

        public void Die(string c)
        {
            if (!IsAlive) return;
            IsAlive = false;
            CauseOfDeath = c;
            State = AnimalState.Dead;
        }

        public bool TryTame(Human h, double sk)
        {
            if (IsPredator && Genes.Aggression > 0.85) return false;
            double ch = (0.08 + sk * 0.15) * (1.1 - Genes.Aggression * 0.7);
            if (R.NextDouble() < ch)
            {
                Tameness = Math.Min(1.0, Tameness + 0.22);
                if (Tameness >= 0.55)
                {
                    IsDomesticated = true;
                    OwnerId = h.Id;
                    LoyaltyToOwner = 0.8;
                    return true;
                }
            }
            return false;
        }

        public void Feed(double n, int f)
        {
            Hunger = Math.Max(0, Hunger - n * 0.15);
            Brain.Satisfy(n * 0.1);
            Tameness = Math.Min(1.0, Tameness + 0.08);
        }

        public static Animal CreateOffspring(Animal m, Animal f, double x, double y)
        {
            var cg = AnimalGenome.Cross(m.Genes, f.Genes);
            cg.Mutate(0.18, 0.12);
            var cb = AnimalBrain.Crossover(m.Brain, f.Brain, R);
            cb.Mutate(0.15, 0.1);
            var c = new Animal(x, y, cg, cb, m.SpeciesName)
            {
                Age = 0,
                PackId = m.PackId,
                IsHerbivore = m.IsHerbivore,
                IsPredator = m.IsPredator,
                IsOmnivore = m.IsOmnivore
            };
            c.Health = c.MaxHealth * 0.75;
            if (m.IsDomesticated)
            {
                c.Tameness = m.Tameness * 0.7;
                c.IsDomesticated = c.Tameness >= 0.5;
                c.OwnerId = m.OwnerId;
            }
            return c;
        }

        public float DrawSize => (float)(6 + Genes.Size * 5);

        public Color GetColor()
        {
            if (!IsAlive) return Color.DarkGray;
            if (IsDomesticated)
                return Color.FromArgb(
                    Math.Clamp((int)(Genes.ColorR * 180 + 65), 65, 255),
                    Math.Clamp((int)(Genes.ColorG * 190 + 65), 65, 255),
                    Math.Clamp((int)(Genes.ColorB * 150 + 105), 105, 255));
            return Genes.GetColor();
        }

        public double CalculateFitness() =>
            Age * 2.0 + FoodEaten * 0.6 + OffspringCount * 22.0 + (Health / MaxHealth) * 12.0;
    }

    public enum AnimalState { Idle, Walking, Running, Eating, Sleeping, Hunting, Fleeing, Mating, Defending, Dead }
}
