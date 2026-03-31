using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace NeuroCivilization.Core
{
    public enum Goal
    {
        None, Flee, FindFood, EatFood, HuntAnimal, GatherWood,
        GatherStone, Rest, Sleep, Socialize, Mate, Teach,
        FollowLeader, Build, Craft, Explore, DefendTerritory,
        TameAnimal, Cook, Heal, Trade, GuardCamp
    }

    public class Human
    {
        public int Id;
        public string Name;
        public bool IsAlive = true;
        public Gender Sex;
        static int nextId = 0;

        public double X, Y, Angle, Vx, Vy;
        public Genome Genes;
        public FastBrain Brain;

        public double Health = 100;
        public double MaxHealth => 80 + Genes.EffBodySize * 30 + Genes.EffResilience * 20;
        public double Energy = 100;
        public double MaxEnergy => 80 + Genes.EffStamina * 40 + Genes.EffBodySize * 10;
        public double Hunger, Thirst, Fatigue, Temperature = 0.5, Pain;
        public double Happiness;
        public double MateCooldown;

        public double Age, AgeSpeed = 1;
        public bool IsChild => Age < 12;
        public bool IsAdult => Age >= 16 && Age < 55;
        public bool IsElder => Age >= 55;
        public bool IsPregnant;
        public double PregnancyProgress;
        public Human PregnancyPartner;

        public Dictionary<int, Relationship> Relationships = new();
        public int TribeId = -1;
        public bool IsLeader;
        public double Reputation, Trust;

        public double SkillHunting, SkillGathering, SkillBuilding;
        public double SkillCrafting, SkillFighting, SkillCooking;
        public double SkillMedicine, SkillSpeaking, SkillTeaching, SkillLeading;

        public List<Item> Inventory = new();
        public Item EquippedTool, EquippedWeapon;

        public HashSet<string> KnownTechnologies = new();
        public List<int> KnownWords = new();
        public int VocabularySize => KnownWords.Count;

        // Память об опасных животных
        public HashSet<string> KnownDangerousSpecies = new();
        // Запас мяса
        public double MeatStored;

        public HumanState State = HumanState.Idle;

        public int FoodEaten, ChildrenBorn, ToolsMade;
        public int BuildingsBuilt, BattlesWon, LessonsGiven, AnimalsKilled;
        public double TotalDistanceTraveled;
        public string CauseOfDeath;

        // Целевая система
        public Goal CurrentGoal = Goal.None;
        public double TargetX, TargetY;
        public bool HasTarget;
        public double GoalTimer;
        public double GoalCooldown;
        public string GoalDebug = "";
        public double SocialTimer;
        public int TargetEntityId = -1; // ID цели (животное/человек)

        public double NeedSocial => Math.Max(0, 1 - Brain.Oxytocin * 3 - Relationships.Count * 0.08);

        EpigeneticContext epiCtx = new();
        static Random R = new();

        const double SPEED_WALK = 0.008;
        const double SPEED_RUN = 0.018;
        const double TURN_RATE = 0.12;
        const double FRICTION = 0.88;

        public Human(double x, double y, Genome genes = null, FastBrain brain = null)
        {
            Id = nextId++;
            X = x; Y = y;
            Angle = R.NextDouble() * Math.PI * 2;
            Sex = R.NextDouble() < 0.5 ? Gender.Male : Gender.Female;
            Genes = genes ?? new Genome();
            Brain = brain ?? new FastBrain();
            Health = MaxHealth;
            Energy = MaxEnergy;
            Trust = Genes.Sociability * 0.5;
            Name = GenName();
            SetExplore(2000, 1500);
        }

        string GenName()
        {
            string[] sy = { "ka", "ra", "ma", "to", "ni", "ba", "lu",
                "ke", "so", "da", "no", "te", "wa", "mi", "ru", "ha",
                "pe", "li", "ko", "sa", "di", "na", "bo", "ze", "fi" };
            int len = 2 + R.Next(2);
            string n = "";
            for (int i = 0; i < len; i++) n += sy[R.Next(sy.Length)];
            return char.ToUpper(n[0]) + n[1..];
        }

        static double NA(double a)
        { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
        static double Dist(double x1, double y1, double x2, double y2)
        { double dx = x1 - x2, dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy); }

        void SetExplore(double ww, double wh)
        {
            double a = R.NextDouble() * Math.PI * 2;
            double d = 200 + R.NextDouble() * 400;
            TargetX = Math.Clamp(X + Math.Cos(a) * d, 80, ww - 80);
            TargetY = Math.Clamp(Y + Math.Sin(a) * d, 80, wh - 80);
            HasTarget = true;
            GoalTimer = 120 + R.NextDouble() * 200;
        }

        void SetTarget(double tx, double ty)
        { TargetX = tx; TargetY = ty; HasTarget = true; }

        // ============ UPDATE (ОПТИМИЗИРОВАН) ============

        public void Update(double dt, WorldContext world)
        {
            if (!IsAlive) return;

            Age += dt * AgeSpeed * 0.004;
            Genes.EpigeneticAge = Age;
            if (Age > Genes.MaxAge) { Die("Old age"); return; }

            UpdateNeeds(dt, world);

            // Эпигенетика — раз в 10 тиков для оптимизации
            if ((Id + (int)(Age * 100)) % 10 == 0)
            {
                UpdateEpiCtx(world);
                if (IsChild) Genes.CriticalPeriodUpdate(dt * 10, epiCtx);
                else Genes.UpdateEpigenetics(dt * 10, epiCtx);
            }

            Brain.UpdateCircadian(world.DayLight);

            Happiness = (1 - Hunger) * 0.25 + (Health / MaxHealth) * 0.2 +
                        (1 - Fatigue) * 0.1 + Brain.Serotonin * 0.2 +
                        Brain.Oxytocin * 0.15 + Brain.Dopamine * 0.1;

            GoalCooldown = Math.Max(0, GoalCooldown - dt);
            GoalTimer -= dt;
            MateCooldown = Math.Max(0, MateCooldown - dt);

            if (GoalCooldown <= 0) SelectGoal(dt, world);
            Navigate(dt, world);
            Act(dt, world);
            Physics(dt, world);

            if (IsPregnant) PregnancyProgress += dt * 0.005;

            if (Health <= 0) Die("Injuries");
            if (Hunger >= 1 && Energy <= 0) Die("Starvation");

            Brain.Experience += 0.0002;
        }

        // ============ ПИРАМИДА ПОТРЕБНОСТЕЙ + РЕАЛИЗМ ============

        // В Human.cs ЗАМЕНИТЬ метод SelectGoal:

        void SelectGoal(double dt, WorldContext world)
        {
            // ═══ УРОВЕНЬ 1: НЕМЕДЛЕННОЕ ВЫЖИВАНИЕ ═══

            // Бегство от хищника
            Animal nearDanger = FindDangerousAnimal(world);
            if (nearDanger != null)
            {
                double pd = Dist(X, Y, nearDanger.X, nearDanger.Y);
                if (pd < 140)
                {
                    double fa = Math.Atan2(nearDanger.Y - Y, nearDanger.X - X) + Math.PI;
                    SetTarget(
                        Math.Clamp(X + Math.Cos(fa) * 300, 80, world.Width - 80),
                        Math.Clamp(Y + Math.Sin(fa) * 300, 80, world.Height - 80));
                    CurrentGoal = Goal.Flee;
                    State = HumanState.Fleeing;
                    GoalDebug = $"FLEE {nearDanger.SpeciesName} d={pd:F0}!";
                    GoalCooldown = 15;
                    Brain.Fear(0.4);
                    KnownDangerousSpecies.Add(nearDanger.SpeciesName);
                    return;
                }
            }

            // Критический голод → есть запас мяса
            if (Hunger > 0.5 && MeatStored > 2)
            {
                double eat = Math.Min(MeatStored, 12);
                MeatStored -= eat;
                Eat(eat);
                State = HumanState.Eating;
                GoalDebug = $"ATE stored meat ({MeatStored:F0} left)";
                GoalCooldown = 5;
                return;
            }

            // Критический голод → искать растительную еду
            if (Hunger > 0.4)
            {
                if (TryFindFood(world)) return;
            }

            // Голод → охота (НАМНОГО чаще чем раньше!)
            if (Hunger > 0.3 && IsAdult)
            {
                if (TryFindHunt(world)) return;
            }

            // Любой голод → еда если видна рядом
            if (Hunger > 0.15)
            {
                var (_, fd) = world.NearestFood(X, Y, 150);
                if (fd < 130 && TryFindFood(world)) return;
            }

            // Критическая усталость
            if (Fatigue > 0.75)
            {
                CurrentGoal = Goal.Rest;
                HasTarget = false;
                State = HumanState.Resting;
                GoalDebug = "REST(exhausted)";
                GoalCooldown = 25;
                return;
            }

            // Критическое здоровье
            if (Health < MaxHealth * 0.25)
            {
                CurrentGoal = KnownTechnologies.Contains("medicine_herbal") ? Goal.Heal : Goal.Rest;
                HasTarget = false;
                State = HumanState.Resting;
                GoalDebug = CurrentGoal == Goal.Heal ? "HEALING" : "REST(wounded)";
                GoalCooldown = 25;
                return;
            }

            // Сон
            if (Brain.IsSleeping)
            {
                var shelter = FindNearestBuilding(world, 200);
                if (shelter != null) SetTarget(shelter.X, shelter.Y);
                else HasTarget = false;
                CurrentGoal = Goal.Sleep;
                State = HumanState.Sleeping;
                GoalDebug = shelter != null ? "SLEEP→shelter" : "SLEEP";
                GoalCooldown = 15;
                return;
            }

            // ═══ УРОВЕНЬ 2: ПРОАКТИВНАЯ ДОБЫЧА ═══

            // Умеренный голод → собирать
            if (Hunger > 0.2)
            {
                if (TryFindFood(world)) return;
            }

            // Усталость
            if (Fatigue > 0.5)
            {
                CurrentGoal = Goal.Rest;
                HasTarget = false;
                State = HumanState.Resting;
                GoalDebug = $"REST f={Fatigue:F2}";
                GoalCooldown = 18;
                return;
            }

            // Охота для запаса мяса (ГЛАВНОЕ ЗАНЯТИЕ сытых взрослых!)
            if (IsAdult && MeatStored < 25 && Hunger < 0.35 && Fatigue < 0.5 &&
                Health > MaxHealth * 0.5)
            {
                // 15% шанс каждый тик выбора — охотятся ЧАСТО
                if (R.NextDouble() < 0.15)
                {
                    if (TryFindHunt(world))
                    {
                        GoalDebug = $"HUNT(stockpile meat={MeatStored:F0})";
                        return;
                    }
                }
            }

            // Защита территории
            if (IsAdult && TribeId >= 0 && nearDanger != null)
            {
                double td = Dist(X, Y, nearDanger.X, nearDanger.Y);
                if (td < 250 && td > 80 && SkillFighting > 0.1 && Health > MaxHealth * 0.5)
                {
                    SetTarget(nearDanger.X, nearDanger.Y);
                    TargetEntityId = nearDanger.Id;
                    CurrentGoal = Goal.DefendTerritory;
                    State = HumanState.Running;
                    GoalDebug = $"DEFEND vs {nearDanger.SpeciesName}";
                    GoalCooldown = 20;
                    return;
                }
            }

            // ═══ УРОВЕНЬ 3: РАЗМНОЖЕНИЕ ═══

            if (IsAdult && !IsPregnant && MateCooldown <= 0 &&
                Hunger < 0.4 && Health > MaxHealth * 0.4 && Fatigue < 0.5)
            {
                var mate = FindMate(world);
                if (mate != null)
                {
                    SetTarget(mate.X, mate.Y);
                    TargetEntityId = mate.Id;
                    CurrentGoal = Goal.Mate;
                    State = HumanState.Walking;
                    GoalDebug = $"MATE→{mate.Name}";
                    GoalCooldown = 20;
                    MateCooldown = 100;
                    return;
                }
            }

            // ═══ УРОВЕНЬ 4: РАЗВИТИЕ (по приоритету) ═══

            // Выбираем случайное занятие из доступных
            double roll = R.NextDouble();

            // 25% — строить (если умеет и нет дома рядом)
            if (roll < 0.25 && IsAdult && KnownTechnologies.Contains("building") &&
                Hunger < 0.25 && Fatigue < 0.4)
            {
                var myBuilding = FindNearestBuilding(world, 300);
                if (myBuilding == null)
                {
                    CurrentGoal = Goal.Build;
                    HasTarget = false;
                    State = HumanState.Building;
                    GoalDebug = "BUILD home";
                    GoalCooldown = 35;
                    GoalTimer = 20;
                    return;
                }
            }

            // 20% — крафт (если нет инструментов)
            if (roll < 0.45 && IsAdult && KnownTechnologies.Contains("tools") &&
                Hunger < 0.25 && ToolsMade < 3)
            {
                CurrentGoal = Goal.Craft;
                HasTarget = false;
                State = HumanState.Crafting;
                GoalDebug = "CRAFT tools";
                GoalCooldown = 25;
                GoalTimer = 15;
                return;
            }

            // 15% — учить детей
            if (roll < 0.60 && IsAdult && SkillTeaching > 0.03)
            {
                Human child = null;
                foreach (var h in world.World.Humans)
                {
                    if (!h.IsAlive || h.Id == Id || !h.IsChild) continue;
                    if (Dist(X, Y, h.X, h.Y) < 200)
                    { child = h; break; }
                }
                if (child != null)
                {
                    SetTarget(child.X, child.Y);
                    TargetEntityId = child.Id;
                    CurrentGoal = Goal.Teach;
                    State = HumanState.Walking;
                    GoalDebug = $"TEACH→{child.Name}";
                    GoalCooldown = 25;
                    GoalTimer = 25;
                    return;
                }
            }

            // 10% — общение (КОРОТКОЕ!)
            if (roll < 0.70 && NeedSocial > 0.15)
            {
                var p = world.GetNearestHuman(X, Y, Id, 180);
                if (p != null)
                {
                    SetTarget(p.X, p.Y);
                    TargetEntityId = p.Id;
                    CurrentGoal = Goal.Socialize;
                    State = HumanState.Walking;
                    GoalDebug = $"CHAT→{p.Name}";
                    GoalCooldown = 20;
                    GoalTimer = 15 + R.NextDouble() * 10; // КОРОТКОЕ общение 15-25 тиков
                    SocialTimer = 0;
                    return;
                }
            }

            // 10% — приручение
            if (roll < 0.80 && IsAdult && KnownTechnologies.Contains("animal_taming") &&
                Hunger < 0.2 && MeatStored > 5)
            {
                foreach (var a in world.World.Animals)
                {
                    if (!a.IsAlive || a.IsPredator || a.IsDomesticated) continue;
                    double d = Dist(X, Y, a.X, a.Y);
                    if (d < 150)
                    {
                        SetTarget(a.X, a.Y);
                        TargetEntityId = a.Id;
                        CurrentGoal = Goal.TameAnimal;
                        State = HumanState.Walking;
                        GoalDebug = $"TAME {a.SpeciesName}";
                        GoalCooldown = 30;
                        return;
                    }
                }
            }

            // 10% — сбор камня/дерева
            if (roll < 0.90 && IsAdult && Hunger < 0.2)
            {
                foreach (var r in world.World.Resources)
                {
                    if (r.Amount < 3) continue;
                    if (r.Type != ResourceType.Stone && r.Type != ResourceType.Wood) continue;
                    double d = Dist(X, Y, r.X, r.Y);
                    if (d < 250)
                    {
                        SetTarget(r.X, r.Y);
                        CurrentGoal = r.Type == ResourceType.Stone ? Goal.GatherStone : Goal.GatherWood;
                        State = HumanState.Walking;
                        GoalDebug = $"GATHER {r.Type}";
                        GoalCooldown = 20;
                        return;
                    }
                }
            }

            // 5% — следовать за лидером (КОРОТКО!)
            if (roll < 0.95 && TribeId >= 0 && !IsLeader)
            {
                var leader = world.GetTribalLeader(TribeId);
                if (leader != null && leader.Id != Id && Dist(X, Y, leader.X, leader.Y) > 120)
                {
                    SetTarget(leader.X, leader.Y);
                    CurrentGoal = Goal.FollowLeader;
                    State = HumanState.Walking;
                    GoalDebug = $"FOLLOW→{leader.Name}";
                    GoalCooldown = 30;
                    GoalTimer = 40; // Максимум 40 тиков следования
                    return;
                }
            }

            // ═══ БАЗОВОЕ: ИССЛЕДОВАНИЕ ═══
            if (GoalTimer <= 0 || CurrentGoal == Goal.None ||
                (HasTarget && Dist(X, Y, TargetX, TargetY) < 40))
            {
                SetExplore(world.Width, world.Height);
                CurrentGoal = Goal.Explore;
                State = HumanState.Walking;
                GoalDebug = "EXPLORE";
                GoalCooldown = 10;
            }
        }

        // ============ ПОИСК ЦЕЛЕЙ ============

        Animal FindDangerousAnimal(WorldContext world)
        {
            Animal closest = null;
            double best = 200;
            foreach (var a in world.World.Animals)
            {
                if (!a.IsAlive) continue;
                // Хищники всегда опасны
                // + запомненные виды тоже
                bool dangerous = a.IsPredator || KnownDangerousSpecies.Contains(a.SpeciesName);
                if (!dangerous) continue;
                double d = Dist(X, Y, a.X, a.Y);
                if (d < best) { best = d; closest = a; }
            }
            return closest;
        }

        Building FindNearestBuilding(WorldContext world, double range)
        {
            Building closest = null;
            double best = range;
            foreach (var b in world.World.Buildings)
            {
                if (b.TribeId != TribeId && TribeId >= 0) continue;
                double d = Dist(X, Y, b.X, b.Y);
                if (d < best) { best = d; closest = b; }
            }
            return closest;
        }

        Human FindMate(WorldContext world)
        {
            Human best = null;
            double bestScore = -1;
            foreach (var h in world.World.Humans)
            {
                if (!h.IsAlive || h.Id == Id) continue;
                if (!h.IsAdult || h.IsPregnant || h.Sex == Sex) continue;
                if (h.Hunger > 0.6 || h.Health < h.MaxHealth * 0.3) continue;
                double dist = Dist(X, Y, h.X, h.Y);
                if (dist > 250) continue;
                double score = 10 - dist * 0.02 + GetRelationStrength(h.Id) * 5 +
                    h.Health / h.MaxHealth * 3 + (1 - h.Hunger) * 2;
                if (GetGeneticDistance(h) < 0.05) score -= 10;
                if (score > bestScore) { bestScore = score; best = h; }
            }
            return bestScore > 3 ? best : null;
        }

        bool TryFindFood(WorldContext world)
        {
            var (foodA, foodD) = world.NearestFood(X, Y, Genes.EffVisionRange);
            if (foodD < Genes.EffVisionRange * 0.9)
            {
                SetTarget(X + Math.Cos(foodA) * foodD, Y + Math.Sin(foodA) * foodD);
                CurrentGoal = foodD < 50 ? Goal.EatFood : Goal.FindFood;
                State = Hunger > 0.55 ? HumanState.Running : HumanState.Walking;
                GoalDebug = $"FOOD d={foodD:F0}";
                GoalCooldown = 12;
                return true;
            }
            return false;
        }

        bool TryFindHunt(WorldContext world)
        {
            Animal bestPrey = null;
            double bestD = Genes.EffVisionRange * 0.7;
            foreach (var a in world.World.Animals)
            {
                if (!a.IsAlive || a.IsPredator) continue;
                double d = Dist(X, Y, a.X, a.Y);
                if (d < bestD) { bestD = d; bestPrey = a; }
            }
            if (bestPrey != null)
            {
                SetTarget(bestPrey.X, bestPrey.Y);
                TargetEntityId = bestPrey.Id;
                CurrentGoal = Goal.HuntAnimal;
                State = HumanState.Hunting;
                GoalDebug = $"HUNT {bestPrey.SpeciesName} d={bestD:F0}";
                GoalCooldown = 15;
                return true;
            }
            return false;
        }

        // ============ НАВИГАЦИЯ ============

        void Navigate(double dt, WorldContext world)
        {
            if (!HasTarget) return;
            if (CurrentGoal == Goal.Rest || CurrentGoal == Goal.Sleep ||
                CurrentGoal == Goal.Build || CurrentGoal == Goal.Craft ||
                CurrentGoal == Goal.Heal || CurrentGoal == Goal.Cook)
            { Vx *= 0.92; Vy *= 0.92; return; }

            double dx = TargetX - X, dy = TargetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 5) return;

            // Обновить цель если преследуем движущийся объект
            if (TargetEntityId >= 0 && (CurrentGoal == Goal.HuntAnimal ||
                CurrentGoal == Goal.TameAnimal || CurrentGoal == Goal.DefendTerritory))
            {
                var targetA = world.World.Animals.FirstOrDefault(a => a.Id == TargetEntityId && a.IsAlive);
                if (targetA != null)
                { TargetX = targetA.X; TargetY = targetA.Y; }
                else { CurrentGoal = Goal.None; return; }
            }
            else if (TargetEntityId >= 0 && (CurrentGoal == Goal.Mate ||
                CurrentGoal == Goal.Socialize || CurrentGoal == Goal.Teach))
            {
                var targetH = world.World.Humans.FirstOrDefault(h => h.Id == TargetEntityId && h.IsAlive);
                if (targetH != null)
                { TargetX = targetH.X; TargetY = targetH.Y; }
                else { CurrentGoal = Goal.None; return; }
            }

            double tA = Math.Atan2(dy, dx);
            double diff = NA(tA - Angle);

            double tr = TURN_RATE;
            if (CurrentGoal == Goal.Flee) tr = 0.25;
            Angle += diff * tr;
            Angle = NA(Angle);

            double speed = Genes.EffMaxSpeed;
            bool run = false;

            switch (CurrentGoal)
            {
                case Goal.Flee: speed *= SPEED_RUN * 1.3; run = true; break;
                case Goal.FindFood when Hunger > 0.5: speed *= SPEED_RUN; run = true; break;
                case Goal.HuntAnimal: speed *= SPEED_RUN * 0.9; run = true; break;
                case Goal.DefendTerritory: speed *= SPEED_RUN; run = true; break;
                default: speed *= SPEED_WALK; break;
            }

            if (Fatigue > 0.7) speed *= 0.4;
            if (IsChild) speed *= 0.7;
            if (dist < 60 && CurrentGoal != Goal.Flee) speed *= Math.Max(0.3, dist / 60);

            if (Math.Abs(diff) < Math.PI * 0.5)
            { Vx += Math.Cos(Angle) * speed * dt; Vy += Math.Sin(Angle) * speed * dt; }

            State = run ? HumanState.Running : HumanState.Walking;
        }

        // ============ ДЕЙСТВИЯ ============

        void Act(double dt, WorldContext world)
        {
            switch (CurrentGoal)
            {
                case Goal.FindFood:
                case Goal.EatFood:
                    var food = world.TryGetFood(X, Y, 65);
                    if (food != null)
                    {
                        Eat(food.Value);
                        SkillGathering = Math.Min(1, SkillGathering + 0.008);
                        Brain.Reward(0.5);
                        FoodEaten++;
                        State = HumanState.Eating;
                        GoalDebug = $"EATING h={Hunger:F2}";
                        GoalCooldown = 5;
                        if (Hunger < 0.08) { CurrentGoal = Goal.None; GoalCooldown = 8; }
                        else TryFindFood(world);
                    }
                    break;

                case Goal.HuntAnimal:
                    var prey = world.World.Animals.FirstOrDefault(a =>
                        a.Id == TargetEntityId && a.IsAlive);
                    if (prey != null && Dist(X, Y, prey.X, prey.Y) < 45)
                    {
                        double pow = Genes.PhysicalStrength * (1 + SkillFighting * 0.5);
                        if (EquippedWeapon != null) pow *= EquippedWeapon.Modifier;
                        prey.TakeDamage(pow * 0.7, "Hunter");
                        SkillHunting = Math.Min(1, SkillHunting + 0.01);
                        SkillFighting = Math.Min(1, SkillFighting + 0.005);
                        State = HumanState.Fighting;

                        if (!prey.IsAlive)
                        {
                            double meat = prey.Genes.Size * 20;
                            Eat(meat * 0.4); // Съесть часть
                            MeatStored += meat * 0.6; // Остальное в запас
                            MeatStored = Math.Min(50, MeatStored);
                            Brain.Reward(0.9);
                            AnimalsKilled++; FoodEaten++;
                            GoalDebug = $"KILLED {prey.SpeciesName}! +{meat * 0.6:F0} meat";
                            Reputation += 0.1;
                            CurrentGoal = Goal.None;
                            GoalCooldown = 12;
                            TargetEntityId = -1;

                            // Научить племя: этот вид съедобен
                        }
                    }
                    else if (prey == null)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.DefendTerritory:
                    var enemy = world.World.Animals.FirstOrDefault(a =>
                        a.Id == TargetEntityId && a.IsAlive);
                    if (enemy != null && Dist(X, Y, enemy.X, enemy.Y) < 45)
                    {
                        double pow = Genes.PhysicalStrength * (1 + SkillFighting * 0.5);
                        enemy.TakeDamage(pow * 0.6, "Defender");
                        SkillFighting = Math.Min(1, SkillFighting + 0.008);
                        State = HumanState.Fighting;
                        Brain.Fear(0.1);

                        if (!enemy.IsAlive)
                        {
                            Brain.Reward(0.7);
                            Reputation += 0.15;
                            BattlesWon++;
                            GoalDebug = $"DEFENDED vs {enemy.SpeciesName}!";
                            KnownDangerousSpecies.Add(enemy.SpeciesName);
                            CurrentGoal = Goal.None;
                            TargetEntityId = -1;
                        }
                    }
                    else if (enemy == null)
                    { CurrentGoal = Goal.None; TargetEntityId = -1; }
                    break;

                case Goal.Mate:
                    var mp = world.World.Humans.FirstOrDefault(h =>
                        h.Id == TargetEntityId && h.IsAlive);
                    if (mp != null && mp.IsAdult && !mp.IsPregnant &&
                        mp.Sex != Sex && Dist(X, Y, mp.X, mp.Y) < 40)
                    {
                        Human mother = Sex == Gender.Female ? this : mp;
                        Human father = Sex == Gender.Female ? mp : this;
                        if (!mother.IsPregnant)
                        {
                            mother.IsPregnant = true;
                            mother.PregnancyPartner = father;
                            Brain.Reward(0.9); mp.Brain.Reward(0.9);
                            Brain.Social(0.6); mp.Brain.Social(0.6);
                            ImproveRelationship(mp.Id, 0.4, RelationType.Mate);
                            mp.ImproveRelationship(Id, 0.4, RelationType.Mate);
                            GoalDebug = "MATED!";
                        }
                        CurrentGoal = Goal.None;
                        GoalCooldown = 20;
                        mp.MateCooldown = 80;
                        TargetEntityId = -1;
                    }
                    else if (mp == null || !mp.IsAlive)
                    { CurrentGoal = Goal.None; TargetEntityId = -1; }
                    break;

                case Goal.Socialize:
                    SocialTimer += dt;
                    var sp = world.World.Humans.FirstOrDefault(h =>
                        h.Id == TargetEntityId && h.IsAlive);
                    if (sp != null && Dist(X, Y, sp.X, sp.Y) < 50)
                    {
                        State = HumanState.Socializing;
                        Vx *= 0.88; Vy *= 0.88;
                        if (SocialTimer > 5)
                        {
                            Teach(sp);
                            SkillSpeaking = Math.Min(1, SkillSpeaking + 0.005);
                            Brain.Social(0.15); sp.Brain.Social(0.15);
                            ImproveRelationship(sp.Id, 0.04, RelationType.Friend);
                            sp.ImproveRelationship(Id, 0.04, RelationType.Friend);

                            // Делиться знаниями об опасных животных
                            foreach (var s in KnownDangerousSpecies)
                                sp.KnownDangerousSpecies.Add(s);
                            foreach (var s in sp.KnownDangerousSpecies)
                                KnownDangerousSpecies.Add(s);

                            if (Hunger < 0.15 && sp.Hunger > 0.3 && MeatStored > 3)
                            {
                                sp.Eat(5); MeatStored -= 3;
                                GoalDebug = $"SHARED meat→{sp.Name}";
                            }
                            else GoalDebug = $"CHAT {sp.Name}";
                            SocialTimer = 0; LessonsGiven++;
                        }
                        if (GoalTimer <= 0)
                        { CurrentGoal = Goal.None; GoalCooldown = 20; TargetEntityId = -1; }
                    }
                    else if (sp == null) { CurrentGoal = Goal.None; TargetEntityId = -1; }
                    break;

                case Goal.Teach:
                    var student = world.World.Humans.FirstOrDefault(h =>
                        h.Id == TargetEntityId && h.IsAlive);
                    if (student != null && Dist(X, Y, student.X, student.Y) < 50)
                    {
                        State = HumanState.Teaching;
                        Vx *= 0.88; Vy *= 0.88;
                        TeachPerson(student);
                        SkillTeaching = Math.Min(1, SkillTeaching + 0.008);
                        Brain.Social(0.2);
                        GoalDebug = $"TEACHING {student.Name}";
                        LessonsGiven++;
                        if (GoalTimer <= 0)
                        { CurrentGoal = Goal.None; GoalCooldown = 20; TargetEntityId = -1; }
                    }
                    else if (student == null) { CurrentGoal = Goal.None; TargetEntityId = -1; }
                    break;

                case Goal.TameAnimal:
                    var wild = world.World.Animals.FirstOrDefault(a =>
                        a.Id == TargetEntityId && a.IsAlive);
                    if (wild != null && Dist(X, Y, wild.X, wild.Y) < 40)
                    {
                        State = HumanState.Gathering;
                        Vx *= 0.85; Vy *= 0.85;
                        if (MeatStored > 2) { wild.Feed(3, Id); MeatStored -= 2; }
                        bool tamed = wild.TryTame(this, SkillHunting);
                        if (tamed)
                        {
                            Brain.Reward(1.0);
                            Reputation += 0.3;
                            GoalDebug = $"TAMED {wild.SpeciesName}!";
                            CurrentGoal = Goal.None;
                            TargetEntityId = -1;
                        }
                    }
                    else if (wild == null) { CurrentGoal = Goal.None; TargetEntityId = -1; }
                    break;

                case Goal.Build:
                    if (GoalTimer <= 0)
                    {
                        if (world.TryBuild(X, Y, this))
                        { SkillBuilding = Math.Min(1, SkillBuilding + 0.015); Brain.Reward(0.5); BuildingsBuilt++; GoalDebug = "BUILT!"; }
                        CurrentGoal = Goal.None; GoalCooldown = 15;
                    }
                    break;

                case Goal.Craft:
                    if (GoalTimer <= 0)
                    {
                        var item = TryCraft();
                        if (item != null)
                        { Inventory.Add(item); SkillCrafting = Math.Min(1, SkillCrafting + 0.015); Brain.Reward(0.4); ToolsMade++; GoalDebug = "CRAFTED!"; }
                        CurrentGoal = Goal.None; GoalCooldown = 15;
                    }
                    break;

                case Goal.GatherStone:
                case Goal.GatherWood:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 40)
                    {
                        State = HumanState.Gathering;
                        SkillGathering = Math.Min(1, SkillGathering + 0.005);
                        Brain.Reward(0.2);
                        GoalDebug = CurrentGoal == Goal.GatherStone ? "GOT stone" : "GOT wood";
                        CurrentGoal = Goal.None; GoalCooldown = 15;
                    }
                    break;

                case Goal.Heal:
                    Health = Math.Min(MaxHealth, Health + dt * (1 + SkillMedicine) * 0.8);
                    SkillMedicine = Math.Min(1, SkillMedicine + 0.003);
                    Vx *= 0.9; Vy *= 0.9;
                    if (Health > MaxHealth * 0.6)
                    { CurrentGoal = Goal.None; GoalCooldown = 10; }
                    break;

                case Goal.Rest:
                    Vx *= 0.92; Vy *= 0.92;
                    Health = Math.Min(MaxHealth, Health + dt * Genes.EffResilience * 0.5);
                    Fatigue = Math.Max(0, Fatigue - dt * 0.006);
                    State = HumanState.Resting;
                    if (Fatigue < 0.15 && Health > MaxHealth * 0.6)
                    { CurrentGoal = Goal.None; GoalCooldown = 8; }
                    break;

                case Goal.Sleep:
                    Vx *= 0.92; Vy *= 0.92;
                    Health = Math.Min(MaxHealth, Health + dt * Genes.EffResilience * 0.3);
                    Fatigue = Math.Max(0, Fatigue - dt * 0.008);
                    State = HumanState.Sleeping;
                    if (!Brain.IsSleeping) { CurrentGoal = Goal.None; GoalCooldown = 8; }
                    break;

                case Goal.Flee:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 50)
                    { CurrentGoal = Goal.None; GoalCooldown = 8; }
                    break;

                case Goal.FollowLeader:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 60)
                    {
                        var ld = world.GetTribalLeader(TribeId);
                        if (ld != null) SetTarget(ld.X, ld.Y);
                        else CurrentGoal = Goal.None;
                    }
                    if (GoalTimer <= 0) CurrentGoal = Goal.None;
                    break;

                case Goal.GuardCamp:
                    Vx *= 0.95; Vy *= 0.95;
                    if (GoalTimer <= 0) CurrentGoal = Goal.None;
                    // Если опасность рядом — переключиться на DefendTerritory
                    var g = FindDangerousAnimal(world);
                    if (g != null && Dist(X, Y, g.X, g.Y) < 150)
                    {
                        SetTarget(g.X, g.Y);
                        TargetEntityId = g.Id;
                        CurrentGoal = Goal.DefendTerritory;
                        GoalDebug = $"GUARD→DEFEND vs {g.SpeciesName}!";
                    }
                    break;

                case Goal.Explore:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 40)
                    { CurrentGoal = Goal.None; GoalCooldown = 5; }
                    // Попутная еда
                    if (Hunger > 0.05)
                    {
                        var nf = world.TryGetFood(X, Y, 35);
                        if (nf != null) { Eat(nf.Value); FoodEaten++; Brain.Reward(0.2); }
                    }
                    break;
            }
        }

        // ============ ПОТРЕБНОСТИ ============

        void UpdateNeeds(double dt, WorldContext world)
        {
            // FIX: Голод растёт ЗНАЧИТЕЛЬНО быстрее!
            // При SPEED_WALK человек голодает за ~600 тиков (20 сек при 30fps)
            double hr = dt * 0.0015;
            if (State == HumanState.Running) hr *= 2.0;
            if (State == HumanState.Fighting) hr *= 2.5;
            if (IsPregnant) hr *= 1.3;
            if (IsChild) hr *= 0.8;
            Hunger = Math.Min(1, Hunger + hr);

            Thirst = Math.Min(1, Thirst + dt * 0.0008);

            double fr = dt * 0.0005;
            if (State == HumanState.Running) fr *= 2;
            if (State == HumanState.Fighting) fr *= 3;
            if (State == HumanState.Resting || State == HumanState.Sleeping) fr = -dt * 0.006;
            Fatigue = Math.Clamp(Fatigue + fr, 0, 1);

            if (Hunger > 0.8) Energy = Math.Max(0, Energy - dt * 0.3);
            else if (Hunger < 0.3) Energy = Math.Min(MaxEnergy, Energy + dt * 0.15);

            double tT = 0.5;
            if (world.Temperature < 0.3) tT -= (0.3 - world.Temperature) * (1 - Genes.EffThermoTolerance) * 0.15;
            if (world.Temperature > 0.7) tT += (world.Temperature - 0.7) * (1 - Genes.EffThermoTolerance) * 0.15;
            Temperature += (tT - Temperature) * dt * 0.02;

            if (Temperature < 0.08 || Temperature > 0.92)
                Health -= dt * 0.12 * (1 - Genes.EffThermoTolerance);

            Pain = Math.Max(0, Pain - dt * 0.03);
            if (Hunger > 0.95) Health -= dt * 0.15;
            MeatStored = Math.Max(0, MeatStored - dt * 0.0003);
        }

        void UpdateEpiCtx(WorldContext w)
        {
            epiCtx.Stress = Brain.Cortisol; epiCtx.Hunger = Hunger;
            epiCtx.Trauma = Brain.TraumaLevel; epiCtx.Temperature = w.Temperature;
            epiCtx.NutritionQuality = 1 - Hunger; epiCtx.SocialSupport = 0;
            foreach (var r in Relationships.Values)
                if (r.Type == RelationType.Friend || r.Type == RelationType.Family)
                    epiCtx.SocialSupport = Math.Min(1, epiCtx.SocialSupport + r.Strength * 0.3);
            epiCtx.LearningIntensity = Brain.Acetylcholine;
            epiCtx.PhysicalActivity = State == HumanState.Running ? 0.8 : 0.2;
        }

        void Physics(double dt, WorldContext world)
        {
            X += Vx * dt; Y += Vy * dt;
            double m = 30;
            if (X < m) { X = m + 5; Vx = Math.Abs(Vx) * 0.2; SetExplore(world.Width, world.Height); }
            if (X > world.Width - m) { X = world.Width - m - 5; Vx = -Math.Abs(Vx) * 0.2; SetExplore(world.Width, world.Height); }
            if (Y < m) { Y = m + 5; Vy = Math.Abs(Vy) * 0.2; SetExplore(world.Width, world.Height); }
            if (Y > world.Height - m) { Y = world.Height - m - 5; Vy = -Math.Abs(Vy) * 0.2; SetExplore(world.Width, world.Height); }
            Vx *= FRICTION; Vy *= FRICTION;
            TotalDistanceTraveled += Math.Sqrt(Vx * Vx + Vy * Vy) * dt;
        }

        // ============ УТИЛИТЫ ============

        public void Eat(double n)
        { Hunger = Math.Max(0, Hunger - n * 0.22); Energy = Math.Min(MaxEnergy, Energy + n * 6); Health = Math.Min(MaxHealth, Health + n * 0.5); }
        public void Drink(double a) => Thirst = Math.Max(0, Thirst - a);

        public void TakeDamage(double dmg, string src)
        {
            Health -= dmg * (1 - Genes.EffPainTolerance * 0.3);
            Pain = Math.Min(1, Pain + dmg / 50);
            Brain.Pain(dmg / 50); Brain.Fear(0.3);
            if (dmg > 30) Brain.TraumaticEvent(src);
            if (Health <= 0) Die(src);
        }

        public void Die(string c)
        { if (!IsAlive) return; IsAlive = false; CauseOfDeath = c; State = HumanState.Dead; }

        public void TeachPerson(Human s)
        {
            if (s == null || !s.IsAlive) return;
            double eff = (0.1 + SkillTeaching) * Genes.EffIntelligence * s.Genes.LearningSpeed * 0.15;
            foreach (var t in KnownTechnologies)
                if (!s.KnownTechnologies.Contains(t) && R.NextDouble() < eff)
                    s.KnownTechnologies.Add(t);
            // Передать навыки
            if (SkillHunting > s.SkillHunting) s.SkillHunting = Math.Min(1, s.SkillHunting + eff * 0.1);
            if (SkillBuilding > s.SkillBuilding) s.SkillBuilding = Math.Min(1, s.SkillBuilding + eff * 0.1);
            if (SkillCrafting > s.SkillCrafting) s.SkillCrafting = Math.Min(1, s.SkillCrafting + eff * 0.1);
            if (SkillFighting > s.SkillFighting) s.SkillFighting = Math.Min(1, s.SkillFighting + eff * 0.08);
            // Передать знания об опасных видах
            foreach (var sp in KnownDangerousSpecies) s.KnownDangerousSpecies.Add(sp);
            s.Brain.Social(0.1);
        }

        public void Teach(Human s) => TeachPerson(s);

        Item TryCraft()
        {
            if (Genes.EffCreativity < 0.3 || !KnownTechnologies.Contains("stone_tools")) return null;
            if (R.NextDouble() > (0.1 + SkillCrafting) * Genes.EffCreativity * 0.5) return null;
            return new Item
            {
                Type = R.NextDouble() < 0.5 ? ItemType.Tool : ItemType.Weapon,
                Name = "Stone tool",
                Modifier = 1.3 + SkillCrafting * 0.5,
                Durability = 50
            };
        }

        public void ImproveRelationship(int id, double a, RelationType t)
        { if (!Relationships.ContainsKey(id)) Relationships[id] = new Relationship { TargetId = id }; Relationships[id].Strength = Math.Min(1, Relationships[id].Strength + a); Relationships[id].Type = t; }
        public double GetRelationStrength(int id) => Relationships.ContainsKey(id) ? Relationships[id].Strength : 0;
        public double GetGeneticDistance(Human o) { double d = 0; for (int i = 0; i < 4; i++) d += Math.Abs(Genes.Species[i] - o.Genes.Species[i]); return d / 4; }

        public static Human CreateChild(Human m, Human f, double x, double y)
        {
            var cg = Genome.Cross(m.Genes, f.Genes); cg.Mutate(0.15, 0.1);
            var cb = new FastBrain(); cb.TransferKnowledge(m.Brain, 0.1); cb.TransferKnowledge(f.Brain, 0.1); cb.Mutate(0.2, 0.15);
            var child = new Human(x, y, cg, cb) { Age = 0, TribeId = m.TribeId };
            child.Health = child.MaxHealth * 0.8; child.Energy = child.MaxEnergy * 0.5;
            foreach (var t in m.KnownTechnologies) if (R.NextDouble() < 0.3) child.KnownTechnologies.Add(t);
            foreach (var s in m.KnownDangerousSpecies) child.KnownDangerousSpecies.Add(s);
            child.ImproveRelationship(m.Id, 0.9, RelationType.Family); m.ImproveRelationship(child.Id, 0.9, RelationType.Family);
            if (f.IsAlive) { child.ImproveRelationship(f.Id, 0.7, RelationType.Family); f.ImproveRelationship(child.Id, 0.7, RelationType.Family); }
            return child;
        }

        public double CalculateFitness() => (Health / MaxHealth) * 20 + (1 - Hunger) * 15 + Age * 0.5 + ChildrenBorn * 30 + FoodEaten * 0.5 + AnimalsKilled * 5 + Reputation * 10 + MeatStored * 0.3 + (SkillHunting + SkillBuilding + SkillCrafting + SkillFighting + SkillSpeaking + SkillTeaching) * 5;
        public float DrawSize => (float)(8 + Genes.EffBodySize * 4);
        public Color GetColor() { if (!IsAlive) return Color.Gray; if (IsChild) return Color.FromArgb(200, 220, 255); if (IsLeader) return Color.Gold; return Genes.Skin; }
    }

    public enum Gender { Male, Female }
    public enum HumanState { Idle, Walking, Running, Eating, Sleeping, Resting, Fighting, Hunting, Gathering, Building, Crafting, Socializing, Teaching, Trading, Fleeing, Dead }
    public class Relationship { public int TargetId; public RelationType Type; public double Strength; public double LastInteraction; }
    public enum RelationType { Neutral, Friend, Family, Mate, Rival, Enemy }
    public class Item { public string Name; public ItemType Type; public double Modifier; public double Durability; public void Use() => Durability -= 1; public bool IsBroken => Durability <= 0; }
    public enum ItemType { Food, Tool, Weapon, Shield, Material, Medicine, Clothing }
}