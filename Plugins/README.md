# Plugins — extend NeuroCivilization behavior

The simulation scans `<app>\Plugins\*.dll` at startup and after every world reset (N).
Every public class implementing `NeuroCivilization.Core.ISimPlugin` in those DLLs is
instantiated and driven by the host — no configuration, no editing of the main project.

```csharp
public interface ISimPlugin
{
    string Name { get; }             // shown in the chronicle on load
    void OnLoad(World world);        // after discovery / after each reset
    void OnTick(World world);        // every simulation tick
    void OnUnload(World world);      // on reset / shutdown
}
```

From `OnTick` you can read and mutate the whole live world: humans, animals, tribes,
resources, buildings, environments — or raise events:

- `world.AddEvent(...)` / `world.AddEventAt(x, y, text)` — chronicle + floating label
- `world.AddEventThrottled(gateKey, x, y, text, argb, gate)` — flood-safe events
- `world.Humans[i].Brain.Reward(0.5)` — reward conditioning from script

## Build the sample

```
cd Plugins/ExamplePlugin
dotnet build -c Release
```

Copy the produced DLL (`bin/Release/net8.0-windows/ExamplePlugin.dll`) into the game's
`Plugins` folder, run the game — you should see `[P] Plugin loaded: Example Welcome`
in the chronicle and a largest-tribe report every ~30 s.

## Rules

- Never throw from `OnTick` — exceptions are caught and written to the debug console;
  the simulation keeps running.
- Ship your DLL against the same .NET runtime (`net8.0-windows`).
- UI strings should go through `Lang.T(key, args)` so your plugin text switches
  together with the EN/RU toggle (hotkey **T**).
