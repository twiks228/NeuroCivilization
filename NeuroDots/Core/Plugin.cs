using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Contract for user behavior plugins ("Black Mirror"-style living world).
    /// Build a class library referencing NeuroCivilization.dll, implement this interface
    /// in one public class, drop the compiled DLL into the "<AppDir>\Plugins" folder and
    /// it is picked up automatically on start (and on every 'N' reset).
    /// </summary>
    public interface ISimPlugin
    {
        /// <summary>Shown in the world chronicle on load.</summary>
        string Name { get; }

        /// <summary>Called once after plugins are discovered (also after each world reset).</summary>
        void OnLoad(World world);

        /// <summary>Called every simulation tick (after the world update).</summary>
        void OnTick(World world);

        /// <summary>Called when the world is reset / app closes.</summary>
        void OnUnload(World world);
    }

    /// <summary>Discovers, loads and drives <see cref="ISimPlugin"/> DLLs from "Plugins/".</summary>
    public static class PluginHost
    {
        static readonly List<ISimPlugin> Loaded = new();
        public static IReadOnlyList<ISimPlugin> Plugins => Loaded;

        public static void LoadAll(World world)
        {
            UnloadAll(world);
            string dir = Path.Combine(AppContext.BaseDirectory, "Plugins");
            if (!Directory.Exists(dir)) return;
            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    foreach (var t in asm.GetExportedTypes())
                        if (typeof(ISimPlugin).IsAssignableFrom(t) && !t.IsAbstract &&
                            Activator.CreateInstance(t) is ISimPlugin p)
                        {
                            p.OnLoad(world);
                            Loaded.Add(p);
                            world.AddEvent(Lang.T("plugin_loaded", p.Name));
                        }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[Plugins] " + dll + ": " + ex.Message);
                }
            }
        }

        public static void TickAll(World world)
        {
            for (int i = 0; i < Loaded.Count; i++)
            {
                try { Loaded[i].OnTick(world); }
                catch (Exception ex) { Console.Error.WriteLine("[Plugins:" + Loaded[i].Name + "] " + ex.Message); }
            }
        }

        public static void UnloadAll(World world)
        {
            for (int i = 0; i < Loaded.Count; i++)
            {
                try { Loaded[i].OnUnload(world); } catch { /* plugin errors must never break the sim */ }
            }
            Loaded.Clear();
        }
    }
}
