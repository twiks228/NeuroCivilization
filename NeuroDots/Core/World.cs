using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NeuroCivilization.Core
{

public class World
    {
        public double Width = 2000;
        public double Height = 1500;

        public List<Human> Humans = new();
        public List<Animal> Animals = new();
        public List<Tribe> Tribes = new();
        public List<ResourceNode> Resources = new();
        public List<Building> Buildings = new();

        // O(1) ������� �� ID
        public readonly Dictionary<int, Human> HumanById = new();
        public readonly Dictionary<int, Animal> AnimalById = new();
        public readonly Dictionary<int, Tribe> TribeById = new();

        // ���������������� ����������� (Spatial Grids) ��� O(1) ������ �������
        public readonly SpatialGrid<Human> HumanGrid;
        public readonly SpatialGrid<Animal> AnimalGrid;
        public readonly SpatialGrid<ResourceNode> ResourceGrid;
        public readonly SpatialGrid<Building> BuildingGrid;

        // ��� ������� ���� �������� (����������� 1 ��� �� ���)
        public readonly Dictionary<int, (double X, double Y, int Count)> PackCenters = new();

        public Environment Env;
        public LanguageSystem Language;
        public EvolutionManager Evolution;
        public WorldContext Context;

        public int Tick;
        public double TotalTime;
        public int TotalBirths, TotalDeaths, TotalAnimalDeaths;
        public int PeakPopulation, TechsDiscovered;

        // ����� ������ ������� ����
        public readonly List<string> EventLog = new(24);

        private readonly HashSet<int> _countedHDeaths = new();
        private readonly HashSet<int> _countedADeaths = new();
        private readonly List<SoundEvent> _sounds = new();
        private readonly List<Human> _newHumansBuffer = new(16);
        private readonly List<Animal> _newAnimalsBuffer = new(16);
        private readonly Random _rng;

        public World(int seed = 0)
        {
            _rng = seed == 0 ? new Random() : new Random(seed);
            Env = new Environment(_rng);
            Language = new LanguageSystem();
            Evolution = new EvolutionManager(_rng);

            HumanGrid = new SpatialGrid<Human>(Width, Height, 140);
            AnimalGrid = new SpatialGrid<Animal>(Width, Height, 140);
            ResourceGrid = new SpatialGrid<ResourceNode>(Width, Height, 120);
            BuildingGrid = new SpatialGrid<Building>(Width, Height, 160);

            Context = new WorldContext { Width = Width, Height = Height, World = this };
            GenerateResources(360);
            Env.GenerateRivers(Width, Height, _rng);
            SpawnRiverNodes();
            RebuildSpatialGrids();
            AddEvent("World initialized. Ecosystem simulation started.");
        }

        /// <summary>World-space floating label rendered for a few seconds above the action point.</summary>
        public sealed class Popup
        {
            public double X, Y;
            public string Text = "";
            public int TickSpawned;
            public int Argb;
            public int TickLiving;
        }

        /// <summary>Floating event popups (world-anchored, faded by renderer).</summary>
        public readonly List<Popup> Popups = new();

        /// <summary>Log + floating world-space popup at the event location.</summary>
        public void AddEventAt(double x, double y, string message, int argb = unchecked((int)0xFFE97DFF))
        {
            AddEvent(message);
            if (Popups.Count > 40) Popups.RemoveAt(0);
            Popups.Add(new Popup { X = x, Y = y, Text = message, TickSpawned = Tick, Argb = argb });
        }

        public void AddEvent(string message)
        {
            string entry = $"[Y{Env.Year} T{Tick}] {message}";
            Trace.WriteLine("[SIM] " + message);
            EventLog.Insert(0, entry);
            if (EventLog.Count > 18)
                EventLog.RemoveAt(EventLog.Count - 1);
        }

        /// <summary>Per-key event throttle: floods (hunts, kills, trades) are logged at most once per <paramref name="gate"/> ticks.</summary>
        readonly Dictionary<string, int> _evGate = new();
        public void AddEventThrottled(string gateKey, double x, double y, string message, int argb, int gate = 240)
        {
            int last = _evGate.TryGetValue(gateKey, out var t) ? t : int.MinValue;
            if (Tick - last < gate) return;
            _evGate[gateKey] = Tick;
            AddEventAt(x, y, message, argb);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Human? GetHumanById(int id) => HumanById.TryGetValue(id, out var h) ? h : null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Animal? GetAnimalById(int id) => AnimalById.TryGetValue(id, out var a) ? a : null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Tribe? GetTribeById(int id) => TribeById.TryGetValue(id, out var t) ? t : null;

        public void SpawnHumans(int count)
        {
            int groupSize = Math.Max(5, count / 4);
            int groups = count / groupSize;

            for (int g = 0; g < groups; g++)
            {
                double cx = 220 + _rng.NextDouble() * (Width - 440);
                double cy = 220 + _rng.NextDouble() * (Height - 440);

                var tribe = new Tribe { CenterX = cx, CenterY = cy };
                Tribes.Add(tribe);
                TribeById[tribe.Id] = tribe;

                int baseWordFood = Language.CreateWord(Concept.Food, -1);
                int baseWordDanger = Language.CreateWord(Concept.Danger, -1);

                for (int i = 0; i < groupSize; i++)
                {
                    double x = Math.Clamp(cx + (_rng.NextDouble() - 0.5) * 120, 50, Width - 50);
                    double y = Math.Clamp(cy + (_rng.NextDouble() - 0.5) * 120, 50, Height - 50);
                    var human = new Human(x, y);
                    human.LearnTechnology("language_proto");
                    human.LearnTechnology("stone_tools");
                    if (_rng.NextDouble() < 0.45) human.LearnTechnology("fire_control");
                    if (_rng.NextDouble() < 0.35) human.LearnTechnology("shelter_basic");
                    human.LearnWord(baseWordFood);
                    human.LearnWord(baseWordDanger);
                    human.Hunger = 0.05 + _rng.NextDouble() * 0.1;
                    human.Age = 18 + _rng.NextDouble() * 14;
                    Humans.Add(human);
                    HumanById[human.Id] = human;
                    tribe.AddMember(human);
                }

                // ��������� ����� � ������ ������ �������
                Buildings.Add(new Building
                {
                    X = cx,
                    Y = cy,
                    Type = BuildingType.Firepit,
                    BuilderId = -1,
                    TribeId = tribe.Id,
                    Health = 120,
                    Size = 16
                });
            }

            int remaining = count - groups * groupSize;
            for (int i = 0; i < remaining; i++)
            {
                var h = new Human(80 + _rng.NextDouble() * (Width - 160), 80 + _rng.NextDouble() * (Height - 160));
                h.LearnTechnology("stone_tools");
                h.Hunger = 0.05;
                h.Age = 18 + _rng.NextDouble() * 10;
                Humans.Add(h);
                HumanById[h.Id] = h;
            }

            RebuildSpatialGrids();
        }

        public void SpawnAnimals(int herbivores, int predators)
        {
            string[] herbNames = { "Deer", "Rabbit", "Goat", "Bison", "Horse" };
            string[] predNames = { "Wolf", "Bear", "Lion", "Hyena" };

            for (int i = 0; i < herbivores; i++)
            {
                var g = new AnimalGenome
                {
                    Aggression = _rng.NextDouble() * 0.25,
                    AttackPower = _rng.NextDouble() * 0.25,
                    Speed = 55 + _rng.NextDouble() * 65
                };
                var a = new Animal(
                    50 + _rng.NextDouble() * (Width - 100),
                    50 + _rng.NextDouble() * (Height - 100),
                    g, null, herbNames[_rng.Next(herbNames.Length)])
                {
                    IsHerbivore = true,
                    IsPredator = false,
                    PackId = 100 + (i / 5),
                    Hunger = 0.1 + _rng.NextDouble() * 0.2
                };
                Animals.Add(a);
                AnimalById[a.Id] = a;
            }

            for (int i = 0; i < predators; i++)
            {
                var g = new AnimalGenome
                {
                    Aggression = 0.6 + _rng.NextDouble() * 0.4,
                    AttackPower = 0.55 + _rng.NextDouble() * 0.45,
                    Speed = 62 + _rng.NextDouble() * 60
                };
                var a = new Animal(
                    50 + _rng.NextDouble() * (Width - 100),
                    50 + _rng.NextDouble() * (Height - 100),
                    g, null, predNames[_rng.Next(predNames.Length)])
                {
                    IsPredator = true,
                    IsHerbivore = false,
                    PackId = i / 3,
                    Hunger = 0.2
                };
                Animals.Add(a);
                AnimalById[a.Id] = a;
            }

            RebuildSpatialGrids();
        }

        /// <summary>Water nodes (drink points) + berry banks spawned along generated rivers and lakes.</summary>
        private void SpawnRiverNodes()
        {
            int tries = 0, added = 0;
            while (added < 42 && tries++ < 900)
            {
                double x = 35 + _rng.NextDouble() * (Width - 70);
                double y = 35 + _rng.NextDouble() * (Height - 70);
                if (!Env.IsWater(x, y)) continue;

                Resources.Add(new ResourceNode
                {
                    X = x, Y = y, Type = ResourceType.Water,
                    Amount = 90, MaxAmount = 100, RegrowthRate = 0.85
                });

                double bx = x + 28 + _rng.NextDouble() * 26, by = y + (_rng.NextDouble() - 0.5) * 55;
                if (bx > 20 && by > 20 && bx < Width - 20 && by < Height - 20 && !Env.IsWater(bx, by))
                    Resources.Add(new ResourceNode
                    {
                        X = bx, Y = by, Type = ResourceType.Berry,
                        Amount = 40 + _rng.NextDouble() * 30, MaxAmount = 70, RegrowthRate = 0.55
                    });
                added++;
            }
        }

        /// <summary>Living diplomacy: relations drift by proximity, aggression and trade; 5 visible stages with chronicle events.</summary>
        private void EvolveDiplomacy()
        {
            for (int i = 0; i < Tribes.Count; i++)
                for (int j = i + 1; j < Tribes.Count; j++)
                {
                    var ta = Tribes[i];
                    var tb = Tribes[j];
                    if (ta.Population == 0 || tb.Population == 0) continue;

                    double dist = Math.Sqrt((ta.CenterX - tb.CenterX) * (ta.CenterX - tb.CenterX) + (ta.CenterY - tb.CenterY) * (ta.CenterY - tb.CenterY));
                    bool close = dist < (ta.TerritoryRadius + tb.TerritoryRadius) * 1.5;
                    var d1 = ta.GetDiplomacy(tb.Id);
                    var d2 = tb.GetDiplomacy(ta.Id);
                    string st0 = d1.Stage;
                    double r = _rng.NextDouble();

                    if (st0 == "War")
                    {
                        if (r < 0.22) { ta.ImproveRelations(tb.Id, 0.25); tb.ImproveRelations(ta.Id, 0.25); }
                        else if (r < 0.40 && close) AssignRaiders(ta, tb);
                    }
                    else if (st0 == "Hostile")
                    {
                        if (r < 0.20) { ta.ImproveRelations(tb.Id, 0.2); tb.ImproveRelations(ta.Id, 0.2); }
                        else if (r < 0.30 && (ta.AggressionNorm + tb.AggressionNorm) > 1.0)
                        { ta.WorsenRelations(tb.Id, 0.15); tb.WorsenRelations(ta.Id, 0.15); }
                    }
                    else if (st0 == "Trade")
                    {
                        if (r < 0.20 && d1.TradeCount > 2) { ta.ImproveRelations(tb.Id, 0.2); tb.ImproveRelations(ta.Id, 0.2); }
                        else if (r < 0.32) { ta.WorsenRelations(tb.Id, 0.1); tb.WorsenRelations(ta.Id, 0.1); }
                    }
                    else if (st0 == "Alliance")
                    {
                        if (r < 0.14) { ta.WorsenRelations(tb.Id, 0.12); tb.WorsenRelations(ta.Id, 0.12); }
                    }
                    else // Neutral
                    {
                        if (close && r < 0.16)
                        { ta.ImproveRelations(tb.Id, 0.14); tb.ImproveRelations(ta.Id, 0.14); if (d1.TradeCount > 0 || d2.TradeCount > 0) { d1.TradeCount++; d2.TradeCount++; } }
                        else if (close && r < 0.22 && (ta.AggressionNorm + tb.AggressionNorm) > 0.8)
                        { ta.WorsenRelations(tb.Id, 0.14); tb.WorsenRelations(ta.Id, 0.14); }
                        else if (close && r < 0.26)
                        { ta.WorsenRelations(tb.Id, 0.05); tb.WorsenRelations(ta.Id, 0.05); }
                    }

                    string st1 = ta.GetDiplomacy(tb.Id).Stage;
                    if (st1 != st0) AnnounceStage(ta, tb, st1);
                    else if (st1 == "War" && _rng.Next(4) == 0) AssignRaiders(ta, tb);
                }
        }

        void AnnounceStage(Tribe ta, Tribe tb, string stage)
        {
            string key = stage switch
            {
                "War" => "d_war", "Hostile" => "d_hostile", "Trade" => "d_trade",
                "Alliance" => "d_alliance", _ => "d_neutral"
            };
            int argb = stage == "War" ? unchecked((int)0xFFFF6050)
                     : stage == "Hostile" ? unchecked((int)0xFFFFB060)
                     : stage == "Trade" || stage == "Alliance" ? unchecked((int)0xFF9FD4FF)
                     : unchecked((int)0xFFB8C4CC);
            AddEventThrottled("diplo" + ta.Id + "_" + tb.Id + "_" + stage, ta.CenterX, ta.CenterY,
                Lang.T(key, ta.Name, tb.Name), argb, 90);
        }

        /// <summary>War effect: best fighters march on the enemy camp to plunder supplies.</summary>
        void AssignRaiders(Tribe attacker, Tribe enemy)
        {
            for (int k = 0; k < attacker.MemberIds.Count && k < 6; k++)
            {
                var h = HumanById.TryGetValue(attacker.MemberIds[k], out var mm) ? mm : null;
                if (h == null || !h.IsAlive || h.IsChild) continue;
                if (h.RaidTargetTribeId == enemy.Id) continue;
                if (h.SkillFighting < 0.25) continue;
                h.RaidTargetTribeId = enemy.Id;
                h.TargetX = enemy.CenterX;
                h.TargetY = enemy.CenterY;
                h.HasTarget = true;
                h.CurrentGoal = Goal.Explore;
                h.GoalDebug = $"RAID→{enemy.Name}";
            }
        }

        /// <summary>Raiders inside enemy territory steal stored food (visible plunder events).</summary>
        private void ProcessRaids()
        {
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (!h.IsAlive || h.RaidTargetTribeId < 0) continue;
                var enemy = GetTribeById(h.RaidTargetTribeId);
                var home = h.TribeId >= 0 ? GetTribeById(h.TribeId) : null;
                if (enemy == null || home == null || enemy.GetDiplomacy(home.Id).Stage != "War")
                {
                    h.RaidTargetTribeId = -1;
                    continue;
                }
                if (enemy.IsInTerritory(h.X, h.Y))
                {
                    double stolen = Math.Min(8, enemy.FoodStorage);
                    if (stolen <= 0.5)
                    {
                        h.RaidTargetTribeId = -1;
                        continue;
                    }
                    enemy.FoodStorage -= stolen;
                    home.FoodStorage += stolen * 0.85;
                    h.MeatStored += stolen * 0.2;
                    h.Brain.Reward(0.55);
                    h.Reputation += 0.1;
                    AddEventThrottled("raid", h.X, h.Y,
                        Lang.T("raid", h.Name, enemy.Name), unchecked((int)0xFFFF8060), 150);
                }
            }
        }

        /// <summary>Market trading posts: automatic exchange between tribes with open trade/alliance.</summary>
        private void ProcessMarkets()
        {
            for (int i = 0; i < Tribes.Count; i++)
                for (int j = i + 1; j < Tribes.Count; j++)
                {
                    var ta = Tribes[i];
                    var tb = Tribes[j];
                    var stage = ta.GetDiplomacy(tb.Id).Stage;
                    if (stage != "Trade" && stage != "Alliance") continue;
                    if (!HasTribeBuilding(ta.Id, BuildingType.Market) || !HasTribeBuilding(tb.Id, BuildingType.Market)) continue;

                    for (int side = 0; side < 2; side++)
                    {
                        var from = side == 0 ? ta : tb;
                        var to = side == 0 ? tb : ta;
                        if (_rng.Next(2) == 0)
                        {
                            if (from.FoodStorage >= 14 && to.MaterialStorage >= 5)
                            { from.FoodStorage -= 10; to.FoodStorage += 10; from.MaterialStorage += 3; to.MaterialStorage -= 1; }
                        }
                        else if (from.MaterialStorage >= 7 && to.FoodStorage >= 10)
                        { from.MaterialStorage -= 6; to.MaterialStorage += 6; from.MaterialStorage += 1; }
                    }
                    ta.CultureLevel += 0.01; tb.CultureLevel += 0.01;
                    ta.GetDiplomacy(tb.Id).TradeCount += 2;
                    tb.GetDiplomacy(ta.Id).TradeCount += 2;
                    AddEventThrottled("mkt", ta.CenterX, ta.CenterY,
                        Lang.T("trade_deal", ta.Name, tb.Name), unchecked((int)0xFF9FD4FF), 200);
                }
        }

        /// <summary>Ritual dance at the totem: raises morale/cohesion of the gathered members.</summary>
        private void ProcessRituals()
        {
            for (int t = 0; t < Tribes.Count; t++)
            {
                var tribe = Tribes[t];
                if (!HasTribeBuilding(tribe.Id, BuildingType.Totem)) continue;
                if (tribe.Population == 0) continue;

                int dancers = 0;
                double tx = 0, ty = 0;
                for (int b = 0; b < Buildings.Count; b++)
                    if (Buildings[b].Type == BuildingType.Totem && Buildings[b].TribeId == tribe.Id)
                    { tx = Buildings[b].X; ty = Buildings[b].Y; break; }

                for (int h = 0; h < Humans.Count; h++)
                {
                    var hu = Humans[h];
                    if (!hu.IsAlive || hu.TribeId != tribe.Id) continue;
                    double dx = hu.X - tx, dy = hu.Y - ty;
                    if (dx * dx + dy * dy < 220.0 * 220.0)
                    {
                        dancers++;
                        hu.Brain.Reward(0.12);
                        if (hu.Sex == Gender.Female) hu.Happiness = Math.Min(1.0, hu.Happiness + 0.02);
                    }
                }
                if (dancers < 3) continue;
                tribe.CultureLevel += 0.02;
                tribe.CooperationNorm = Math.Min(1.0, tribe.CooperationNorm + 0.005);
                AddEventThrottled("rit", tx, ty, Lang.T("ritual", tribe.Name, dancers), unchecked((int)0xFFB080FF), 200);
            }
        }

        /// <summary>Chronicle announcements for stable lineage transitions (leader → heir).</summary>
        private void ProcessLeaderAnnouncements()
        {
            for (int i = 0; i < Tribes.Count; i++)
            {
                var t = Tribes[i];
                _announcedLeader.TryGetValue(t.Id, out var last);
                if (t.LeaderId <= 0 || t.LeaderId == last) { _announcedLeader[t.Id] = t.LeaderId; continue; }
                _announcedLeader[t.Id] = t.LeaderId;
                var leader = HumanById.TryGetValue(t.LeaderId, out var lh) ? lh : null;
                if (leader == null) continue;
                AddEventThrottled("heir" + t.Id, t.CenterX, t.CenterY,
                    Lang.T("heir", t.Name, leader.Name, ""), unchecked((int)0xFFFFD080), 100);
            }
        }

        bool HasTribeBuilding(int tribeId, BuildingType type)
        {
            for (int i = 0; i < Buildings.Count; i++)
                if (Buildings[i].TribeId == tribeId && Buildings[i].Type == type) return true;
            return false;
        }

        /// <summary>Nearest dry land from a point inside water (ring scan) — used to wade ashore.</summary>
        public (double X, double Y)? FindNearestLand(double x, double y)
        {
            for (int r = 20; r < 900; r += 18)
                for (int a = 0; a < 16; a++)
                {
                    double ang = a * Math.PI / 8;
                    double px = x + Math.Cos(ang) * r, py = y + Math.Sin(ang) * r;
                    if (px < 40 || py < 40 || px > Width - 40 || py > Height - 40) continue;
                    if (!Env.IsWater(px, py)) return (px, py);
                }
            return null;
        }

        readonly Dictionary<int, int> _announcedLeader = new();

        private void GenerateResources(int count)
        {
            for (int i = 0; i < count; i++)
            {
                double x = 35 + _rng.NextDouble() * (Width - 70);
                double y = 35 + _rng.NextDouble() * (Height - 70);
                var biome = Env.GetBiome(x, y, Width, Height);

                double r = _rng.NextDouble();
                ResourceType type;
                if (biome == Biome.Forest || biome == Biome.TropicalForest || biome == Biome.BorealForest)
                    type = r < 0.45 ? ResourceType.Berry : r < 0.75 ? ResourceType.Plant : r < 0.92 ? ResourceType.Wood : ResourceType.Stone;
                else if (biome == Biome.Desert || biome == Biome.Mountain)
                    type = r < 0.30 ? ResourceType.Plant : r < 0.75 ? ResourceType.Stone : ResourceType.Berry;
                else
                    type = r < 0.42 ? ResourceType.Berry : r < 0.74 ? ResourceType.Plant : r < 0.88 ? ResourceType.Stone : ResourceType.Wood;

                Resources.Add(new ResourceNode
                {
                    X = x,
                    Y = y,
                    Type = type,
                    Amount = 55 + _rng.NextDouble() * 45,
                    MaxAmount = 100,
                    RegrowthRate = type == ResourceType.Berry ? 0.55 :
                                   type == ResourceType.Plant ? 0.45 : 0.08
                });
            }
        }

        private void RebuildSpatialGrids()
        {
            HumanGrid.Clear();
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (h.IsAlive) HumanGrid.Insert(h, h.X, h.Y);
            }

            AnimalGrid.Clear();
            PackCenters.Clear();
            for (int i = 0; i < Animals.Count; i++)
            {
                var a = Animals[i];
                if (!a.IsAlive) continue;
                AnimalGrid.Insert(a, a.X, a.Y);
                if (a.PackId >= 0)
                {
                    if (PackCenters.TryGetValue(a.PackId, out var cur))
                        PackCenters[a.PackId] = (cur.X + a.X, cur.Y + a.Y, cur.Count + 1);
                    else
                        PackCenters[a.PackId] = (a.X, a.Y, 1);
                }
            }

            // ����������� ������� ��������� ����
            if (PackCenters.Count > 0)
            {
                var keys = new List<int>(PackCenters.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    int k = keys[i];
                    var v = PackCenters[k];
                    PackCenters[k] = (v.X / v.Count, v.Y / v.Count, v.Count);
                }
            }

            ResourceGrid.Clear();
            for (int i = 0; i < Resources.Count; i++)
            {
                var r = Resources[i];
                if (r.Amount > 0.5) ResourceGrid.Insert(r, r.X, r.Y);
            }

            BuildingGrid.Clear();
            for (int i = 0; i < Buildings.Count; i++)
            {
                var b = Buildings[i];
                BuildingGrid.Insert(b, b.X, b.Y);
            }
        }

        public void Update(double dt)
        {
            Tick++;
            TotalTime += dt;

            int prevYear = Env.Year;
            Env.Update(dt);
            Context.DayLight = Env.DayLight;
            Context.Temperature = Env.Temperature;
            Context.Season = Env.CurrentSeason;
            Context.Weather = Env.CurrentWeather;

            UpdateResources(dt);
            RebuildSpatialGrids();

            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (h.IsAlive) h.Update(dt, Context);
            }

            for (int i = 0; i < Animals.Count; i++)
            {
                var a = Animals[i];
                if (a.IsAlive) a.Update(dt, Context);
            }

            for (int i = 0; i < Tribes.Count; i++)
                Tribes[i].Update(dt, HumanById);

            Language.Update(dt);

            // Rivers & lakes: fording slows movement (deep water drag)
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (h.IsAlive && Env.IsWater(h.X, h.Y)) { h.Vx *= 0.90; h.Vy *= 0.90; }
            }
            for (int i = 0; i < Animals.Count; i++)
            {
                var a = Animals[i];
                if (a.IsAlive && Env.IsWater(a.X, a.Y)) { a.Vx *= 0.86; a.Vy *= 0.86; }
            }

            ProcessBirths();

            if (Tick % 45 == 0) ProcessTechDiscovery();
            if (Tick % 4 == 0) ProcessInteractions(dt);
            if (Tick % 120 == 0) ProcessTribeFormationAndSplitting();
            if (Tick % 120 == 0) EvolveDiplomacy();
            if (Tick % 400 == 0) ProcessMarkets();
            if (Tick % 240 == 0) ProcessRituals();
            if (Tick % 60 == 0) ProcessRaids();
            if (Tick % 40 == 0) ProcessLeaderAnnouncements();

            // ������ ���������� �������� ������ ����� ��� ��� ������ 300 �����
            if (Env.Year > prevYear || Tick % 300 == 0)
            {
                Evolution.ApplyGroupSelection(Tribes, Humans);
                Evolution.ApplyGeneticDrift(Humans);
                Evolution.RecordGeneration(Humans, Tribes);
            }

            for (int i = _sounds.Count - 1; i >= 0; i--)
            {
                _sounds[i].Age += dt;
                if (_sounds[i].Age > 2.0) _sounds.RemoveAt(i);
            }

            CountDeaths();

            if (Tick % 200 == 0)
            {
                for (int i = Humans.Count - 1; i >= 0; i--)
                {
                    if (!Humans[i].IsAlive)
                    {
                        HumanById.Remove(Humans[i].Id);
                        Humans.RemoveAt(i);
                    }
                }
                for (int i = Animals.Count - 1; i >= 0; i--)
                {
                    if (!Animals[i].IsAlive)
                    {
                        AnimalById.Remove(Animals[i].Id);
                        Animals.RemoveAt(i);
                    }
                }
            }

            if (Tick % 90 == 0)
            {
                int foodCount = 0;
                for (int i = 0; i < Resources.Count; i++)
                {
                    var r = Resources[i];
                    if (r.Amount > 1 && (r.Type == ResourceType.Berry || r.Type == ResourceType.Plant))
                        foodCount++;
                }
                if (foodCount < 160)
                    GenerateResources(45);
            }

            if (Tick % 350 == 0)
            {
                int aliveH = 0, aliveP = 0;
                for (int i = 0; i < Animals.Count; i++)
                {
                    var a = Animals[i];
                    if (!a.IsAlive) continue;
                    if (a.IsHerbivore) aliveH++;
                    else if (a.IsPredator) aliveP++;
                }
                if (aliveH < 18) SpawnAnimals(10, 0);
                if (aliveP < 4) SpawnAnimals(0, 3);
            }

            int pop = 0;
            for (int i = 0; i < Humans.Count; i++)
                if (Humans[i].IsAlive) pop++;
            if (pop > PeakPopulation) PeakPopulation = pop;
        }

        private void UpdateResources(double dt)
        {
            double growth = Env.PlantGrowthRate;
            bool drought = Env.IsDrought;

            for (int i = 0; i < Resources.Count; i++)
            {
                var r = Resources[i];
                if (r.Amount < r.MaxAmount && (r.Type == ResourceType.Plant || r.Type == ResourceType.Berry))
                {
                    r.Amount = Math.Min(r.MaxAmount, r.Amount + r.RegrowthRate * growth * dt);
                }
                if (drought && (r.Type == ResourceType.Plant || r.Type == ResourceType.Berry))
                    r.Amount = Math.Max(0, r.Amount - dt * 0.15);
            }

            if (Tick % 120 == 0)
                Resources.RemoveAll(r => r.Amount <= 0.1 && r.Type != ResourceType.Water);
        }

        private void ProcessBirths()
        {
            _newHumansBuffer.Clear();

            // Carrying capacity: the land feeds only so many mouths — births pause when over capacity.
            double foodSum = 0;
            for (int i = 0; i < Resources.Count; i++)
            {
                var r = Resources[i];
                if (r.Type == ResourceType.Berry || r.Type == ResourceType.Plant) foodSum += r.Amount;
            }
            double foodFactor = Math.Clamp(foodSum / 14000.0, 0.2, 1.0);
            int capacity = (int)(46 + 90 * foodFactor);
            bool overCapacity = Humans.Count >= capacity;

            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (!h.IsAlive || !h.IsPregnant || h.PregnancyProgress < 0.99) continue;
                if (overCapacity) break;                      // еды не хватит — рожать нельзя
                if (h.Hunger > 0.72 || h.Health < h.MaxHealth * 0.5)
                {
                    h.PregnancyProgress = 0.985;              // голодная/больная — роды откладываются
                    continue;
                }

                h.IsPregnant = false;
                h.PregnancyProgress = 0;
                h.ChildrenBorn++;

                if (h.PregnancyPartner != null)
                {
                    var child = Human.CreateChild(h, h.PregnancyPartner,
                        Math.Clamp(h.X + (_rng.NextDouble() - 0.5) * 30, 40, Width - 40),
                        Math.Clamp(h.Y + (_rng.NextDouble() - 0.5) * 30, 40, Height - 40));

                    child.Age = 0;
                    child.Hunger = 0.15;

                    _newHumansBuffer.Add(child);
                    h.Brain.Reward(1.0);
                    h.Brain.Social(0.8);
                    TotalBirths++;

                    if (h.TribeId >= 0 && TribeById.TryGetValue(h.TribeId, out var tribe))
                    {
                        tribe.AddMember(child);
                        tribe.TotalBirths++;
                    }

                                                AddEventAt(h.X, h.Y, Lang.T("birth", h.Name, child.Name), unchecked((int)0xFF7DD4FF));
                    h.PregnancyPartner = null;
                }
            }

            for (int i = 0; i < _newHumansBuffer.Count; i++)
            {
                var ch = _newHumansBuffer[i];
                Humans.Add(ch);
                HumanById[ch.Id] = ch;
            }

            _newAnimalsBuffer.Clear();

            // Carrying capacity for wildlife: predators are limited by prey, prey by vegetation.
            int herbAlive = 0, predAlive = 0;
            for (int i = 0; i < Animals.Count; i++)
            {
                if (!Animals[i].IsAlive) continue;
                if (Animals[i].IsHerbivore) herbAlive++;
                else predAlive++;
            }
            int herbCap = 120, predCap = 26;

            for (int i = 0; i < Animals.Count; i++)
            {
                var a = Animals[i];
                if (!a.IsAlive || !a.IsPregnant || a.PregnancyProgress < 0.99) continue;
                if (a.IsHerbivore ? herbAlive >= herbCap : predAlive >= predCap) break;

                a.IsPregnant = false;
                a.PregnancyProgress = 0;
                a.OffspringCount++;

                Animal father = Context.FindMaleAnimalOfSpecies(a.X, a.Y, a.Id, a.SpeciesName, 250) ?? a;
                var offspring = Animal.CreateOffspring(a, father,
                    Math.Clamp(a.X + (_rng.NextDouble() - 0.5) * 25, 40, Width - 40),
                    Math.Clamp(a.Y + (_rng.NextDouble() - 0.5) * 25, 40, Height - 40));
                _newAnimalsBuffer.Add(offspring);
            }

            for (int i = 0; i < _newAnimalsBuffer.Count; i++)
            {
                var an = _newAnimalsBuffer[i];
                Animals.Add(an);
                AnimalById[an.Id] = an;
            }
        }

        private void ProcessTechDiscovery()
        {
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (!h.IsAlive || h.IsChild) continue;
                int peers = CountNearbyHumans(h.X, h.Y, 160, h.TribeId, true);
                string? discovered = TechnologyTree.TryDiscover(h, _rng, peers);
                if (discovered != null)
                {
                    h.Brain.Reward(0.95);
                    h.Brain.RememberEpisode(h.X, h.Y, EpisodeType.Discovery, 1.0);
                    h.Reputation += 0.35;
                    TechsDiscovered++;

                    var info = TechnologyTree.GetInfo(discovered);
                    string techName = info != null ? info.Name : discovered;

                    if (h.TribeId >= 0 && TribeById.TryGetValue(h.TribeId, out var tribe))
                    {
                        tribe.Technologies.Add(discovered);
                        AddEventAt(h.X, h.Y, Lang.T("tech_tribe", tribe.Name, techName), unchecked((int)0xFF7DFFAB));
                    }
                    else
                    {
                        AddEventAt(h.X, h.Y, Lang.T("tech_solo", h.Name, techName), unchecked((int)0xFF7DFFAB));
                    }
                }
            }
        }

        private void ProcessInteractions(double dt)
        {
            // ������� ������� ����� ���������� + ����������� �������� �������� �������
            for (int i = 0; i < Animals.Count; i++)
            {
                var pred = Animals[i];
                if (!pred.IsAlive || !pred.IsPredator || pred.Hunger < 0.35) continue;

                HumanGrid.GetCellBounds(pred.X, pred.Y, 45, out int minX, out int maxX, out int minY, out int maxY);
                bool attacked = false;
                for (int cy = minY; cy <= maxY && !attacked; cy++)
                {
                    for (int cx = minX; cx <= maxX && !attacked; cx++)
                    {
                        var bucket = HumanGrid.GetBucket(cx, cy);
                        for (int k = 0; k < bucket.Count; k++)
                        {
                            var h = bucket[k].Item;
                            if (!h.IsAlive) continue;
                            if (DistSq(pred.X, pred.Y, h.X, h.Y) < 38 * 38)
                            {
                                h.TakeDamage(pred.Genes.DamageOutput * 0.38, pred.SpeciesName);
                                h.KnownDangerousSpecies.Add(pred.SpeciesName);
                                pred.Hunger = Math.Max(0, pred.Hunger - 0.3);
                                pred.Brain.Satisfy(0.4);
                                attacked = true;

                                // ���������� ��������, ���� �� �������
                                if (h.IsAlive && (h.EquippedWeapon != null || h.SkillFighting > 0.2))
                                {
                                    double counter = h.Genes.PhysicalStrength * (1.0 + h.SkillFighting) *
                                                     (h.EquippedWeapon != null ? h.EquippedWeapon.Modifier : 0.8);
                                    pred.TakeDamage(counter * 0.4, h.Name);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            // ����������� �������� (����� SpatialGrid)
            for (int i = 0; i < Animals.Count; i++)
            {
                var a = Animals[i];
                if (!a.IsAlive || a.IsPregnant || a.MatingCooldown > 0) continue;
                if (a.Sex != Gender.Female || a.Hunger > 0.45 || a.Age < 1.8) continue;

                var mate = Context.FindMaleAnimalOfSpecies(a.X, a.Y, a.Id, a.SpeciesName, 70);
                if (mate != null && mate.MatingCooldown <= 0 && _rng.NextDouble() < 0.025)
                {
                    a.IsPregnant = true;
                    a.MatingCooldown = 50;
                    mate.MatingCooldown = 30;
                }
            }
        }

        private void ProcessTribeFormationAndSplitting()
        {
            // �������� ����� ������������ � ����� ����� ��� �������������� � �������������
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (!h.IsAlive || h.TribeId >= 0) continue;

                for (int t = 0; t < Tribes.Count; t++)
                {
                    var tr = Tribes[t];
                    if (tr.Population > 0 && DistSq(h.X, h.Y, tr.CenterX, tr.CenterY) < tr.TerritoryRadius * tr.TerritoryRadius)
                    {
                        tr.AddMember(h);
                        break;
                    }
                }
            }
        }

        private void CountDeaths()
        {
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (!h.IsAlive && _countedHDeaths.Add(h.Id))
                {
                    TotalDeaths++;
                    if (h.TribeId >= 0 && TribeById.TryGetValue(h.TribeId, out var tr))
                        tr.TotalDeaths++;
                        AddEventAt(h.X, h.Y, Lang.T("death", h.Name, h.Age, h.CauseOfDeath), unchecked((int)0xFFC0B4B4));
                }
            }
            for (int i = 0; i < Animals.Count; i++)
            {
                var a = Animals[i];
                if (!a.IsAlive && _countedADeaths.Add(a.Id))
                    TotalAnimalDeaths++;
            }
        }

        public int CountNearbyHumans(double x, double y, double radius, int tribeId, bool same)
        {
            int c = 0;
            double rSq = radius * radius;
            HumanGrid.GetCellBounds(x, y, radius, out int minX, out int maxX, out int minY, out int maxY);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = HumanGrid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var h = bucket[i].Item;
                        if (!h.IsAlive) continue;
                        if (same && h.TribeId != tribeId) continue;
                        if (!same && h.TribeId == tribeId) continue;
                        if (DistSq(h.X, h.Y, x, y) < rSq) c++;
                    }
                }
            }
            return c;
        }

        public void EmitSound(double x, double y, int signal, double radius, int id)
        {
            if (_sounds.Count < 64)
                _sounds.Add(new SoundEvent { X = x, Y = y, Signal = signal, Radius = radius, EmitterId = id });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DistSq(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        public WorldStats GetStats()
        {
            int pop = 0, preg = 0;
            double sumFit = 0, sumAge = 0;
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                if (!h.IsAlive) continue;
                pop++;
                if (h.IsPregnant) preg++;
                sumFit += h.CalculateFitness();
                sumAge += h.Age;
            }

            int animCount = 0, herbCount = 0, predCount = 0;
            for (int i = 0; i < Animals.Count; i++)
            {
                var a = Animals[i];
                if (!a.IsAlive) continue;
                animCount++;
                if (a.IsHerbivore) herbCount++;
                else if (a.IsPredator) predCount++;
            }

            int activeTribes = 0;
            var allTechs = new HashSet<string>();
            for (int i = 0; i < Tribes.Count; i++)
            {
                var t = Tribes[i];
                if (t.MemberIds.Count > 0)
                {
                    activeTribes++;
                    foreach (var tech in t.Technologies)
                        allTechs.Add(tech);
                }
            }

            int resCount = 0;
            for (int i = 0; i < Resources.Count; i++)
                if (Resources[i].Amount > 1) resCount++;

            return new WorldStats
            {
                Population = pop,
                AnimalCount = animCount,
                HerbivoreCount = herbCount,
                PredatorCount = predCount,
                TribeCount = activeTribes,
                AverageFitness = pop > 0 ? sumFit / pop : 0,
                AverageAge = pop > 0 ? sumAge / pop : 0,
                TotalTechs = allTechs.Count,
                Season = Env.CurrentSeason,
                Temperature = Env.Temperature,
                Year = Env.Year,
                TotalBirths = TotalBirths,
                TotalDeaths = TotalDeaths,
                PeakPopulation = PeakPopulation,
                WordsInLanguage = Language.ActiveWords,
                ResourceCount = resCount,
                PregnantCount = preg
            };
        }
    }


public class WorldContext
    {
        public double Width, Height;
        public double DayLight, Temperature;
        public Season Season;
        public Weather Weather;
        public World World = null!;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DistSq(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        public double RayCast(double x, double y, double angle, double maxDist)
        {
            const double step = 20;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            for (double d = step; d < maxDist; d += step)
            {
                double px = x + cos * d;
                double py = y + sin * d;
                if (px < 0 || px > Width || py < 0 || py > Height) return d;
                for (int i = 0; i < World.Buildings.Count; i++)
                {
                    var b = World.Buildings[i];
                    if (Math.Abs(b.X - px) < b.Size && Math.Abs(b.Y - py) < b.Size)
                        return d;
                }
            }
            return maxDist;
        }

        public (double angle, double dist) NearestFood(double x, double y, double range)
        {
            double bestSq = range * range;
            ResourceNode? bestNode = null;
            var grid = World.ResourceGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var r = bucket[i].Item;
                        if (r.Type != ResourceType.Berry && r.Type != ResourceType.Plant) continue;
                        if (r.Amount < 1) continue;
                        double dSq = DistSq(x, y, r.X, r.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestNode = r;
                        }
                    }
                }
            }

            if (bestNode == null) return (0, range);
            return (Math.Atan2(bestNode.Y - y, bestNode.X - x), Math.Sqrt(bestSq));
        }

        public (double angle, double dist, int id) NearestHuman(double x, double y, int excludeId, double range)
        {
            double bestSq = range * range;
            Human? bestH = null;
            var grid = World.HumanGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var h = bucket[i].Item;
                        if (!h.IsAlive || h.Id == excludeId) continue;
                        double dSq = DistSq(x, y, h.X, h.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestH = h;
                        }
                    }
                }
            }

            if (bestH == null) return (0, range, -1);
            return (Math.Atan2(bestH.Y - y, bestH.X - x), Math.Sqrt(bestSq), bestH.Id);
        }

        public (double angle, double dist, bool isPredator) NearestAnimal(double x, double y, double range)
        {
            double bestSq = range * range;
            Animal? bestA = null;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestA = a;
                        }
                    }
                }
            }

            if (bestA == null) return (0, range, false);
            return (Math.Atan2(bestA.Y - y, bestA.X - x), Math.Sqrt(bestSq), bestA.IsPredator);
        }

        public (double angle, double dist) NearestFoodForAnimal(double x, double y, double range, bool isPredator)
        {
            if (!isPredator) return NearestFood(x, y, range);

            double bestSq = range * range;
            Animal? bestPrey = null;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || a.IsPredator) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestPrey = a;
                        }
                    }
                }
            }

            if (bestPrey == null) return (0, range);
            return (Math.Atan2(bestPrey.Y - y, bestPrey.X - x), Math.Sqrt(bestSq));
        }

        public (double angle, double dist) NearestThreat(double x, double y, double range, bool iAmPredator)
        {
            if (iAmPredator) return (0, range);

            double bestSq = range * range;
            double bestX = 0, bestY = 0;
            bool found = false;

            var aGrid = World.AnimalGrid;
            aGrid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = aGrid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || !a.IsPredator) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestX = a.X;
                            bestY = a.Y;
                            found = true;
                        }
                    }
                }
            }

            double hRange = range * 0.7;
            var hGrid = World.HumanGrid;
            hGrid.GetCellBounds(x, y, hRange, out minX, out maxX, out minY, out maxY);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = hGrid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var h = bucket[i].Item;
                        if (!h.IsAlive) continue;
                        double dSq = DistSq(x, y, h.X, h.Y);
                        if (dSq < bestSq * 0.5)
                        {
                            bestSq = dSq;
                            bestX = h.X;
                            bestY = h.Y;
                            found = true;
                        }
                    }
                }
            }

            if (!found) return (0, range);
            return (Math.Atan2(bestY - y, bestX - x), Math.Sqrt(bestSq));
        }

        public (double angle, double dist) NearestPackMember(double x, double y, int myId, int packId, double range)
        {
            if (packId < 0) return (0, range);
            double bestSq = range * range;
            Animal? bestA = null;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || a.Id == myId || a.PackId != packId) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestA = a;
                        }
                    }
                }
            }

            if (bestA == null) return (0, range);
            return (Math.Atan2(bestA.Y - y, bestA.X - x), Math.Sqrt(bestSq));
        }

        public Animal? FindNearestDangerousAnimal(double x, double y, double range, HashSet<string>? knownDangerous)
        {
            double bestSq = range * range;
            Animal? bestA = null;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || a.IsDomesticated) continue;
                        if (!a.IsPredator && (knownDangerous == null || !knownDangerous.Contains(a.SpeciesName))) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestA = a;
                        }
                    }
                }
            }
            return bestA;
        }

        public Animal? FindNearestPrey(double x, double y, double range)
        {
            double bestSq = range * range;
            Animal? bestA = null;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || a.IsPredator || a.IsDomesticated) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestA = a;
                        }
                    }
                }
            }
            return bestA;
        }

        public Animal? FindNearbyTameableAnimal(double x, double y, double range)
        {
            double bestSq = range * range;
            Animal? bestA = null;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || a.IsPredator || a.IsDomesticated) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestA = a;
                        }
                    }
                }
            }
            return bestA;
        }

        public Animal? FindMaleAnimalOfSpecies(double x, double y, int excludeId, string species, double range)
        {
            double bestSq = range * range;
            Animal? bestA = null;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || a.Id == excludeId || a.Sex != Gender.Male || a.SpeciesName != species) continue;
                        double dSq = DistSq(x, y, a.X, a.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestA = a;
                        }
                    }
                }
            }
            return bestA;
        }

        public Building? FindNearestBuilding(double x, double y, int tribeId, double range)
        {
            double bestSq = range * range;
            Building? bestB = null;
            var grid = World.BuildingGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var b = bucket[i].Item;
                        if (b.TribeId != tribeId && tribeId >= 0) continue;
                        double dSq = DistSq(x, y, b.X, b.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            bestB = b;
                        }
                    }
                }
            }
            return bestB;
        }

        public bool IsNearFirepit(double x, double y, double range)
        {
            double rSq = range * range;
            var grid = World.BuildingGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var b = bucket[i].Item;
                        if ((b.Type == BuildingType.Firepit || b.Type == BuildingType.Hut) && DistSq(x, y, b.X, b.Y) < rSq)
                            return true;
                    }
                }
            }
            return false;
        }

        public Human? FindBestMate(Human seeker, double range)
        {
            Human? best = null;
            double bestScore = -1;
            double rSq = range * range;
            var grid = World.HumanGrid;
            grid.GetCellBounds(seeker.X, seeker.Y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var h = bucket[i].Item;
                        if (!h.IsAlive || h.Id == seeker.Id) continue;
                        if (!h.IsAdult || h.IsPregnant || h.Sex == seeker.Sex) continue;
                        if (h.Hunger > 0.65 || h.Health < h.MaxHealth * 0.3) continue;
                        double dSq = DistSq(seeker.X, seeker.Y, h.X, h.Y);
                        if (dSq > rSq) continue;

                        double dist = Math.Sqrt(dSq);
                        double score = 10.0 - dist * 0.02 + seeker.GetRelationStrength(h.Id) * 5.0 +
                                       (h.Health / h.MaxHealth) * 3.0 + (1.0 - h.Hunger) * 2.0 +
                                       (h.IsLeader ? 2.0 : 0.0);
                        if (seeker.GetGeneticDistance(h) < 0.04) score -= 10.0;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = h;
                        }
                    }
                }
            }
            return bestScore > 2.5 ? best : null;
        }

        public Human? FindNearbyChild(double x, double y, int excludeId, double range)
        {
            double bestSq = range * range;
            Human? best = null;
            var grid = World.HumanGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var h = bucket[i].Item;
                        if (!h.IsAlive || h.Id == excludeId || !h.IsChild) continue;
                        double dSq = DistSq(x, y, h.X, h.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            best = h;
                        }
                    }
                }
            }
            return best;
        }

        public Human? FindNearbyTrader(double x, double y, int excludeId, int myTribeId, double range)
        {
            double bestSq = range * range;
            Human? best = null;
            var grid = World.HumanGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var h = bucket[i].Item;
                        if (!h.IsAlive || h.Id == excludeId || !h.IsAdult) continue;
                        double dSq = DistSq(x, y, h.X, h.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            best = h;
                        }
                    }
                }
            }
            return best;
        }

        public ResourceNode? FindNearestMaterial(double x, double y, double range)
        {
            double bestSq = range * range;
            ResourceNode? best = null;
            var grid = World.ResourceGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var r = bucket[i].Item;
                        if (r.Amount < 3 || (r.Type != ResourceType.Stone && r.Type != ResourceType.Wood)) continue;
                        double dSq = DistSq(x, y, r.X, r.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            best = r;
                        }
                    }
                }
            }
            return best;
        }

        public (ResourceType type, double amount)? TryHarvestMaterial(double x, double y, double range)
        {
            double rSq = range * range;
            var grid = World.ResourceGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var r = bucket[i].Item;
                        if (r.Amount < 1 || (r.Type != ResourceType.Stone && r.Type != ResourceType.Wood)) continue;
                        if (DistSq(x, y, r.X, r.Y) < rSq)
                        {
                            double taken = Math.Min(r.Amount, 6.0);
                            r.Amount -= taken;
                            return (r.Type, taken);
                        }
                    }
                }
            }
            return null;
        }

        public int CountNearbyHumans(double x, double y, double r, int tribeId, bool same) =>
            World.CountNearbyHumans(x, y, r, tribeId, same);

        public Human? GetNearestHuman(double x, double y, int excludeId, double range)
        {
            double bestSq = range * range;
            Human? best = null;
            var grid = World.HumanGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var h = bucket[i].Item;
                        if (!h.IsAlive || h.Id == excludeId) continue;
                        double dSq = DistSq(x, y, h.X, h.Y);
                        if (dSq < bestSq)
                        {
                            bestSq = dSq;
                            best = h;
                        }
                    }
                }
            }
            return best;
        }

        public Human? GetTribalLeader(int tribeId)
        {
            var tribe = World.GetTribeById(tribeId);
            if (tribe == null || tribe.LeaderId < 0) return null;
            var leader = World.GetHumanById(tribe.LeaderId);
            return leader != null && leader.IsAlive ? leader : null;
        }

        public double? TryGetFood(double x, double y, double range)
        {
            double rSq = range * range;
            var grid = World.ResourceGrid;
            grid.GetCellBounds(x, y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var r = bucket[i].Item;
                        if (r.Amount < 1 || (r.Type != ResourceType.Berry && r.Type != ResourceType.Plant)) continue;
                        if (DistSq(x, y, r.X, r.Y) < rSq)
                        {
                            double taken = Math.Min(r.Amount, 12.0);
                            r.Amount -= taken;
                            return taken;
                        }
                    }
                }
            }
            return null;
        }

        public double? TryGetPlantFood(double x, double y, double range) =>
            TryGetFood(x, y, range);

        public void TryAttack(Human attacker, double power, double range)
        {
            double rSq = range * range;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(attacker.X, attacker.Y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive) continue;
                        if (DistSq(attacker.X, attacker.Y, a.X, a.Y) < rSq)
                        {
                            a.TakeDamage(power, "Human");
                            attacker.Brain.Reward(0.2);
                            if (!a.IsAlive)
                            {
                                attacker.Eat(a.Genes.Size * 18);
                                attacker.Brain.Reward(0.5);
                                attacker.SkillHunting = Math.Min(1.0, attacker.SkillHunting + 0.015);
                                attacker.BattlesWon++;
                                attacker.AnimalsKilled++;
                            }
                            return;
                        }
                    }
                }
            }
        }

        public Animal? TryAnimalAttack(Animal predator, double power, double range)
        {
            double rSq = range * range;
            var grid = World.AnimalGrid;
            grid.GetCellBounds(predator.X, predator.Y, range, out int minX, out int maxX, out int minY, out int maxY);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var a = bucket[i].Item;
                        if (!a.IsAlive || a.Id == predator.Id || a.IsPredator) continue;
                        if (DistSq(predator.X, predator.Y, a.X, a.Y) < rSq)
                        {
                            a.TakeDamage(power, predator.SpeciesName);
                            if (!a.IsAlive) return a;
                        }
                    }
                }
            }
            return null;
        }

        bool TribeHasBuilding(int tribeId, BuildingType type)
        {
            var bs = World.Buildings;
            for (int i = 0; i < bs.Count; i++)
                if (bs[i].TribeId == tribeId && bs[i].Type == type) return true;
            return false;
        }

        public bool TryBuild(double x, double y, Human builder)
        {
            var grid = World.BuildingGrid;
            grid.GetCellBounds(x, y, 55, out int minX, out int maxX, out int minY, out int maxY);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var bucket = grid.GetBucket(cx, cy);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        if (DistSq(bucket[i].Item.X, bucket[i].Item.Y, x, y) < 48 * 48)
                            return false;
                    }
                }
            }

            var tribe = builder.TribeId >= 0 ? World.GetTribeById(builder.TribeId) : null;

            BuildingType bType = BuildingType.Hut;
            double sz = 20;
            if (World.Env.IsWater(x, y)) return false;   // не строить в воде
            if (!IsNearFirepit(x, y, 160) && builder.KnownTechnologies.Contains("fire_control"))
            {
                bType = BuildingType.Firepit;
                sz = 15;
            }
            else if (builder.KnownTechnologies.Contains("pottery") && Random.Shared.NextDouble() < 0.3)
            {
                bType = BuildingType.StorageHut;
                sz = 22;
            }
            else if (builder.KnownTechnologies.Contains("stone_wall") && Random.Shared.NextDouble() < 0.25)
            {
                bType = BuildingType.Wall;
                sz = 24;
            }
            else if (tribe != null && !TribeHasBuilding(builder.TribeId, BuildingType.Totem)
                     && tribe.CultureLevel >= 1.0 && Random.Shared.NextDouble() < 0.45)
            {
                bType = BuildingType.Totem;
                sz = 20;
                World.AddEventThrottled("totem" + builder.TribeId, x, y,
                    Lang.T("totem_built", tribe.Name), unchecked((int)0xFFB080FF), 120);
            }
            else if (tribe != null && !TribeHasBuilding(builder.TribeId, BuildingType.Market)
                     && tribe.FoodStorage >= 35 && tribe.ToolCount >= 2
                     && Random.Shared.NextDouble() < 0.45)
            {
                bType = BuildingType.Market;
                sz = 22;
                World.AddEventThrottled("market" + builder.TribeId, x, y,
                    Lang.T("market_built", tribe.Name), unchecked((int)0xFFFFC870), 120);
            }

            var nb = new Building
            {
                X = x,
                Y = y,
                Type = bType,
                BuilderId = builder.Id,
                TribeId = builder.TribeId,
                Health = 120,
                Size = sz
            };
            World.Buildings.Add(nb);
            World.BuildingGrid.Insert(nb, x, y);
            return true;
        }

        public (double X, double Y)? GetPackCenter(int packId)
        {
            if (World.PackCenters.TryGetValue(packId, out var c) && c.Count > 0)
                return (c.X, c.Y);
            return null;
        }

        public void EmitSound(double x, double y, int signal, double radius, int id) =>
            World.EmitSound(x, y, signal, radius, id);
    }

    public class ResourceNode
    {
        public double X, Y;
        public ResourceType Type;
        public double Amount, MaxAmount, RegrowthRate;
    }

    public enum ResourceType { Berry, Plant, Stone, Wood, Water, Ore }

    public class Building
    {
        public double X, Y;
        public BuildingType Type;
        public int BuilderId, TribeId;
        public double Health, Size;
    }

    public enum BuildingType { Hut, Firepit, StorageHut, Wall, Pen, Market, Totem }

    internal class SoundEvent
    {
        public double X, Y;
        public int Signal;
        public double Radius;
        public int EmitterId;
        public double Age;
    }


public class WorldStats
    {
        public int Population, AnimalCount, HerbivoreCount, PredatorCount;
        public int TribeCount, TotalTechs;
        public double AverageFitness, AverageAge;
        public Season Season;
        public double Temperature;
        public int Year, TotalBirths, TotalDeaths, PeakPopulation;
        public int WordsInLanguage, ResourceCount, PregnantCount;
    }
}

