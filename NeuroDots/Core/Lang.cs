using System;
using System.Collections.Generic;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Tiny in-app UI localizer.
    /// English is the default language; Russian is switched at runtime (hotkey T) or by plugins.
    /// All world events (chronicle + floating popups) go through this localizer.
    /// </summary>
    public static class Lang
    {
        /// <summary>Current UI language code: "en" (default) or "ru".</summary>
        public static string Code = "en";

        /// <summary>Localized strings: key -> [en, ru]. Index 0 = English (default).</summary>
        static readonly Dictionary<string, string[]> D = new(StringComparer.Ordinal)
        {
            ["birth"]      = new[] { "[B] {0} gave birth: {1}",      "[Б] {0} родила: {1}" },
            ["tech_tribe"] = new[] { "[T] Tribe {0}: {1}",           "[И] Племя {0}: {1}" },
            ["tech_solo"]  = new[] { "[T] {0}: {1}",                 "[И] {0}: {1}" },
            ["death"]      = new[] { "[D] {0} ({1}y): {2}",          "[✝] {0} ({1} г): {2}" },
            ["hunt"]       = new[] { "[H] {0} hunted {1}",           "[H] {0} добыл {1}" },
            ["tamed"]      = new[] { "[P] {0} tamed {1}",            "[P] {0} приручил {1}" },
            ["built"]      = new[] { "[C] {0} built a {1}",          "[C] {0} построил {1}" },
            ["sig_gave"]   = new[] { "[S] {0} -> {1} ({2})",         "[S] {0} дал {1} ({2})" },
            ["saw_tribe"]  = new[] { "[W] New tribe settled: {0}",   "[W] Основано племя: {0}" },
            ["plugin_loaded"] = new[] { "[P] Plugin loaded: {0}",    "[P] Загружен плагин: {0}" },
            ["legend"]     = new[] {
                "Red outline = predator | Green = herbivore | Blue pulse = tamed red pulse = HUNT",
                "Красная обводка = хищник | зелёная = травоядное | голубой пульс = приручено, красный = ОХОТА"
            },
            ["legend2"]    = new[] {
                "Above each agent: green bar=health, amber=hunger, blue=energy; crown=leader, pink=child being born",
                "Над агентами: зелёный бар=здоровье, жёлтый=голод, синий=энергия; корона=лидер, розовый=беременна"
            },
            ["legend3"]    = new[] {
                "Berries mag. dot | plants green | stone grey | wood brown; hut/wall/fire pit buildings",
                "Розовая точка = ягода | зелёная = растение | серый = камень | коричневый = дерево; хижины и костры"
            },
            ["plugin_tick"] = new[] { "[P] Largest tribe now: {0} ({1} members)", "[P] Крупнейшее племя: {0} ({1} чел.)" },
            ["plugin_pred"] = new[] { "[P] PACK RELEASED: predators restored to the wild", "[P] ВЫПУЩЕНЫ ХИЩНИКИ — баланс хищников восстановлен" },
            ["plugin_prey"] = new[] { "[P] HERBIVORE BOOM: prey packs restored", "[P] ВЫПУЩЕНЫ ТРАВОЯДНЫЕ — баланс добычи восстановлен" },
            ["plugin_famine"] = new[] { "[P] FAMINE WARNING: the tribe is starving", "[P] ГОЛОД: племени не хватает еды" },
            ["d_hostile"]  = new[] { "[W] Tensions rise: {0} vs {1}",     "[В] Напряжение растёт: {0} против {1}" },
            ["d_war"]      = new[] { "[W] WAR declared: {0} vs {1}",      "[В] ВОЙНА объявлена: {0} против {1}" },
            ["d_trade"]    = new[] { "[W] Trade opened: {0} <-> {1}",     "[В] Открыта торговля: {0} <-> {1}" },
            ["d_alliance"] = new[] { "[W] ALLIANCE forged: {0} + {1}",    "[В] Заключён СОЮЗ: {0} + {1}" },
            ["d_neutral"]  = new[] { "[W] Peace restored: {0} - {1}",     "[В] Мир восстановлен: {0} - {1}" },
            ["trade_deal"] = new[] { "[S] Market deal: {0} <-> {1}",      "[С] Рыночная сделка: {0} <-> {1}" },
            ["ritual"]     = new[] { "[R] Ritual dance at the totem of {0} ({1} dancers)", "[Р] Ритуальный танец у тотема {0} ({1} участников)" },
            ["heir"]       = new[] { "[L] New leader of {0}: {1} (heir of {2})", "[Л] Новый вождь {0}: {1} (наследник {2})" },
            ["totem_built"]   = new[] { "[C] {0} raised a TOTEM POLE",    "[С] {0} воздвиг ТОТЕМ" },
            ["market_built"]  = new[] { "[C] {0} opened a MARKET",        "[С] {0} открыл РЫНОК" },
            ["raid"]       = new[] { "[W] RAID: {0} plundered the supplies of {1}", "[В] НАБЕГ: {0} разграбил запасы {1}" },
        };

        /// <summary>Gets the localized formatted string for the current language.</summary>
        public static string T(string key, params object[] args)
        {
            var a = D.TryGetValue(key, out var arr) ? arr : null;
            string fmt = a is null ? key : (Code == "ru" ? a[1] : a[0]);
            return args is { Length: > 0 } ? string.Format(fmt, args) : fmt;
        }

        /// <summary>Switches en<->ru and returns the new code.</summary>
        public static string Toggle() => Code = Code == "en" ? "ru" : "en";
    }
}
