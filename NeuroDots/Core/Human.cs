using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;

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
        private static int _nextId;

        public static void ResetIds() => _nextId = 0;

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
        public bool IsAdult => Age >= 15 && Age < 58;
        public bool IsElder => Age >= 58;
        public bool IsPregnant;
        public double PregnancyProgress;
        public Human? PregnancyPartner;

        public Dictionary<int, Relationship> Relationships = new();
        public int TribeId = -1;
        public bool IsLeader;
        public double Reputation, Trust;

        public double SkillHunting, SkillGathering, SkillBuilding;
        public double SkillCrafting, SkillFighting, SkillCooking;
        public double SkillMedicine, SkillSpeaking, SkillTeaching, SkillLeading;

        public List<Item> Inventory = new();
        public Item? EquippedTool, EquippedWeapon;

        public HashSet<string> KnownTechnologies = new();
        public List<int> KnownWords = new();
        private readonly HashSet<int> _knownWordsSet = new();
        public int VocabularySize => KnownWords.Count;

        // Кэш бонусов технологий (обновляется только при изучении новой технологии)
        public TechBonus CombinedTechBonus = new();

        public HashSet<string> KnownDangerousSpecies = new();
        public double MeatStored;
        public double WoodStored;
        public double StoneStored;

        public HumanState State = HumanState.Idle;

        public int FoodEaten, ChildrenBorn, ToolsMade;
        public int BuildingsBuilt, BattlesWon, LessonsGiven, AnimalsKilled;
        public double TotalDistanceTraveled;
        public string CauseOfDeath = "";

        public Goal CurrentGoal = Goal.None;
        public double TargetX, TargetY;
        public bool HasTarget;
        public double GoalTimer;
        public double GoalCooldown;
        public string _goalDebug = "";

        /// <summary>Ring buffer of recent decisions — powers the OBSERVE panel (pause + click agent).</summary>
        public readonly List<string> MindLog = new();

        /// <summary>Chronicle-visible change of current intent; logged for the observe panel.</summary>
        public string GoalDebug
        {
            get => _goalDebug;
            set
            {
                if (string.IsNullOrEmpty(value) || value == _goalDebug) return;
                _goalDebug = value;
                MindLog.Add(value);
                if (MindLog.Count > 10) MindLog.RemoveAt(0);
            }
        }

        /// <summary>Tribe this warrior is raiding (set by diplomacy system during wars), -1 = none.</summary>
        public int RaidTargetTribeId = -1;
        public double SocialTimer;
        public int TargetEntityId = -1;

        public double NeedSocial => Math.Max(0, 1.0 - Brain.Oxytocin * 2.5 - Math.Min(Relationships.Count, 8) * 0.06);

        private readonly EpigeneticContext _epiCtx = new();
        private readonly double[] _sensors = new double[FastBrain.INPUTS];
        private static Random R => Random.Shared;

        private const double SPEED_WALK = 0.0062;
        private const double SPEED_RUN = 0.0130;
        private const double TURN_RATE = 0.13;
        private const double FRICTION = 0.88;

        public Human(double x, double y, Genome? genes = null, FastBrain? brain = null)
        {
            Id = _nextId++;
            X = x;
            Y = y;
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

        private static string GenName()
        {
            string[] sy = { "ka", "ra", "ma", "to", "ni", "ba", "lu",
                "ke", "so", "da", "no", "te", "wa", "mi", "ru", "ha",
                "pe", "li", "ko", "sa", "di", "na", "bo", "ze", "fi" };
            int len = 2 + R.Next(2);
            string n = "";
            for (int i = 0; i < len; i++) n += sy[R.Next(sy.Length)];
            return char.ToUpper(n[0]) + n[1..];
        }

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

        public bool KnowsWord(int code) => _knownWordsSet.Contains(code);

        public void LearnWord(int code)
        {
            if (_knownWordsSet.Add(code))
                KnownWords.Add(code);
        }

        public void LearnTechnology(string techId)
        {
            if (KnownTechnologies.Add(techId))
                RecalculateTechBonuses();
        }

        public void RecalculateTechBonuses()
        {
            var b = new TechBonus();
            foreach (var t in KnownTechnologies)
            {
                var tb = TechnologyTree.GetBonus(t);
                b.WarmthBonus += tb.WarmthBonus;
                b.PredatorDefense += tb.PredatorDefense;
                b.GatheringBonus += tb.GatheringBonus;
                b.CraftingBonus += tb.CraftingBonus;
                b.FoodEfficiency += tb.FoodEfficiency;
                b.HuntingBonus += tb.HuntingBonus;
                b.AttackBonus += tb.AttackBonus;
                b.ShelterQuality += tb.ShelterQuality;
                b.HealingBonus += tb.HealingBonus;
                b.DiseaseResistance += tb.DiseaseResistance;
                b.FoodProduction += tb.FoodProduction;
                b.FoodStorage += tb.FoodStorage;
                b.MaterialBonus += tb.MaterialBonus;
                b.TradeBonus += tb.TradeBonus;
                b.Sedentism += tb.Sedentism;
            }
            CombinedTechBonus = b;
        }

        private void SetExplore(double ww, double wh)
        {
            // Если есть пространственная память о пище и голоден — вспомнить место
            if (Hunger > 0.25 && Brain.TryRecallEpisode(EpisodeType.FoundFood, out var ep) && R.NextDouble() < 0.45)
            {
                TargetX = Math.Clamp(ep.X + (R.NextDouble() - 0.5) * 80, 80, ww - 80);
                TargetY = Math.Clamp(ep.Y + (R.NextDouble() - 0.5) * 80, 80, wh - 80);
                HasTarget = true;
                GoalTimer = 100 + R.NextDouble() * 140;
                return;
            }

            double a = R.NextDouble() * Math.PI * 2;
            double d = 180 + R.NextDouble() * 380;
            TargetX = Math.Clamp(X + Math.Cos(a) * d, 80, ww - 80);
            TargetY = Math.Clamp(Y + Math.Sin(a) * d, 80, wh - 80);
            HasTarget = true;
            GoalTimer = 110 + R.NextDouble() * 180;
        }

        private void SetTarget(double tx, double ty)
        {
            TargetX = tx;
            TargetY = ty;
            HasTarget = true;
        }

        public void Update(double dt, WorldContext world)
        {
            if (!IsAlive) return;

            Age += dt * AgeSpeed * 0.004;
            Genes.EpigeneticAge = Age;
            if (Age > Genes.MaxAge) { Die("Old age"); return; }

            UpdateNeeds(dt, world);

            // Эпигенетика и пространственная карта — раз в 10 тиков
            if ((Id + world.World.Tick) % 10 == 0)
            {
                UpdateEpiCtx(world);
                if (IsChild) Genes.CriticalPeriodUpdate(dt * 10, _epiCtx);
                else Genes.UpdateEpigenetics(dt * 10, _epiCtx);
                Brain.UpdateSpatialMap(X, Y, world.Width, world.Height);
            }

            // Когнитивный проход нейросети FastBrain (раз в 2 тика для высокой производительности)
            if ((Id + world.World.Tick) % 2 == 0)
            {
                UpdateSensorsAndBrain(dt * 2, world);
            }

            Brain.UpdateCircadian(world.DayLight);

            Happiness = Math.Clamp(
                (1.0 - Hunger) * 0.25 + (Health / MaxHealth) * 0.20 +
                (1.0 - Fatigue) * 0.10 + Brain.Serotonin * 0.20 +
                Brain.Oxytocin * 0.15 + Brain.Dopamine * 0.10, 0.0, 1.0);

            GoalCooldown = Math.Max(0, GoalCooldown - dt);
            GoalTimer -= dt;
            MateCooldown = Math.Max(0, MateCooldown - dt);

            if (GoalCooldown <= 0) SelectGoal(dt, world);
            Navigate(dt, world);
            Act(dt, world);
            Physics(dt, world);

            if (IsPregnant) PregnancyProgress += dt * 0.0055;

            if (Health <= 0) Die("Injuries");
            else if (Hunger >= 1.0 && Energy <= 0) Die("Starvation");
        }

        private void UpdateSensorsAndBrain(double dt, WorldContext world)
        {
            double vr = Math.Max(1.0, Genes.EffVisionRange);
            var (fA, fD) = world.NearestFood(X, Y, vr);
            var (hA, hD, _) = world.NearestHuman(X, Y, Id, vr);
            var (aA, aD, isPred) = world.NearestAnimal(X, Y, vr);

            _sensors[0] = Hunger;
            _sensors[1] = Health / MaxHealth;
            _sensors[2] = Fatigue;
            _sensors[3] = Energy / MaxEnergy;
            _sensors[4] = NA(fA - Angle) / Math.PI;
            _sensors[5] = Math.Clamp(fD / vr, 0, 1);
            _sensors[6] = NA(hA - Angle) / Math.PI;
            _sensors[7] = Math.Clamp(hD / vr, 0, 1);
            _sensors[8] = NA(aA - Angle) / Math.PI;
            _sensors[9] = Math.Clamp(aD / vr, 0, 1);
            _sensors[10] = isPred ? 1.0 : 0.0;
            _sensors[11] = world.DayLight;
            _sensors[12] = world.Temperature;
            _sensors[13] = Pain;
            _sensors[14] = NeedSocial;
            _sensors[15] = IsPregnant ? 1.0 : 0.0;
            _sensors[16] = Math.Min(1.0, MeatStored / 30.0);
            _sensors[17] = EquippedWeapon != null ? 1.0 : 0.0;
            _sensors[18] = EquippedTool != null ? 1.0 : 0.0;
            _sensors[19] = Brain.Cortisol;
            _sensors[20] = Brain.Dopamine;
            _sensors[21] = Brain.Oxytocin;
            _sensors[22] = Math.Clamp(X / world.Width, 0, 1);
            _sensors[23] = Math.Clamp(Y / world.Height, 0, 1);

            Brain.ThinkWithInstincts(_sensors, Hunger, Health, MaxHealth, Fatigue, Pain, X, Y, world.Width, world.Height, dt);
        }

        private void SelectGoal(double dt, WorldContext world)
        {
            var brainOut = Brain.LastOutput;

            // ═══ УРОВЕНЬ 1: НЕМЕДЛЕННОЕ ВЫЖИВАНИЕ ═══

            Animal? nearDanger = FindDangerousAnimal(world);
            if (nearDanger != null)
            {
                double pd = Dist(X, Y, nearDanger.X, nearDanger.Y);
                bool canFight = IsAdult && Health > MaxHealth * 0.65 &&
                                (EquippedWeapon != null || SkillFighting > 0.35 || Genes.EffAggression > 0.55) &&
                                world.CountNearbyHumans(X, Y, 150, TribeId, true) >= 2;

                if (pd < 145 && !canFight)
                {
                    double fa = Math.Atan2(nearDanger.Y - Y, nearDanger.X - X) + Math.PI;
                    SetTarget(
                        Math.Clamp(X + Math.Cos(fa) * 300, 80, world.Width - 80),
                        Math.Clamp(Y + Math.Sin(fa) * 300, 80, world.Height - 80));
                    CurrentGoal = Goal.Flee;
                    State = HumanState.Fleeing;
                    GoalDebug = $"FLEE {nearDanger.SpeciesName} d={pd:F0}!";
                    GoalCooldown = 14;
                    Brain.Fear(0.4);
                    Brain.RememberEpisode(nearDanger.X, nearDanger.Y, EpisodeType.Danger, 0.9);
                    KnownDangerousSpecies.Add(nearDanger.SpeciesName);
                    world.EmitSound(X, Y, (int)Concept.Danger, 220, Id);
                    return;
                }
                else if (pd < 180 && canFight)
                {
                    SetTarget(nearDanger.X, nearDanger.Y);
                    TargetEntityId = nearDanger.Id;
                    CurrentGoal = Goal.DefendTerritory;
                    State = HumanState.Running;
                    GoalDebug = $"DEFEND vs {nearDanger.SpeciesName}";
                    GoalCooldown = 16;
                    return;
                }
            }

            // Голод → съесть запас мяса или приготовить на огне
            if (Hunger > 0.42 && MeatStored >= 2.0)
            {
                if (KnownTechnologies.Contains("cooking") && Hunger < 0.78)
                {
                    CurrentGoal = Goal.Cook;
                    HasTarget = false;
                    State = HumanState.Eating;
                    GoalDebug = "COOKING meat";
                    GoalCooldown = 18;
                    GoalTimer = 6;
                    return;
                }
                double eat = Math.Min(MeatStored, 12.0);
                MeatStored -= eat;
                Eat(eat * (1.0 + CombinedTechBonus.FoodEfficiency * 0.5));
                State = HumanState.Eating;
                GoalDebug = $"ATE meat ({MeatStored:F0} left)";
                GoalCooldown = 12;
                return;
            }

            if (Hunger > 0.38)
            {
                if (TryFindFood(world)) return;
            }

            if (Hunger > 0.28 && IsAdult)
            {
                if (TryFindHunt(world)) return;
            }

            if (Hunger > 0.15)
            {
                var (_, fd) = world.NearestFood(X, Y, 160);
                if (fd < 140 && TryFindFood(world)) return;
            }

            if (Fatigue > 0.75)
            {
                CurrentGoal = Goal.Rest;
                HasTarget = false;
                State = HumanState.Resting;
                GoalDebug = "REST(exhausted)";
                GoalCooldown = 22;
                return;
            }

            if (Health < MaxHealth * 0.30)
            {
                CurrentGoal = KnownTechnologies.Contains("medicine_herbal") ? Goal.Heal : Goal.Rest;
                HasTarget = false;
                State = HumanState.Resting;
                GoalDebug = CurrentGoal == Goal.Heal ? "HEALING" : "REST(wounded)";
                GoalCooldown = 22;
                return;
            }

            if (Brain.IsSleeping)
            {
                var shelter = FindNearestBuilding(world, 240);
                if (shelter != null) SetTarget(shelter.X, shelter.Y);
                else HasTarget = false;
                CurrentGoal = Goal.Sleep;
                State = HumanState.Sleeping;
                GoalDebug = shelter != null ? "SLEEP→shelter" : "SLEEP";
                GoalCooldown = 15;
                return;
            }

            // ═══ УРОВЕНЬ 2: ПРОАКТИВНАЯ ДОБЫЧА И ЗАЩИТА ═══

            if (Hunger > 0.20)
            {
                if (TryFindFood(world)) return;
            }

            if (Fatigue > 0.52 || brainOut[6] > 0.65)
            {
                CurrentGoal = Goal.Rest;
                HasTarget = false;
                State = HumanState.Resting;
                GoalDebug = $"REST f={Fatigue:F2}";
                GoalCooldown = 16;
                return;
            }

            // Охота для запаса мяса (учитывает нейронный выход атаки brainOut[4])
            if (IsAdult && MeatStored < 30 && Hunger < 0.38 && Fatigue < 0.5 && Health > MaxHealth * 0.5)
            {
                double huntDesire = 0.16 + Math.Max(0, brainOut[4]) * 0.20 + SkillHunting * 0.15;
                if (R.NextDouble() < huntDesire && TryFindHunt(world))
                {
                    GoalDebug = $"HUNT(stock meat={MeatStored:F0})";
                    return;
                }
            }

            if (IsAdult && TribeId >= 0 && nearDanger != null)
            {
                double td = Dist(X, Y, nearDanger.X, nearDanger.Y);
                if (td < 260 && td > 70 && (SkillFighting > 0.08 || EquippedWeapon != null) && Health > MaxHealth * 0.5)
                {
                    SetTarget(nearDanger.X, nearDanger.Y);
                    TargetEntityId = nearDanger.Id;
                    CurrentGoal = Goal.DefendTerritory;
                    State = HumanState.Running;
                    GoalDebug = $"DEFEND vs {nearDanger.SpeciesName}";
                    GoalCooldown = 18;
                    return;
                }
            }

            // ═══ УРОВЕНЬ 3: РАЗМНОЖЕНИЕ ═══

            if (IsAdult && !IsPregnant && MateCooldown <= 0 &&
                Hunger < 0.42 && Health > MaxHealth * 0.45 && Fatigue < 0.55)
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
                    MateCooldown = 85;
                    return;
                }
            }

            // ═══ УРОВЕНЬ 4: РАЗВИТИЕ, КУЛЬТУРА И РЕМЕСЛО (модулируется нейросетью) ═══

            double roll = R.NextDouble();

            // Крафт оружия и инструментов, если их ещё нет или сломались
            bool needsGear = EquippedTool == null || EquippedWeapon == null;
            if ((needsGear || roll < 0.22 + Math.Max(0, brainOut[8]) * 0.15) &&
                IsAdult && KnownTechnologies.Contains("stone_tools") &&
                Hunger < 0.30 && ToolsMade < 5)
            {
                CurrentGoal = Goal.Craft;
                HasTarget = false;
                State = HumanState.Crafting;
                GoalDebug = needsGear ? "CRAFT gear" : "CRAFT tools";
                GoalCooldown = 20;
                GoalTimer = 12;
                return;
            }

            // Строительство домов, костров, складов
            if (roll < 0.38 + Math.Max(0, brainOut[7]) * 0.15 &&
                IsAdult && (KnownTechnologies.Contains("building") || KnownTechnologies.Contains("shelter_basic") || KnownTechnologies.Contains("fire_control")) &&
                Hunger < 0.28 && Fatigue < 0.45)
            {
                var nearBld = FindNearestBuilding(world, 95);
                if (nearBld == null)
                {
                    CurrentGoal = Goal.Build;
                    HasTarget = false;
                    State = HumanState.Building;
                    GoalDebug = "BUILD structure";
                    GoalCooldown = 30;
                    GoalTimer = 16;
                    return;
                }
            }

            // Обучение детей и соплеменников
            if (roll < 0.55 && IsAdult && (SkillTeaching > 0.02 || Genes.EffIntelligence > 0.55))
            {
                var child = world.FindNearbyChild(X, Y, Id, 220);
                if (child != null)
                {
                    SetTarget(child.X, child.Y);
                    TargetEntityId = child.Id;
                    CurrentGoal = Goal.Teach;
                    State = HumanState.Walking;
                    GoalDebug = $"TEACH→{child.Name}";
                    GoalCooldown = 22;
                    GoalTimer = 20;
                    return;
                }
            }

            // Межплеменная торговля и обмен
            if (roll < 0.63 && IsAdult && KnownTechnologies.Contains("trade_system") && MeatStored > 8)
            {
                var trader = world.FindNearbyTrader(X, Y, Id, TribeId, 280);
                if (trader != null)
                {
                    SetTarget(trader.X, trader.Y);
                    TargetEntityId = trader.Id;
                    CurrentGoal = Goal.Trade;
                    State = HumanState.Walking;
                    GoalDebug = $"TRADE→{trader.Name}";
                    GoalCooldown = 25;
                    GoalTimer = 25;
                    return;
                }
            }

            // Общение и протоязык
            if (roll < 0.76 && (NeedSocial > 0.12 || brainOut[9] > 0.3))
            {
                var p = world.GetNearestHuman(X, Y, Id, 200);
                if (p != null)
                {
                    SetTarget(p.X, p.Y);
                    TargetEntityId = p.Id;
                    CurrentGoal = Goal.Socialize;
                    State = HumanState.Walking;
                    GoalDebug = $"CHAT→{p.Name}";
                    GoalCooldown = 18;
                    GoalTimer = 14 + R.NextDouble() * 10;
                    SocialTimer = 0;
                    return;
                }
            }

            // Приручение животных
            if (roll < 0.85 && IsAdult && KnownTechnologies.Contains("animal_taming") &&
                Hunger < 0.25 && MeatStored > 3)
            {
                var tameable = world.FindNearbyTameableAnimal(X, Y, 170);
                if (tameable != null)
                {
                    SetTarget(tameable.X, tameable.Y);
                    TargetEntityId = tameable.Id;
                    CurrentGoal = Goal.TameAnimal;
                    State = HumanState.Walking;
                    GoalDebug = $"TAME {tameable.SpeciesName}";
                    GoalCooldown = 25;
                    return;
                }
            }

            // Сбор камня и дерева
            if (roll < 0.94 && IsAdult && Hunger < 0.25)
            {
                var mat = world.FindNearestMaterial(X, Y, 240);
                if (mat != null)
                {
                    SetTarget(mat.X, mat.Y);
                    CurrentGoal = mat.Type == ResourceType.Stone ? Goal.GatherStone : Goal.GatherWood;
                    State = HumanState.Walking;
                    GoalDebug = $"GATHER {mat.Type}";
                    GoalCooldown = 18;
                    return;
                }
            }

            // Следовать за лидером племени, если ушёл слишком далеко
            if (roll < 0.98 && TribeId >= 0 && !IsLeader)
            {
                var leader = world.GetTribalLeader(TribeId);
                if (leader != null && leader.Id != Id && Dist(X, Y, leader.X, leader.Y) > 160)
                {
                    SetTarget(leader.X, leader.Y);
                    CurrentGoal = Goal.FollowLeader;
                    State = HumanState.Walking;
                    GoalDebug = $"FOLLOW→{leader.Name}";
                    GoalCooldown = 25;
                    GoalTimer = 35;
                    return;
                }
            }

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

        private Animal? FindDangerousAnimal(WorldContext world) =>
            world.FindNearestDangerousAnimal(X, Y, 200, KnownDangerousSpecies);

        private Building? FindNearestBuilding(WorldContext world, double range) =>
            world.FindNearestBuilding(X, Y, TribeId, range);

        private Human? FindMate(WorldContext world) =>
            world.FindBestMate(this, 260);

        private bool TryFindFood(WorldContext world)
        {
            var (foodA, foodD) = world.NearestFood(X, Y, Genes.EffVisionRange);
            if (foodD < Genes.EffVisionRange * 0.95)
            {
                SetTarget(X + Math.Cos(foodA) * foodD, Y + Math.Sin(foodA) * foodD);
                CurrentGoal = foodD < 50 ? Goal.EatFood : Goal.FindFood;
                State = Hunger > 0.55 ? HumanState.Running : HumanState.Walking;
                GoalDebug = $"FOOD d={foodD:F0}";
                GoalCooldown = 10;
                return true;
            }
            return false;
        }

        private bool TryFindHunt(WorldContext world)
        {
            var bestPrey = world.FindNearestPrey(X, Y, Genes.EffVisionRange * 0.8);
            if (bestPrey != null)
            {
                double bestD = Dist(X, Y, bestPrey.X, bestPrey.Y);
                SetTarget(bestPrey.X, bestPrey.Y);
                TargetEntityId = bestPrey.Id;
                CurrentGoal = Goal.HuntAnimal;
                State = HumanState.Hunting;
                GoalDebug = $"HUNT {bestPrey.SpeciesName} d={bestD:F0}";
                GoalCooldown = 14;
                return true;
            }
            return false;
        }

        private void Navigate(double dt, WorldContext world)
        {
            if (!HasTarget) return;
            if (CurrentGoal == Goal.Rest || CurrentGoal == Goal.Sleep ||
                CurrentGoal == Goal.Build || CurrentGoal == Goal.Craft ||
                CurrentGoal == Goal.Heal || CurrentGoal == Goal.Cook)
            {
                Vx *= 0.90;
                Vy *= 0.90;
                return;
            }

            if (TargetEntityId >= 0)
            {
                if (CurrentGoal == Goal.HuntAnimal || CurrentGoal == Goal.TameAnimal || CurrentGoal == Goal.DefendTerritory)
                {
                    var targetA = world.World.GetAnimalById(TargetEntityId);
                    if (targetA != null && targetA.IsAlive)
                    {
                        TargetX = targetA.X;
                        TargetY = targetA.Y;
                    }
                    else
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                        return;
                    }
                }
                else if (CurrentGoal == Goal.Mate || CurrentGoal == Goal.Socialize || CurrentGoal == Goal.Teach || CurrentGoal == Goal.Trade)
                {
                    var targetH = world.World.GetHumanById(TargetEntityId);
                    if (targetH != null && targetH.IsAlive)
                    {
                        TargetX = targetH.X;
                        TargetY = targetH.Y;
                    }
                    else
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                        return;
                    }
                }
            }

            double dx = TargetX - X, dy = TargetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 5) return;

            double tA = Math.Atan2(dy, dx);
            double diff = NA(tA - Angle);

            double tr = CurrentGoal == Goal.Flee ? 0.25 : TURN_RATE;
            double neuralTurn = Brain.LastOutput[1] * 0.025;
            Angle = NA(Angle + diff * tr + neuralTurn);

            double speed = Genes.EffMaxSpeed * (1.0 + Math.Max(0, Brain.LastOutput[0]) * 0.12);
            bool run = false;

            switch (CurrentGoal)
            {
                case Goal.Flee: speed *= SPEED_RUN * 1.3; run = true; break;
                case Goal.FindFood when Hunger > 0.5: speed *= SPEED_RUN; run = true; break;
                case Goal.HuntAnimal: speed *= SPEED_RUN * 0.95; run = true; break;
                case Goal.DefendTerritory: speed *= SPEED_RUN; run = true; break;
                default: speed *= SPEED_WALK; break;
            }

            if (Fatigue > 0.7) speed *= 0.45;
            if (IsChild) speed *= 0.75;
            if (dist < 55 && CurrentGoal != Goal.Flee && CurrentGoal != Goal.HuntAnimal)
                speed *= Math.Max(0.3, dist / 55.0);

            if (Math.Abs(diff) < Math.PI * 0.55)
            {
                Vx += Math.Cos(Angle) * speed * dt;
                Vy += Math.Sin(Angle) * speed * dt;
            }

            State = run ? HumanState.Running : HumanState.Walking;
        }

        /// <summary>In water: abandon any stationary activity and wade to the nearest shore.</summary>
        private void WadeToShore(WorldContext world)
        {
            var land = world.World.FindNearestLand(X, Y);
            if (land is { } l) SetTarget(l.X, l.Y);
            CurrentGoal = Goal.None;
            GoalCooldown = 8;
            State = HumanState.Walking;
        }

        private void Act(double dt, WorldContext world)
        {
            // Сон в воде невозможен — проснуться и выбраться на берег
            if (State == HumanState.Sleeping && world.World.Env.IsWater(X, Y))
            {
                State = HumanState.Walking;
                CurrentGoal = Goal.None;
                GoalCooldown = 6;
                var land = world.World.FindNearestLand(X, Y);
                if (land is { } l) SetTarget(l.X, l.Y);
            }

            switch (CurrentGoal)
            {
                case Goal.FindFood:
                case Goal.EatFood:
                    double gatherBonus = 1.0 + SkillGathering * 0.4 + CombinedTechBonus.GatheringBonus +
                                         (EquippedTool != null ? (EquippedTool.Modifier - 1.0) * 0.5 : 0.0);
                    var food = world.TryGetFood(X, Y, 65);
                    if (food != null)
                    {
                        double effFood = food.Value * gatherBonus * (1.0 + CombinedTechBonus.FoodEfficiency * 0.4);
                        Eat(effFood);
                        UseEquippedTool();
                        SkillGathering = Math.Min(1.0, SkillGathering + 0.01);
                        Brain.Reward(0.5);
                        Brain.RememberEpisode(X, Y, EpisodeType.FoundFood, effFood);
                        FoodEaten++;
                        State = HumanState.Eating;
                        GoalDebug = $"EATING h={Hunger:F2}";
                        GoalCooldown = 12;

                        // Излишек еды передаём в хранилище племени
                        if (Hunger < 0.15 && TribeId >= 0)
                            world.World.GetTribeById(TribeId)?.ContributeFood(2.5);

                        if (Hunger < 0.08) { CurrentGoal = Goal.None; GoalCooldown = 18; }
                        else TryFindFood(world);
                    }
                    break;

                case Goal.Cook:
                    Vx *= 0.9;
                    Vy *= 0.9;
                    if (GoalTimer <= 0)
                    {
                        double cookAmt = Math.Min(MeatStored, 10.0);
                        MeatStored -= cookAmt;
                        Eat(cookAmt * (1.45 + SkillCooking * 0.4 + CombinedTechBonus.FoodEfficiency));
                        SkillCooking = Math.Min(1.0, SkillCooking + 0.015);
                        Brain.Reward(0.6);
                        GoalDebug = "COOKED meal!";
                        CurrentGoal = Goal.None;
                        GoalCooldown = 10;
                    }
                    break;

                case Goal.HuntAnimal:
                    var prey = world.World.GetAnimalById(TargetEntityId);
                    double attackRange = KnownTechnologies.Contains("bow") ? 115.0 :
                                         KnownTechnologies.Contains("spear") ? 68.0 : 46.0;
                    if (prey != null && prey.IsAlive && Dist(X, Y, prey.X, prey.Y) < attackRange)
                    {
                        double pow = Genes.PhysicalStrength * (1.0 + SkillFighting * 0.5 + SkillHunting * 0.4 + CombinedTechBonus.HuntingBonus);
                        if (EquippedWeapon != null)
                        {
                            pow *= EquippedWeapon.Modifier;
                            UseEquippedWeapon();
                        }
                        prey.TakeDamage(pow * 0.85, "Hunter");
                        SkillHunting = Math.Min(1.0, SkillHunting + 0.012);
                        SkillFighting = Math.Min(1.0, SkillFighting + 0.006);
                        State = HumanState.Fighting;

                        if (!prey.IsAlive)
                        {
                            double meat = prey.Genes.Size * 24.0;
                            Eat(meat * 0.45 * (1.0 + CombinedTechBonus.FoodEfficiency * 0.3));
                            MeatStored = Math.Min(60.0, MeatStored + meat * 0.55);
                            if (TribeId >= 0)
                                world.World.GetTribeById(TribeId)?.ContributeFood(meat * 0.25);

                            Brain.Reward(0.95);
                            Brain.RememberEpisode(prey.X, prey.Y, EpisodeType.FoundFood, meat);
                            AnimalsKilled++;
                            FoodEaten++;
                            GoalDebug = $"HUNTED {prey.SpeciesName}! +{meat * 0.55:F0} meat";
                            world.World.AddEventThrottled("hunt_" + Id, X, Y, Lang.T("hunt", Name, prey.SpeciesName), unchecked((int)0xFFFFC36B), 200);
                            Reputation += 0.12;
                            CurrentGoal = Goal.None;
                            GoalCooldown = 12;
                            TargetEntityId = -1;
                        }
                    }
                    else if (prey == null || !prey.IsAlive)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.DefendTerritory:
                    var enemy = world.World.GetAnimalById(TargetEntityId);
                    double defRange = KnownTechnologies.Contains("bow") ? 105.0 :
                                      KnownTechnologies.Contains("spear") ? 65.0 : 46.0;
                    if (enemy != null && enemy.IsAlive && Dist(X, Y, enemy.X, enemy.Y) < defRange)
                    {
                        double pow = Genes.PhysicalStrength * (1.0 + SkillFighting * 0.6 + CombinedTechBonus.AttackBonus);
                        if (EquippedWeapon != null)
                        {
                            pow *= EquippedWeapon.Modifier;
                            UseEquippedWeapon();
                        }
                        enemy.TakeDamage(pow * 0.8, "Defender");
                        SkillFighting = Math.Min(1.0, SkillFighting + 0.01);
                        State = HumanState.Fighting;

                        if (!enemy.IsAlive)
                        {
                            Brain.Reward(0.85);
                            Reputation += 0.2;
                            BattlesWon++;
                            MeatStored = Math.Min(60.0, MeatStored + enemy.Genes.Size * 12.0);
                            GoalDebug = $"DEFENDED vs {enemy.SpeciesName}!";
                            KnownDangerousSpecies.Add(enemy.SpeciesName);
                            CurrentGoal = Goal.None;
                            TargetEntityId = -1;
                        }
                    }
                    else if (enemy == null || !enemy.IsAlive)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.Mate:
                    var mp = world.World.GetHumanById(TargetEntityId);
                    if (mp != null && mp.IsAlive && mp.IsAdult && !mp.IsPregnant &&
                        mp.Sex != Sex && Dist(X, Y, mp.X, mp.Y) < 42)
                    {
                        Human mother = Sex == Gender.Female ? this : mp;
                        Human father = Sex == Gender.Female ? mp : this;
                        if (!mother.IsPregnant)
                        {
                            mother.IsPregnant = true;
                            mother.PregnancyPartner = father;
                            Brain.Reward(0.9);
                            mp.Brain.Reward(0.9);
                            Brain.Social(0.6);
                            mp.Brain.Social(0.6);
                            ImproveRelationship(mp.Id, 0.45, RelationType.Mate);
                            mp.ImproveRelationship(Id, 0.45, RelationType.Mate);
                            Brain.RememberEpisode(X, Y, EpisodeType.Mating, 1.0);
                            GoalDebug = "MATED!";
                        }
                        CurrentGoal = Goal.None;
                        GoalCooldown = 20;
                        mp.MateCooldown = 75;
                        TargetEntityId = -1;
                    }
                    else if (mp == null || !mp.IsAlive)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.Socialize:
                    SocialTimer += dt;
                    var sp = world.World.GetHumanById(TargetEntityId);
                    if (sp != null && sp.IsAlive && Dist(X, Y, sp.X, sp.Y) < 55)
                    {
                        State = HumanState.Socializing;
                        Vx *= 0.85;
                        Vy *= 0.85;
                        if (SocialTimer > 4)
                        {
                            Teach(sp);
                            // Активное использование системы протоязыка!
                            Concept topic = Hunger > 0.3 ? Concept.Food :
                                            KnownDangerousSpecies.Count > 0 && R.NextDouble() < 0.3 ? Concept.Danger :
                                            R.NextDouble() < 0.5 ? Concept.Friend : Concept.Tool;
                            var comm = world.World.Language.Communicate(this, sp, topic);

                            Brain.LearnFromObservation(sp.Brain.LastOutput, sp.CalculateFitness(), CalculateFitness());
                            Brain.Social(0.18);
                            sp.Brain.Social(0.18);
                            ImproveRelationship(sp.Id, 0.05, RelationType.Friend);
                            sp.ImproveRelationship(Id, 0.05, RelationType.Friend);

                            foreach (var s in KnownDangerousSpecies)
                                sp.KnownDangerousSpecies.Add(s);
                            foreach (var s in sp.KnownDangerousSpecies)
                                KnownDangerousSpecies.Add(s);

                            if (Hunger < 0.20 && sp.Hunger > 0.35 && MeatStored > 3)
                            {
                                sp.Eat(5);
                                MeatStored -= 3;
                                GoalDebug = $"SHARED meat→{sp.Name}";
                            }
                            else
                            {
                                GoalDebug = comm.Success ? $"SPOKE [{topic}]→{sp.Name}" : $"CHAT {sp.Name}";
                            }
                            SocialTimer = 0;
                            LessonsGiven++;
                        }
                        if (GoalTimer <= 0)
                        {
                            CurrentGoal = Goal.None;
                            GoalCooldown = 18;
                            TargetEntityId = -1;
                        }
                    }
                    else if (sp == null || !sp.IsAlive)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.Trade:
                    var partner = world.World.GetHumanById(TargetEntityId);
                    if (partner != null && partner.IsAlive && Dist(X, Y, partner.X, partner.Y) < 55)
                    {
                        State = HumanState.Trading;
                        Vx *= 0.85;
                        Vy *= 0.85;
                        if (MeatStored > 4)
                        {
                            MeatStored -= 3;
                            partner.MeatStored += 3;
                            TeachPerson(partner);
                            partner.TeachPerson(this);
                            Reputation += 0.1;
                            partner.Reputation += 0.1;
                            Brain.Reward(0.45);
                            partner.Brain.Reward(0.45);

                            if (TribeId >= 0 && partner.TribeId >= 0 && TribeId != partner.TribeId)
                            {
                                var t1 = world.World.GetTribeById(TribeId);
                                var t2 = world.World.GetTribeById(partner.TribeId);
                                if (t1 != null && t2 != null)
                                {
                                    t1.ImproveRelations(t2.Id, 0.12);
                                    t2.ImproveRelations(t1.Id, 0.12);
                                    t1.GetDiplomacy(t2.Id).TradeCount++;
                                    t2.GetDiplomacy(t1.Id).TradeCount++;
                                }
                            }
                            GoalDebug = $"TRADED w/ {partner.Name}!";
                        }
                        CurrentGoal = Goal.None;
                        GoalCooldown = 20;
                        TargetEntityId = -1;
                    }
                    else if (partner == null || !partner.IsAlive)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.Teach:
                    var student = world.World.GetHumanById(TargetEntityId);
                    if (student != null && student.IsAlive && Dist(X, Y, student.X, student.Y) < 55)
                    {
                        State = HumanState.Teaching;
                        Vx *= 0.85;
                        Vy *= 0.85;
                        TeachPerson(student);
                        world.World.Language.Communicate(this, student, Concept.Help);
                        SkillTeaching = Math.Min(1.0, SkillTeaching + 0.01);
                        Brain.Social(0.2);
                        GoalDebug = $"TEACHING {student.Name}";
                        LessonsGiven++;
                        if (GoalTimer <= 0)
                        {
                            CurrentGoal = Goal.None;
                            GoalCooldown = 18;
                            TargetEntityId = -1;
                        }
                    }
                    else if (student == null || !student.IsAlive)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.TameAnimal:
                    var wild = world.World.GetAnimalById(TargetEntityId);
                    if (wild != null && wild.IsAlive && Dist(X, Y, wild.X, wild.Y) < 48)
                    {
                        State = HumanState.Gathering;
                        Vx *= 0.85;
                        Vy *= 0.85;
                        if (MeatStored >= 1.5)
                        {
                            wild.Feed(3, Id);
                            MeatStored -= 1.5;
                        }
                        if (wild.TryTame(this, SkillHunting + Genes.EffSociability * 0.5))
                        {
                            Brain.Reward(1.0);
                            Reputation += 0.35;
                            GoalDebug = $"TAMED {wild.SpeciesName}!";
                            world.World.AddEventThrottled("tame_" + Id, X, Y, Lang.T("tamed", Name, wild.SpeciesName), unchecked((int)0xFF9DD9FF), 200);
                            CurrentGoal = Goal.None;
                            TargetEntityId = -1;
                        }
                    }
                    else if (wild == null || !wild.IsAlive)
                    {
                        CurrentGoal = Goal.None;
                        TargetEntityId = -1;
                    }
                    break;

                case Goal.Build:
                    if (GoalTimer <= 0)
                    {
                        if (world.TryBuild(X, Y, this))
                        {
                            SkillBuilding = Math.Min(1.0, SkillBuilding + 0.02);
                            Brain.Reward(0.6);
                            Brain.RememberEpisode(X, Y, EpisodeType.Building, 0.8);
                            BuildingsBuilt++;
                            Reputation += 0.1;
                            GoalDebug = "BUILT structure!";
                        }
                        CurrentGoal = Goal.None;
                        GoalCooldown = 15;
                    }
                    break;

                case Goal.Craft:
                    if (world.World.Env.IsWater(X, Y)) { WadeToShore(world); break; }
                    if (GoalTimer <= 0)
                    {
                        var item = TryCraft();
                        if (item != null)
                        {
                            Inventory.Add(item);
                            EquipItem(item);
                            SkillCrafting = Math.Min(1.0, SkillCrafting + 0.02);
                            Brain.Reward(0.5);
                            ToolsMade++;
                            GoalDebug = $"CRAFTED {item.Name}!";
                        }
                        CurrentGoal = Goal.None;
                        GoalCooldown = 15;
                    }
                    break;

                case Goal.GatherStone:
                case Goal.GatherWood:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 45)
                    {
                        State = HumanState.Gathering;
                        var node = world.TryHarvestMaterial(X, Y, 55);
                        if (node.HasValue)
                        {
                            if (node.Value.type == ResourceType.Stone) StoneStored += node.Value.amount;
                            else WoodStored += node.Value.amount;
                            if (TribeId >= 0)
                                world.World.GetTribeById(TribeId)?.ContributeMaterial(node.Value.amount);
                        }
                        SkillGathering = Math.Min(1.0, SkillGathering + 0.008);
                        Brain.Reward(0.25);
                        GoalDebug = CurrentGoal == Goal.GatherStone ? "GOT stone" : "GOT wood";
                        CurrentGoal = Goal.None;
                        GoalCooldown = 14;
                    }
                    break;

                case Goal.Heal:
                    if (world.World.Env.IsWater(X, Y)) { WadeToShore(world); break; }
                    double healRate = (1.0 + SkillMedicine + CombinedTechBonus.HealingBonus) * 0.9;
                    Health = Math.Min(MaxHealth, Health + dt * healRate);
                    SkillMedicine = Math.Min(1.0, SkillMedicine + 0.004);
                    Vx *= 0.9;
                    Vy *= 0.9;
                    if (Health > MaxHealth * 0.7)
                    {
                        CurrentGoal = Goal.None;
                        GoalCooldown = 10;
                    }
                    break;

                case Goal.Rest:
                    if (world.World.Env.IsWater(X, Y)) { WadeToShore(world); break; }
                    Vx *= 0.90;
                    Vy *= 0.90;
                    Health = Math.Min(MaxHealth, Health + dt * Genes.EffResilience * 0.55);
                    Fatigue = Math.Max(0, Fatigue - dt * 0.007);
                    State = HumanState.Resting;
                    if (Fatigue < 0.15 && Health > MaxHealth * 0.65)
                    {
                        CurrentGoal = Goal.None;
                        GoalCooldown = 18;
                    }
                    break;

                case Goal.Sleep:
                    // Нельзя спать в воде — сначала выбраться на берег
                    if (world.World.Env.IsWater(X, Y)) { WadeToShore(world); break; }
                    Vx *= 0.90;
                    Vy *= 0.90;
                    double shelterBonus = 1.0 + CombinedTechBonus.ShelterQuality * 0.5;
                    Health = Math.Min(MaxHealth, Health + dt * Genes.EffResilience * 0.4 * shelterBonus);
                    Fatigue = Math.Max(0, Fatigue - dt * 0.009 * shelterBonus);
                    State = HumanState.Sleeping;
                    if (!Brain.IsSleeping)
                    {
                        CurrentGoal = Goal.None;
                        GoalCooldown = 18;
                    }
                    break;

                case Goal.Flee:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 50)
                    {
                        CurrentGoal = Goal.None;
                        GoalCooldown = 18;
                    }
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
                    Vx *= 0.95;
                    Vy *= 0.95;
                    if (GoalTimer <= 0) CurrentGoal = Goal.None;
                    var g = FindDangerousAnimal(world);
                    if (g != null && Dist(X, Y, g.X, g.Y) < 160)
                    {
                        SetTarget(g.X, g.Y);
                        TargetEntityId = g.Id;
                        CurrentGoal = Goal.DefendTerritory;
                        GoalDebug = $"GUARD→DEFEND vs {g.SpeciesName}!";
                    }
                    break;

                case Goal.Explore:
                    if (HasTarget && Dist(X, Y, TargetX, TargetY) < 40)
                    {
                        CurrentGoal = Goal.None;
                        GoalCooldown = 12;
                    }
                    if (Hunger > 0.06)
                    {
                        var nf = world.TryGetFood(X, Y, 38);
                        if (nf != null)
                        {
                            Eat(nf.Value * (1.0 + CombinedTechBonus.FoodEfficiency * 0.3));
                            FoodEaten++;
                            Brain.Reward(0.25);
                        }
                    }
                    break;
            }
        }

        private void EquipItem(Item item)
        {
            if (item.Type == ItemType.Tool)
            {
                if (EquippedTool == null || item.Modifier > EquippedTool.Modifier)
                    EquippedTool = item;
            }
            else if (item.Type == ItemType.Weapon)
            {
                if (EquippedWeapon == null || item.Modifier > EquippedWeapon.Modifier)
                    EquippedWeapon = item;
            }
        }

        private void UseEquippedTool()
        {
            if (EquippedTool == null) return;
            EquippedTool.Use();
            if (EquippedTool.IsBroken)
            {
                Inventory.Remove(EquippedTool);
                EquippedTool = null;
            }
        }

        private void UseEquippedWeapon()
        {
            if (EquippedWeapon == null) return;
            EquippedWeapon.Use();
            if (EquippedWeapon.IsBroken)
            {
                Inventory.Remove(EquippedWeapon);
                EquippedWeapon = null;
            }
        }

        private void UpdateNeeds(double dt, WorldContext world)
        {
            double hr = dt * 0.0013 * Math.Clamp(Genes.EffMetabolism, 0.5, 1.4);
            if (State == HumanState.Running) hr *= 1.8;
            else if (State == HumanState.Fighting) hr *= 2.2;
            if (IsPregnant) hr *= 1.25;
            if (IsChild) hr *= 0.75;
            Hunger = Math.Min(1.0, Hunger + hr);

            Thirst = Math.Min(1.0, Thirst + dt * 0.0007);
            if (Thirst > 0.35 && (world.Weather == Weather.Rain || world.Weather == Weather.Storm))
                Drink(dt * 0.02);

            double fr = dt * 0.00045;
            if (State == HumanState.Running) fr *= 1.9;
            else if (State == HumanState.Fighting) fr *= 2.5;
            else if (State == HumanState.Resting || State == HumanState.Sleeping) fr = -dt * 0.0065;
            Fatigue = Math.Clamp(Fatigue + fr, 0.0, 1.0);

            if (Hunger > 0.8) Energy = Math.Max(0.0, Energy - dt * 0.28);
            else if (Hunger < 0.35) Energy = Math.Min(MaxEnergy, Energy + dt * 0.18);

            // Терморегуляция с учётом технологий одежды, огня и костров рядом
            double thermoProt = Math.Clamp(Genes.EffThermoTolerance + CombinedTechBonus.WarmthBonus, 0.0, 0.95);
            if (world.IsNearFirepit(X, Y, 130))
                thermoProt = Math.Min(0.98, thermoProt + 0.45);

            double tT = 0.5;
            if (world.Temperature < 0.3) tT -= (0.3 - world.Temperature) * (1.0 - thermoProt) * 0.15;
            if (world.Temperature > 0.7) tT += (world.Temperature - 0.7) * (1.0 - Genes.EffThermoTolerance) * 0.15;
            Temperature += (tT - Temperature) * dt * 0.02;

            if (Temperature < 0.08 || Temperature > 0.92)
                Health -= dt * 0.10 * (1.0 - thermoProt);

            Pain = Math.Max(0.0, Pain - dt * 0.03);
            if (Hunger > 0.95) Health -= dt * 0.15;

            double spoilageRate = KnownTechnologies.Contains("pottery") || KnownTechnologies.Contains("fermentation") ? 0.0001 : 0.00025;
            MeatStored = Math.Max(0.0, MeatStored - dt * spoilageRate);
        }

        private void UpdateEpiCtx(WorldContext w)
        {
            _epiCtx.Stress = Brain.Cortisol;
            _epiCtx.Hunger = Hunger;
            _epiCtx.Trauma = Brain.TraumaLevel;
            _epiCtx.Temperature = w.Temperature;
            _epiCtx.NutritionQuality = 1.0 - Hunger;
            _epiCtx.SocialSupport = 0;
            foreach (var r in Relationships.Values)
            {
                if (r.Type == RelationType.Friend || r.Type == RelationType.Family || r.Type == RelationType.Mate)
                    _epiCtx.SocialSupport = Math.Min(1.0, _epiCtx.SocialSupport + r.Strength * 0.3);
            }
            _epiCtx.LearningIntensity = Brain.Acetylcholine;
            _epiCtx.PhysicalActivity = State == HumanState.Running || State == HumanState.Hunting ? 0.8 : 0.25;
        }

        private void Physics(double dt, WorldContext world)
        {
            // Swimming: deep water strongly resists movement (nobody sprints across a river)
            double drag = world.World.Env.IsWater(X, Y) ? 0.30 : 1.0;
            X += Vx * dt * drag;
            Y += Vy * dt * drag;
            const double m = 30;
            if (X < m) { X = m + 5; Vx = Math.Abs(Vx) * 0.2; SetExplore(world.Width, world.Height); }
            else if (X > world.Width - m) { X = world.Width - m - 5; Vx = -Math.Abs(Vx) * 0.2; SetExplore(world.Width, world.Height); }
            if (Y < m) { Y = m + 5; Vy = Math.Abs(Vy) * 0.2; SetExplore(world.Width, world.Height); }
            else if (Y > world.Height - m) { Y = world.Height - m - 5; Vy = -Math.Abs(Vy) * 0.2; SetExplore(world.Width, world.Height); }
            Vx *= FRICTION;
            Vy *= FRICTION;
            TotalDistanceTraveled += Math.Sqrt(Vx * Vx + Vy * Vy) * dt;
        }

        public void Eat(double n)
        {
            Hunger = Math.Max(0.0, Hunger - n * 0.22);
            Energy = Math.Min(MaxEnergy, Energy + n * 6.0);
            Health = Math.Min(MaxHealth, Health + n * 0.55);
            Drink(n * 0.08);
        }

        public void Drink(double a) => Thirst = Math.Max(0.0, Thirst - a);

        public void TakeDamage(double dmg, string src)
        {
            double def = Genes.EffPainTolerance * 0.25 + CombinedTechBonus.PredatorDefense * 0.3;
            double actualDmg = dmg * Math.Clamp(1.0 - def, 0.2, 1.0);
            Health -= actualDmg;
            Pain = Math.Min(1.0, Pain + actualDmg / 50.0);
            Brain.Pain(actualDmg / 50.0);
            Brain.Fear(0.35);
            if (actualDmg > 28) Brain.TraumaticEvent(src);
            if (Health <= 0) Die(src);
        }

        public void Die(string c)
        {
            if (!IsAlive) return;
            IsAlive = false;
            CauseOfDeath = c;
            State = HumanState.Dead;
        }

        public void TeachPerson(Human s)
        {
            if (s == null || !s.IsAlive) return;
            double eff = (0.15 + SkillTeaching) * Genes.EffIntelligence * s.Genes.LearningSpeed * 0.22;
            foreach (var t in KnownTechnologies)
            {
                if (!s.KnownTechnologies.Contains(t) && R.NextDouble() < eff)
                    s.LearnTechnology(t);
            }

            if (SkillHunting > s.SkillHunting) s.SkillHunting = Math.Min(1.0, s.SkillHunting + eff * 0.12);
            if (SkillBuilding > s.SkillBuilding) s.SkillBuilding = Math.Min(1.0, s.SkillBuilding + eff * 0.12);
            if (SkillCrafting > s.SkillCrafting) s.SkillCrafting = Math.Min(1.0, s.SkillCrafting + eff * 0.12);
            if (SkillFighting > s.SkillFighting) s.SkillFighting = Math.Min(1.0, s.SkillFighting + eff * 0.10);
            if (SkillCooking > s.SkillCooking) s.SkillCooking = Math.Min(1.0, s.SkillCooking + eff * 0.10);
            if (SkillMedicine > s.SkillMedicine) s.SkillMedicine = Math.Min(1.0, s.SkillMedicine + eff * 0.10);

            foreach (var sp in KnownDangerousSpecies)
                s.KnownDangerousSpecies.Add(sp);

            // Передача слов из словаря учителя
            for (int i = 0; i < KnownWords.Count; i++)
            {
                int w = KnownWords[i];
                if (!s.KnowsWord(w) && R.NextDouble() < eff * 0.5)
                    s.LearnWord(w);
            }

            s.Brain.Social(0.12);
        }

        public void Teach(Human s) => TeachPerson(s);

        private Item? TryCraft()
        {
            if (!KnownTechnologies.Contains("stone_tools")) return null;
            double craftChance = Math.Clamp((0.25 + SkillCrafting + CombinedTechBonus.CraftingBonus) * (0.4 + Genes.EffCreativity), 0.15, 0.95);
            if (R.NextDouble() > craftChance) return null;

            bool makeWeapon = EquippedWeapon == null || (EquippedTool != null && R.NextDouble() < 0.55);
            if (makeWeapon)
            {
                string wName = KnownTechnologies.Contains("bow") ? "Composite Bow" :
                               KnownTechnologies.Contains("spear") ? "Flint Spear" : "Stone Club";
                double mod = KnownTechnologies.Contains("bow") ? 1.85 :
                             KnownTechnologies.Contains("spear") ? 1.55 : 1.30;
                return new Item
                {
                    Type = ItemType.Weapon,
                    Name = wName,
                    Modifier = mod + SkillCrafting * 0.4,
                    Durability = 70 + SkillCrafting * 40
                };
            }
            else
            {
                string tName = KnownTechnologies.Contains("tools") ? "Polished Axe" : "Stone Scraper";
                double mod = KnownTechnologies.Contains("tools") ? 1.60 : 1.35;
                return new Item
                {
                    Type = ItemType.Tool,
                    Name = tName,
                    Modifier = mod + SkillCrafting * 0.4,
                    Durability = 80 + SkillCrafting * 40
                };
            }
        }

        public void ImproveRelationship(int id, double a, RelationType t)
        {
            if (!Relationships.TryGetValue(id, out var rel))
            {
                rel = new Relationship { TargetId = id };
                Relationships[id] = rel;
            }
            rel.Strength = Math.Min(1.0, rel.Strength + a);
            if (rel.Type != RelationType.Family && rel.Type != RelationType.Mate)
                rel.Type = t;
            else if (t == RelationType.Mate)
                rel.Type = RelationType.Mate;
        }

        public double GetRelationStrength(int id) =>
            Relationships.TryGetValue(id, out var r) ? r.Strength : 0.0;

        public double GetGeneticDistance(Human o)
        {
            double d = 0;
            for (int i = 0; i < 4; i++)
                d += Math.Abs(Genes.Species[i] - o.Genes.Species[i]);
            return d * 0.25;
        }

        public static Human CreateChild(Human m, Human f, double x, double y)
        {
            var cg = Genome.Cross(m.Genes, f.Genes);
            cg.Mutate(0.15, 0.10);
            var cb = new FastBrain();
            cb.TransferKnowledge(m.Brain, 0.25);
            cb.TransferKnowledge(f.Brain, 0.25);
            cb.Mutate(0.16, 0.12);

            var child = new Human(x, y, cg, cb) { Age = 0, TribeId = m.TribeId };
            child.Health = child.MaxHealth * 0.85;
            child.Energy = child.MaxEnergy * 0.65;

            foreach (var t in m.KnownTechnologies)
                if (R.NextDouble() < 0.45) child.LearnTechnology(t);
            foreach (var t in f.KnownTechnologies)
                if (R.NextDouble() < 0.35) child.LearnTechnology(t);

            foreach (var s in m.KnownDangerousSpecies) child.KnownDangerousSpecies.Add(s);
            for (int i = 0; i < m.KnownWords.Count; i++)
                if (R.NextDouble() < 0.5) child.LearnWord(m.KnownWords[i]);

            child.ImproveRelationship(m.Id, 0.95, RelationType.Family);
            m.ImproveRelationship(child.Id, 0.95, RelationType.Family);
            if (f.IsAlive)
            {
                child.ImproveRelationship(f.Id, 0.80, RelationType.Family);
                f.ImproveRelationship(child.Id, 0.80, RelationType.Family);
            }
            return child;
        }

        public double CalculateFitness() =>
            (Health / MaxHealth) * 20.0 + (1.0 - Hunger) * 15.0 + Age * 0.6 +
            ChildrenBorn * 32.0 + FoodEaten * 0.5 + AnimalsKilled * 6.0 +
            Reputation * 12.0 + MeatStored * 0.35 + KnownTechnologies.Count * 4.0 +
            (SkillHunting + SkillBuilding + SkillCrafting + SkillFighting + SkillSpeaking + SkillTeaching + SkillCooking + SkillMedicine) * 6.0;

        public float DrawSize => (float)(8 + Genes.EffBodySize * 4);

        public Color GetColor()
        {
            if (!IsAlive) return Color.Gray;
            if (IsChild) return Color.FromArgb(200, 225, 255);
            if (IsLeader) return Color.Gold;
            return Genes.Skin;
        }
    }

    public enum Gender { Male, Female }
    public enum HumanState { Idle, Walking, Running, Eating, Sleeping, Resting, Fighting, Hunting, Gathering, Building, Crafting, Socializing, Teaching, Trading, Fleeing, Dead }
    public class Relationship { public int TargetId; public RelationType Type; public double Strength; public double LastInteraction; }
    public enum RelationType { Neutral, Friend, Family, Mate, Rival, Enemy }
    public class Item { public string Name = ""; public ItemType Type; public double Modifier; public double Durability; public void Use() => Durability -= 1; public bool IsBroken => Durability <= 0; }
    public enum ItemType { Food, Tool, Weapon, Shield, Material, Medicine, Clothing }
}
