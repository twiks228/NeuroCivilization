using System;
using System.Collections.Generic;
using System.Linq;

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

        public Environment Env;
        public LanguageSystem Language;
        public WorldContext Context;

        public int Tick;
        public double TotalTime;
        public int TotalBirths, TotalDeaths, TotalAnimalDeaths;
        public int PeakPopulation, TechsDiscovered;

        HashSet<int> countedHDeaths = new(), countedADeaths = new();
        List<SoundEvent> sounds = new();
        Random rng;

        public World(int seed = 0)
        {
            rng = seed == 0 ? new Random() : new Random(seed);
            Env = new Environment(rng);
            Language = new LanguageSystem();
            Context = new WorldContext { Width = Width, Height = Height, World = this };
            GenerateResources(350);
        }

        public void SpawnHumans(int count)
        {
            int groupSize = Math.Max(5, count / 4);
            int groups = count / groupSize;

            for (int g = 0; g < groups; g++)
            {
                double cx = 200 + rng.NextDouble() * (Width - 400);
                double cy = 200 + rng.NextDouble() * (Height - 400);

                var tribe = new Tribe();
                Tribes.Add(tribe);

                for (int i = 0; i < groupSize; i++)
                {
                    double x = cx + (rng.NextDouble() - 0.5) * 120;
                    double y = cy + (rng.NextDouble() - 0.5) * 120;
                    var human = new Human(x, y);
                    human.KnownTechnologies.Add("language_proto");
                    human.KnownTechnologies.Add("stone_tools");
                    if (rng.NextDouble() < 0.4) human.KnownTechnologies.Add("fire_control");
                    if (rng.NextDouble() < 0.3) human.KnownTechnologies.Add("shelter_basic");
                    human.Hunger = 0.05 + rng.NextDouble() * 0.1;
                    // Половина начинают взрослыми для размножения
                    human.Age = 18 + rng.NextDouble() * 15;
                    Humans.Add(human);
                    tribe.AddMember(human);
                }
            }

            int remaining = count - groups * groupSize;
            for (int i = 0; i < remaining; i++)
            {
                var h = new Human(rng.NextDouble() * Width, rng.NextDouble() * Height);
                h.Hunger = 0.05;
                h.Age = 18 + rng.NextDouble() * 10;
                Humans.Add(h);
            }
        }

        public void SpawnAnimals(int herbivores, int predators)
        {
            string[] herbNames = { "Deer", "Rabbit", "Goat", "Bison", "Horse" };
            string[] predNames = { "Wolf", "Bear", "Lion", "Hyena" };

            for (int i = 0; i < herbivores; i++)
            {
                var g = new AnimalGenome();
                g.Aggression = rng.NextDouble() * 0.3;
                g.AttackPower = rng.NextDouble() * 0.3;
                g.Speed = 50 + rng.NextDouble() * 60;
                var a = new Animal(rng.NextDouble() * Width,
                    rng.NextDouble() * Height, g, null,
                    herbNames[rng.Next(herbNames.Length)]);
                a.IsHerbivore = true; a.IsPredator = false;
                a.Hunger = 0.1 + rng.NextDouble() * 0.2;
                Animals.Add(a);
            }

            for (int i = 0; i < predators; i++)
            {
                var g = new AnimalGenome();
                g.Aggression = 0.5 + rng.NextDouble() * 0.5;
                g.AttackPower = 0.5 + rng.NextDouble() * 0.5;
                g.Speed = 60 + rng.NextDouble() * 60;
                var a = new Animal(rng.NextDouble() * Width,
                    rng.NextDouble() * Height, g, null,
                    predNames[rng.Next(predNames.Length)]);
                a.IsPredator = true; a.IsHerbivore = false;
                a.PackId = i / 3;
                a.Hunger = 0.2;
                Animals.Add(a);
            }
        }

        void GenerateResources(int count)
        {
            for (int i = 0; i < count; i++)
            {
                // 70% еда для выживания
                var type = rng.NextDouble() < 0.4 ? ResourceType.Berry :
                           rng.NextDouble() < 0.7 ? ResourceType.Plant :
                           rng.NextDouble() < 0.85 ? ResourceType.Stone : ResourceType.Wood;
                Resources.Add(new ResourceNode
                {
                    X = rng.NextDouble() * Width,
                    Y = rng.NextDouble() * Height,
                    Type = type,
                    Amount = 50 + rng.NextDouble() * 50,
                    MaxAmount = 100,
                    RegrowthRate = type == ResourceType.Berry ? 0.5 :
                                   type == ResourceType.Plant ? 0.4 : 0.05
                });
            }
        }

        public void Update(double dt)
        {
            Tick++;
            TotalTime += dt;

            Env.Update(dt);
            Context.DayLight = Env.DayLight;
            Context.Temperature = Env.Temperature;
            Context.Season = Env.CurrentSeason;
            Context.Weather = Env.CurrentWeather;

            UpdateResources(dt);

            foreach (var h in Humans)
                if (h.IsAlive) h.Update(dt, Context);

            foreach (var a in Animals)
                if (a.IsAlive) a.Update(dt, Context);

            foreach (var t in Tribes)
                t.Update(dt, Humans);

            Language.Update(dt);

            // FIX: Рождения обрабатываются ЗДЕСЬ, не в Human.Update
            ProcessBirths();

            if (Tick % 50 == 0) ProcessTechDiscovery();
            if (Tick % 5 == 0) ProcessInteractions(dt);

            sounds.RemoveAll(s => s.Age > 2);
            foreach (var s in sounds) s.Age += dt;

            CountDeaths();

            // Очистка мёртвых (оставляем немного для визуализации)
            if (Tick % 200 == 0)
            {
                Humans.RemoveAll(h => !h.IsAlive);
                Animals.RemoveAll(a => !a.IsAlive);
            }

            // Респавн ресурсов — ЧАЩЕ И БОЛЬШЕ
            if (Tick % 100 == 0)
            {
                int foodCount = Resources.Count(r => r.Amount > 1 &&
                    (r.Type == ResourceType.Berry || r.Type == ResourceType.Plant));
                if (foodCount < 150)
                    GenerateResources(40);
            }

            // Респавн животных
            if (Tick % 400 == 0)
            {
                int aliveH = Animals.Count(a => a.IsAlive && a.IsHerbivore);
                int aliveP = Animals.Count(a => a.IsAlive && a.IsPredator);
                if (aliveH < 15) SpawnAnimals(10, 0);
                if (aliveP < 4) SpawnAnimals(0, 3);
            }

            int pop = Humans.Count(h => h.IsAlive);
            if (pop > PeakPopulation) PeakPopulation = pop;
        }

        void UpdateResources(double dt)
        {
            foreach (var r in Resources)
            {
                if (r.Amount < r.MaxAmount &&
                    (r.Type == ResourceType.Plant || r.Type == ResourceType.Berry))
                {
                    r.Amount = Math.Min(r.MaxAmount,
                        r.Amount + r.RegrowthRate * Env.PlantGrowthRate * dt);
                }
                if (Env.IsDrought && (r.Type == ResourceType.Plant || r.Type == ResourceType.Berry))
                    r.Amount = Math.Max(0, r.Amount - dt * 0.2);
            }

            if (Tick % 100 == 0)
                Resources.RemoveAll(r => r.Amount <= 0 && r.Type != ResourceType.Water);
        }

        /// <summary>
        /// FIX: Рождения обрабатываются в World, не в Human!
        /// Human ставит IsPregnant=false, PregnancyProgress=0, ChildrenBorn++
        /// НО ребёнка создаёт World!
        /// </summary>
        void ProcessBirths()
        {
            var newHumans = new List<Human>();

            foreach (var h in Humans)
            {
                if (!h.IsAlive) continue;

                // Проверяем: если ChildrenBorn увеличилось, значит надо создать ребёнка
                // Но лучше напрямую: проверяем PregnancyProgress
                if (h.IsPregnant && h.PregnancyProgress >= 0.99)
                {
                    // Родим ДО того как Human.Update обнулит
                    h.IsPregnant = false;
                    h.PregnancyProgress = 0;
                    h.ChildrenBorn++;

                    if (h.PregnancyPartner != null)
                    {
                        var child = Human.CreateChild(h, h.PregnancyPartner,
                            h.X + (rng.NextDouble() - 0.5) * 30,
                            h.Y + (rng.NextDouble() - 0.5) * 30);

                        // Ребёнок начинает маленьким
                        child.Age = 0;
                        child.Hunger = 0.2;

                        newHumans.Add(child);
                        h.Brain.Reward(1.0);
                        h.Brain.Social(0.8);
                        TotalBirths++;

                        if (h.TribeId >= 0)
                        {
                            var tribe = Tribes.FirstOrDefault(t => t.Id == h.TribeId);
                            tribe?.AddMember(child);
                        }

                        h.PregnancyPartner = null;
                    }
                }
            }
            Humans.AddRange(newHumans);

            // Животные
            var newAnimals = new List<Animal>();
            foreach (var a in Animals)
            {
                if (!a.IsAlive || !a.IsPregnant || a.PregnancyProgress < 0.99) continue;
                a.IsPregnant = false;
                a.PregnancyProgress = 0;
                var father = Animals.FirstOrDefault(o =>
                    o.IsAlive && o.Id != a.Id &&
                    o.SpeciesName == a.SpeciesName &&
                    o.Sex == Gender.Male &&
                    Dist(o.X, o.Y, a.X, a.Y) < 200);
                if (father != null)
                    newAnimals.Add(Animal.CreateOffspring(a, father,
                        a.X + rng.NextDouble() * 20, a.Y + rng.NextDouble() * 20));
            }
            Animals.AddRange(newAnimals);
        }

        void ProcessTechDiscovery()
        {
            foreach (var h in Humans)
            {
                if (!h.IsAlive) continue;
                string discovered = TechnologyTree.TryDiscover(h, rng);
                if (discovered != null)
                {
                    h.Brain.Reward(0.9);
                    h.Reputation += 0.3;
                    TechsDiscovered++;
                    if (h.TribeId >= 0)
                        Tribes.FirstOrDefault(t => t.Id == h.TribeId)?
                            .Technologies.Add(discovered);
                }
            }
        }

        void ProcessInteractions(double dt)
        {
            // Хищники атакуют
            foreach (var pred in Animals)
            {
                if (!pred.IsAlive || !pred.IsPredator || pred.Hunger < 0.4) continue;
                foreach (var h in Humans)
                {
                    if (!h.IsAlive) continue;
                    if (Dist(pred.X, pred.Y, h.X, h.Y) < 35)
                    {
                        h.TakeDamage(pred.Genes.DamageOutput * 0.4, pred.SpeciesName);
                        pred.Hunger = Math.Max(0, pred.Hunger - 0.3);
                        pred.Brain.Satisfy(0.4);
                        break;
                    }
                }
            }

            // Размножение животных
            foreach (var a in Animals)
            {
                if (!a.IsAlive || a.IsPregnant || a.MatingCooldown > 0) continue;
                if (a.Sex != Gender.Female || a.Hunger > 0.4 || a.Age < 2) continue;
                var mate = Animals.FirstOrDefault(o =>
                    o.IsAlive && o.Id != a.Id && o.Sex == Gender.Male &&
                    o.SpeciesName == a.SpeciesName &&
                    Dist(o.X, o.Y, a.X, a.Y) < 60);
                if (mate != null && rng.NextDouble() < 0.015)
                {
                    a.IsPregnant = true;
                    a.MatingCooldown = 50;
                    mate.MatingCooldown = 30;
                }
            }
        }

        void CountDeaths()
        {
            foreach (var h in Humans)
                if (!h.IsAlive && !countedHDeaths.Contains(h.Id))
                { TotalDeaths++; countedHDeaths.Add(h.Id); }
            foreach (var a in Animals)
                if (!a.IsAlive && !countedADeaths.Contains(a.Id))
                { TotalAnimalDeaths++; countedADeaths.Add(a.Id); }
        }

        public int CountNearbyHumans(double x, double y, double radius, int tribeId, bool same)
        {
            int c = 0;
            foreach (var h in Humans)
            {
                if (!h.IsAlive) continue;
                if (same && h.TribeId != tribeId) continue;
                if (!same && h.TribeId == tribeId) continue;
                if (Dist(h.X, h.Y, x, y) < radius) c++;
            }
            return c;
        }

        public void EmitSound(double x, double y, int signal, double radius, int id)
        { sounds.Add(new SoundEvent { X = x, Y = y, Signal = signal, Radius = radius, EmitterId = id }); }

        static double Dist(double x1, double y1, double x2, double y2)
        { double dx = x1 - x2, dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy); }

        public WorldStats GetStats()
        {
            var alive = Humans.Where(h => h.IsAlive).ToList();
            return new WorldStats
            {
                Population = alive.Count,
                AnimalCount = Animals.Count(a => a.IsAlive),
                HerbivoreCount = Animals.Count(a => a.IsAlive && a.IsHerbivore),
                PredatorCount = Animals.Count(a => a.IsAlive && a.IsPredator),
                TribeCount = Tribes.Count(t => t.MemberIds.Count > 0),
                AverageFitness = alive.Count > 0 ? alive.Average(h => h.CalculateFitness()) : 0,
                AverageAge = alive.Count > 0 ? alive.Average(h => h.Age) : 0,
                TotalTechs = Tribes.SelectMany(t => t.Technologies).Distinct().Count(),
                Season = Env.CurrentSeason,
                Temperature = Env.Temperature,
                Year = Env.Year,
                TotalBirths = TotalBirths,
                TotalDeaths = TotalDeaths,
                PeakPopulation = PeakPopulation,
                WordsInLanguage = Language.ActiveWords,
                ResourceCount = Resources.Count(r => r.Amount > 1),
                PregnantCount = alive.Count(h => h.IsPregnant)
            };
        }
    }

    public class WorldContext
    {
        public double Width, Height;
        public double DayLight, Temperature;
        public Season Season;
        public Weather Weather;
        public World World;

        public double RayCast(double x, double y, double angle, double maxDist)
        {
            double step = 20;
            for (double d = step; d < maxDist; d += step)
            {
                double px = x + Math.Cos(angle) * d;
                double py = y + Math.Sin(angle) * d;
                if (px < 0 || px > Width || py < 0 || py > Height) return d;
                foreach (var b in World.Buildings)
                    if (Math.Abs(b.X - px) < b.Size && Math.Abs(b.Y - py) < b.Size)
                        return d;
            }
            return maxDist;
        }

        public (double angle, double dist) NearestFood(double x, double y, double range)
        {
            double best = range; double bA = 0;
            foreach (var r in World.Resources)
            {
                if (r.Type != ResourceType.Berry && r.Type != ResourceType.Plant) continue;
                if (r.Amount < 1) continue;
                double d = Dist(x, y, r.X, r.Y);
                if (d < best) { best = d; bA = Math.Atan2(r.Y - y, r.X - x); }
            }
            return (bA, best);
        }

        public (double angle, double dist, int id) NearestHuman(double x, double y, int excludeId, double range)
        {
            double best = range; double bA = 0; int bId = -1;
            foreach (var h in World.Humans)
            {
                if (!h.IsAlive || h.Id == excludeId) continue;
                double d = Dist(x, y, h.X, h.Y);
                if (d < best) { best = d; bA = Math.Atan2(h.Y - y, h.X - x); bId = h.Id; }
            }
            return (bA, best, bId);
        }

        public (double angle, double dist, bool isPredator) NearestAnimal(double x, double y, double range)
        {
            double best = range; double bA = 0; bool pred = false;
            foreach (var a in World.Animals)
            {
                if (!a.IsAlive) continue;
                double d = Dist(x, y, a.X, a.Y);
                if (d < best) { best = d; bA = Math.Atan2(a.Y - y, a.X - x); pred = a.IsPredator; }
            }
            return (bA, best, pred);
        }

        public (double angle, double dist) NearestFoodForAnimal(double x, double y, double range, bool isPredator)
        {
            double best = range; double bA = 0;
            if (isPredator)
            {
                foreach (var a in World.Animals)
                {
                    if (!a.IsAlive || a.IsPredator) continue;
                    double d = Dist(x, y, a.X, a.Y);
                    if (d < best) { best = d; bA = Math.Atan2(a.Y - y, a.X - x); }
                }
            }
            else
            {
                foreach (var r in World.Resources)
                {
                    if (r.Type != ResourceType.Plant && r.Type != ResourceType.Berry) continue;
                    if (r.Amount < 1) continue;
                    double d = Dist(x, y, r.X, r.Y);
                    if (d < best) { best = d; bA = Math.Atan2(r.Y - y, r.X - x); }
                }
            }
            return (bA, best);
        }

        public (double angle, double dist) NearestThreat(double x, double y, double range, bool iAmPredator)
        {
            double best = range; double bA = 0;
            if (!iAmPredator)
            {
                foreach (var a in World.Animals)
                {
                    if (!a.IsAlive || !a.IsPredator) continue;
                    double d = Dist(x, y, a.X, a.Y);
                    if (d < best) { best = d; bA = Math.Atan2(a.Y - y, a.X - x); }
                }
                foreach (var h in World.Humans)
                {
                    if (!h.IsAlive) continue;
                    double d = Dist(x, y, h.X, h.Y);
                    if (d < best * 0.7) { best = d; bA = Math.Atan2(h.Y - y, h.X - x); }
                }
            }
            return (bA, best);
        }

        public (double angle, double dist) NearestPackMember(double x, double y, int myId, int packId, double range)
        {
            double best = range; double bA = 0;
            if (packId < 0) return (0, range);
            foreach (var a in World.Animals)
            {
                if (!a.IsAlive || a.Id == myId || a.PackId != packId) continue;
                double d = Dist(x, y, a.X, a.Y);
                if (d < best) { best = d; bA = Math.Atan2(a.Y - y, a.X - x); }
            }
            return (bA, best);
        }

        public int CountNearbyHumans(double x, double y, double r, int tribeId, bool same) =>
            World.CountNearbyHumans(x, y, r, tribeId, same);

        public Human GetNearestHuman(double x, double y, int excludeId, double range)
        {
            Human best = null; double bestDist = range;
            foreach (var h in World.Humans)
            {
                if (!h.IsAlive || h.Id == excludeId) continue;
                double d = Dist(x, y, h.X, h.Y);
                if (d < bestDist) { bestDist = d; best = h; }
            }
            return best;
        }

        public Human GetTribalLeader(int tribeId)
        {
            var tribe = World.Tribes.FirstOrDefault(t => t.Id == tribeId);
            if (tribe == null) return null;
            return World.Humans.FirstOrDefault(h => h.Id == tribe.LeaderId && h.IsAlive);
        }

        /// <summary>
        /// FIX: Увеличен радиус + количество еды за раз
        /// </summary>
        public double? TryGetFood(double x, double y, double range)
        {
            foreach (var r in World.Resources)
            {
                if (r.Amount < 1) continue;
                if (r.Type != ResourceType.Berry && r.Type != ResourceType.Plant) continue;
                if (Dist(x, y, r.X, r.Y) < range)
                {
                    double taken = Math.Min(r.Amount, 12); // Было 8
                    r.Amount -= taken;
                    return taken;
                }
            }
            return null;
        }

        public double? TryGetPlantFood(double x, double y, double range) =>
            TryGetFood(x, y, range);

        public void TryAttack(Human attacker, double power, double range)
        {
            foreach (var a in World.Animals)
            {
                if (!a.IsAlive) continue;
                if (Dist(attacker.X, attacker.Y, a.X, a.Y) < range)
                {
                    a.TakeDamage(power, "Human");
                    attacker.Brain.Reward(0.2);
                    if (!a.IsAlive)
                    {
                        attacker.Eat(a.Genes.Size * 18);
                        attacker.Brain.Reward(0.5);
                        attacker.SkillHunting = Math.Min(1, attacker.SkillHunting + 0.015);
                        attacker.BattlesWon++;
                        attacker.AnimalsKilled++;
                    }
                    return;
                }
            }
        }

        public object TryAnimalAttack(Animal predator, double power, double range)
        {
            foreach (var a in World.Animals)
            {
                if (!a.IsAlive || a.Id == predator.Id || a.IsPredator) continue;
                if (Dist(predator.X, predator.Y, a.X, a.Y) < range)
                {
                    a.TakeDamage(power, predator.SpeciesName);
                    if (!a.IsAlive) return a;
                }
            }
            return null;
        }

        public bool TryBuild(double x, double y, Human builder)
        {
            foreach (var b in World.Buildings)
                if (Dist(b.X, b.Y, x, y) < 50) return false;
            World.Buildings.Add(new Building
            { X = x, Y = y, Type = BuildingType.Hut, BuilderId = builder.Id, TribeId = builder.TribeId, Health = 100, Size = 20 });
            return true;
        }

        public (double X, double Y)? GetPackCenter(int packId)
        {
            var members = World.Animals.Where(a => a.IsAlive && a.PackId == packId).ToList();
            if (members.Count == 0) return null;
            return (members.Average(m => m.X), members.Average(m => m.Y));
        }

        public void EmitSound(double x, double y, int signal, double radius, int id) =>
            World.EmitSound(x, y, signal, radius, id);

        static double Dist(double x1, double y1, double x2, double y2)
        { double dx = x1 - x2, dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy); }
    }

    public class ResourceNode
    { public double X, Y; public ResourceType Type; public double Amount, MaxAmount, RegrowthRate; }
    public enum ResourceType { Berry, Plant, Stone, Wood, Water, Ore }
    public class Building
    { public double X, Y; public BuildingType Type; public int BuilderId, TribeId; public double Health, Size; }
    public enum BuildingType { Hut, Firepit, StorageHut, Wall, Pen }
    class SoundEvent { public double X, Y; public int Signal; public double Radius; public int EmitterId; public double Age; }

    public class WorldStats
    {
        public int Population, AnimalCount, HerbivoreCount, PredatorCount;
        public int TribeCount, TotalTechs;
        public double AverageFitness, AverageAge;
        public Season Season; public double Temperature;
        public int Year, TotalBirths, TotalDeaths, PeakPopulation;
        public int WordsInLanguage, ResourceCount, PregnantCount;
    }
}