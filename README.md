# 🌍 NeuroCivilization

![C#](https://img.shields.io/badge/Language-C%23-blue)
![Platform](https://img.shields.io/badge/Platform-Windows%20(WinForms)-lightgrey)

**NeuroCivilization** is a complex zero-player Artificial Life simulation written in C#. It simulates a living, breathing ecosystem where AI agents (Humans and Animals) survive, evolve, and form primitive societies using custom Neural Networks and Genetic Algorithms.

## 📸 Screenshots
<img width="1914" height="1020" alt="{603EE807-9F6A-4102-92C0-C8D77B7C77F6}" src="https://github.com/user-attachments/assets/9c026d4c-ac96-47ce-ba06-9a76615afd6a" />
<img width="1916" height="1017" alt="{C7C3B1F9-4E36-492A-A561-2B54CF868002}" src="https://github.com/user-attachments/assets/57543f6d-1779-4a9e-a0f9-43b88584e786" />
<img width="1912" height="991" alt="{A291E7F9-6F13-4D34-B411-E314E57C8DEE}" src="https://github.com/user-attachments/assets/5c0898c6-6e8d-4def-8224-684884f33b5a" />


## 🆕 What's New in This Build
* **Dashboard-style UI overhaul** — sidebar with sections + capsule switches, rounded
  frosted-glass cards, capsule progress bars, top-bar status pill (details below).
* **Population balance fixes** — agents no longer teleport across the map (base run/walk
  speeds lowered ~3×, water applies drag in physics), and a carrying-capacity system
  stops the birth explosion: stable population, ongoing births *and* deaths.
* **No more water bugs** — agents refuse to sleep / build / craft / rest / heal while in
  water and wade to the nearest shore instead (`WadeToShore` + land ring-scan); building
  is impossible mid-water.
* **Seamless tile system** — 30 variant tiles across biomes with baked decor replaced
  the old flat textures; regenerate with `tools/gen_tiles`.

## ✨ Key Features
* **🧠 Custom Neural Networks:** Agents make decisions based on inputs (vision, hunger, pain) processed through a FastBrain network, modulated by simulated neurochemicals (Dopamine, Serotonin, Cortisol).
* **🧬 Advanced Genetics & Epigenetics:** A deep genetic system including inheritance, mutations, telomere aging, and *epigenetic memory* (stress, famine, and trauma affect gene expression in descendants).
* **🏹 AI Ecosystem:** Herbivores and predators roam the map, form packs, and breed. Humans hunt, gather, craft tools, and defend themselves.
* **⛺ Tribe Formation:** Humans form relationships, build shelters, teach each other, invent technologies, and establish tribal territories.
* **⛈️ Dynamic World:** Day/night cycles, changing seasons, and weather events (Rain, Storm, Snow, Heatwaves) directly impact agent survival.
* **🏞 Rivers & Lakes:** 2–3 winding rivers flow edge-to-edge plus 2 lakes (with sandy
  shore banks), giving ≈6–8% water coverage. Rivers are drink points, berries grow on
  their banks, fording deep water slows movement, and agents refuse to sleep, build,
  craft, rest or heal while standing in water — they wade back to shore first.
* **🧮 Population Balance:** births are gated by a *carrying capacity* (human births scale
  with food surplus of the tribe's storage, hungry/sick mothers delay pregnancy), so the
  population settles around ~140–190 instead of exploding; wildlife is capped by
  environment capacity for herbivores and predators.
* **🌳 Living Terrain:** Every tile is decorated with baked-in nature sprites — leafy trees, pines, rocks, cacti and swamp reeds (zero runtime cost, stable per tile).
* **🕊 Diplomacy (5 stages):** War / Hostile / Neutral / Trade / Alliance between tribes, visible in the tribe panel and the chronicle; wars spawn raids that plunder supplies.
* **🛖 Totems & Markets:** New buildable structures — totem poles (ritual dances raise morale/cohesion) and markets (automatic trade deals between trading tribes).
* **👑 Leader Lineage:** Each leader names an heir; inheritance is automatic — no constant re-election spam.
* **✨ Particle World:** Fire smoke & sparks, running dust, rain/snow as real particles, autumn leaves, ash in storms, rising ghosts on death, hearts on births, spirit wisps at totems.
* **🎛 Observation Tools:** Click a territory → tribe panel (leader, technologies, diplomacy, storage); pause + click an agent → OBSERVE panel with its recent decisions; per-tribe population graph lines; filter drawer (only predators / only pregnant / single tribe).
* **🌐 Two Languages:** the whole UI (top bar, panels, legend, chronicle events) is
  localized via `Core/Lang.cs` — English by default, `T` (or the drawer row) switches
  to Русский at runtime.

## 🖥 Dashboard UI

The interface is styled as a modern dark dashboard (frosted-glass cards, large radii,
soft gradients):

* **Sidebar (open by default, toggle with the rounded ☰ button or ESC):** a smooth
  slide-out panel grouped into **DISPLAY / FILTERS / SIMULATION / SYSTEM** sections.
  Every toggle is a real dashboard row — colored accent dot, rounded *active pill*
  highlight on hover, and a **capsule on/off switch** (green when enabled). Action
  rows (speed, tribe filter cycling, new world) show a `›` chevron. The row height
  adapts automatically so the whole sidebar always fits the window, a subtle blue
  accent bar runs down its left edge, and the header carries a live "recording" dot.
* **Cards, not boxes:** every info panel (Civilization Overview, World Chronicle,
  selection/tribe/graph/minimap panels) is a rounded 14-px card with a vertical
  dark gradient and a thin blue rim; panel headers get a soft accent gradient strip.
  While the sidebar is open, the left cards slide right, so nothing is overlapped.
* **Capsule progress bars:** HP / satiety / need bars in all panels are drawn as
  rounded capsules (color-coded red→green), no flat rectangles anywhere.
* **Top bar:** app title + subtitle line (season • time-of-day • weather • temperature)
  next to the ☰ tab, compact one-line world stats, and a rounded **status pill** on the
  right with a live dot — green `▶ xN` while running, amber `⏸ PAUSED` when paused.
* **Smooth motion only:** the sidebar slide animation and row hover highlights are
  eased and capped, so nothing moves "crazily" at 1× simulation speed.

## 🎮 Controls
* `WASD` / `Arrows` - Move Camera
* `Mouse Wheel` - Zoom In / Out
* `Left Click` - Select an Agent / Animal (shows detailed brain/gene stats)
* `Right Click + Drag` - Pan Camera
* `SPACE` - Pause / Resume simulation
* `+` / `-` - Change simulation speed
* `F` - Follow selected agent
* `TAB` - Cycle through alive humans
* `T` - Toggle UI language (English ↔ Русский)
* `☰` (rounded tab, top-left) - Dashboard sidebar: every panel / label / popup /
  language / speed / pause is a clickable row with a dashboard switch — no hotkeys
  needed (the drawer slides open smoothly; ESC closes it)
* `N` - New world
* `F1 - F12` - Toggle UI panels (Map, Stats, Info, Weather, Territories, etc.)

## 🎨 Sprites & Credits
**World tiles** — `Assets/{grass,forest,sand,snow,water,oasis}_{0..4}.png`: seamless
procedurally generated 128×128 textures (5 variants per biome, picked per tile with an
avalanche hash so the map never looks tiled), with baked-in nature decor (trees, pines,
rocks, cacti, reeds), sandy shore banks along rivers/lakes and smooth macro-lighting
that hides the tile grid. Regenerate them deterministically with
`dotnet run --project tools/gen_tiles`.

**Animals** use the CC0 **"Seasons of Forest"** starter sprite pack by inkBubi
(5 species: doe / fox / frog / owl / squirrel; idle + run animations in 4 directions).
**Each of the 9 species is visually unique** — per-species coat tint + body scale
are baked from the CC0 frames at load (`Rabbit` pale small squirrel, `Bison` dark
massive deer, `Wolf` cool grey fox, `Lion` golden cat, `Bear` huge dark brown, …).

**Humans** use the CC0 **"Man Sprite 16x16"** by dutzy (walk / run / work / idle+blink
cycles, LOD: crisp sprite at far zoom with tribe-colored tunic + tribe ring, detailed
vector close-up at zoom ≥ 0.9: outlines, shading, tribe sash, beard, fur cloak).

Resources (berries / plants / stone / wood / water / ore) and all buildings (hut /
storage / wall / pen / fire pit with live flame / totem pole / market) are drawn from
a built-in detailed sprite factory (72×72, cached, zero I/O).
Licenses: all shipped assets are Creative Commons Zero (notices in `Assets/Animals/license.txt` and `Assets/Humans/LICENSE.txt`).
Hot-swap packs: replace PNGs in `Assets/Animals/<folder>/` keeping the
`<idle|run>_<back|front|left|right>_frame_N.png` layout, or swap `Assets/Humans/villager.png` (same 16-px grid, 5 columns).

## 🔌 Plugins (ISimPlugin)
Black-Mirror-style living world: drop a behavior DLL into `Plugins/` (the app's
output `Plugins/` folder) and it becomes part of the simulation — see
`Plugins/README.md` and the ready-to-build `Plugins/ExamplePlugin` sample:
**ExamplePlugin** — a working balance plugin that periodically checks the ecosystem
and warns about extinctions and tribal famine in the world chronicle.
(For a launch run: `dotnet build Plugins/ExamplePlugin` then copy
`Plugins/ExamplePlugin/bin/Release/net8.0-windows/ExamplePlugin.dll`
into the app's `bin/Release/net8.0-windows/Plugins/` folder.)

## 🚀 How to Run
1. Clone the repository.
2. Open the `.sln` file in Visual Studio (2022 recommended).
3. Ensure `.NET Core` or `.NET 5/6/7/8` (depending on your setup) is installed.
4. Build and Run the project (`F5`).

## 🔧 Debug / QA Hooks
Two optional environment variables make visual QA easy (both no-ops when unset):
* `NCD_ZOOM=2.4` — start with a forced camera zoom (perfect for close-up sprite checks).
* `NCD_SHOT=C:\path\shot.png` — at tick 260 the app renders one full frame offscreen
  into the given PNG *in true client pixels* (immune to Windows DPI scaling that
  distorts external screen captures) and writes a `shot.png.txt` with the client size.

Build target: `dotnet build NeuroDots/NeuroCivilization.csproj -c Release`
(current state: 0 errors, 0 warnings).
