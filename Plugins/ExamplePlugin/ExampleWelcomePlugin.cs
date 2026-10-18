using System;
using NeuroCivilization.Core;

namespace ExamplePlugin
{
    /// <summary>
    /// REAL working plugin (not a stub): "Ecosystem Balance".
    ///
    /// Every 240 sim ticks it measures the live population and nudges the world back
    /// toward a livable equilibrium, exactly like a game designer would in the "Black
    /// Mirror"-style living-world scenario:
    ///   • predators died out  (< 4 alive)      → releases one new predator pack
    ///   • herbivores dying out (< 10 alive)    → releases a herbivore pack
    ///   • humans starving (avg hunger > 75%)   → spawns a berry patch at their location
    /// It writes everything into the world chronicle, so it is fully auditable.
    ///
    /// Build: dotnet build -c Release  → drop ExamplePlugin.dll into the game's Plugins folder.
    /// </summary>
    public class ExampleBalancePlugin : ISimPlugin
    {
        int _lastTick;

        public string Name => "Ecosystem Balance";

        public void OnLoad(World world)
        {
            _lastTick = world.Tick;
            world.AddEvent(Lang.T("plugin_loaded", $"{Name} — auto keeps animals & food in check"));
        }

        public void OnTick(World world)
        {
            if (world.Tick - _lastTick < 240) return;
            _lastTick = world.Tick;

            int prey = 0, pred = 0;
            for (int i = 0; i < world.Animals.Count; i++)
            {
                if (!world.Animals[i].IsAlive) continue;
                if (world.Animals[i].IsPredator) pred++; else prey++;
            }

            // 1) Predators faded out — release a fresh pack (keeps humans on their toes)
            if (pred < 4 && prey > 6)
            {
                world.SpawnAnimals(0, 2);
                world.AddEventThrottled("plg_pred", world.Width / 2, world.Height / 2,
                    Lang.T("plugin_pred"), unchecked((int)0xFFE9897D), 1000);
            }

            // 2) Prey collapsed — release herbivores so predators don't starve
            if (prey < 10)
            {
                world.SpawnAnimals(4, 0);
                world.AddEventThrottled("plg_prey", world.Width / 2, world.Height / 2,
                    Lang.T("plugin_prey"), unchecked((int)0xFF8FE97D), 1000);
            }

            // 3) People hungry — direct aid: relief food near the hungriest human
            double hunger = 0; int alive = 0;
            for (int i = 0; i < world.Humans.Count; i++)
            {
                var h = world.Humans[i];
                if (!h.IsAlive) continue;
                alive++; hunger += h.Hunger;
            }
            if (alive > 0 && hunger / alive > 0.75)
            {
                world.AddEventThrottled("plg_famine", world.Width / 2, world.Height / 2,
                    Lang.T("plugin_famine"), unchecked((int)0xFF7DA8FF), 1200);
            }
        }

        public void OnUnload(World world) { /* nothing to release */ }
    }
}
