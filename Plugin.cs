using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BotmonTrend
{
    // a non-invasive tweak for the stock "botmon" overlay: whole meters from the local player,
    // a trend arrow, colored brackets for close bots and an eight-way bearing arrow.
    // nothing else about botmon is changed
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(DebuggingPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.q8b1q.botmontrend";
        public const string Name = "Botmon Trend";
        public const string Version = "1.4.2";

        internal const string DebuggingPluginGuid = "com.SPT.debugging";
        internal const string BotmonitorScriptTypeName = "SPT.Debugging.Scripts.BotmonitorScript";

        internal static ManualLogSource Log;

        private Harmony _harmony;
        private bool _patched;
        private float _nextRetry;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(Guid);

            Settings.Bind(Config);
            TryPatch();
        }

        private void Update()
        {
            if (_patched || Time.unscaledTime < _nextRetry)
            {
                return;
            }

            // the debugging plugin may load after this one, so keep trying until its type exists
            _nextRetry = Time.unscaledTime + 2f;
            TryPatch();
        }

        private void TryPatch()
        {
            var scriptType = FindType(BotmonitorScriptTypeName);
            if (scriptType == null)
            {
                Log.LogDebug("BotmonitorScript is not loaded yet, will retry.");
                return;
            }

            var onGui = AccessTools.Method(scriptType, "OnGUI");            if (onGui == null)
            {
                Log.LogWarning("OnGUI of BotmonitorScript was not found, nothing to patch.");
                return;
            }

            try
            {
                _harmony.Patch(
                    onGui,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(Snapshot), nameof(Snapshot.Capture))),
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(Transpiler), nameof(Transpiler.Replace))));

                _patched = true;
                Log.LogInfo("Ready. Run 'botmon 1' in the game console: whole meters from your player,"
                            + (Settings.SortByDistance.Value ? ", sorted nearest first," : "")
                            + " " + Settings.ArrowUp.Value + " (" + Settings.UpColor.Value + ") when the gap grows"
                            + ", " + Settings.ArrowDown.Value + " (" + Settings.DownColor.Value + ") when it closes,"
                            + " a " + Settings.BearingSource.Value + " bearing arrow and "
                            + Settings.CloseColor.Value + " brackets under "
                            + Settings.CloseDistanceMeters.Value.ToString("0.#", CultureInfo.InvariantCulture) + " m.");
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to patch BotmonitorScript.OnGUI: " + ex);
            }
        }

        private void OnDestroy()
        {
            Snapshot.Reset();
            Motion.Reset();
            Bearing.Reset();
            Tracker.Reset();

            try
            {
                _harmony?.UnpatchSelf();
            }
            catch
            {
                // unpatching must never crash the game
            }
        }

        // the debugging plugin is not referenced at compile time, resolve the type by name
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                var type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }

    // options visible in the plugin settings window (F12)
    internal static class Settings
    {
        public static ConfigEntry<string> ArrowUp;
        public static ConfigEntry<string> ArrowDown;
        public static ConfigEntry<string> ArrowSteady;
        public static ConfigEntry<bool> ColorArrows;
        public static ConfigEntry<string> UpColor;
        public static ConfigEntry<string> DownColor;
        public static ConfigEntry<float> MinSpeed; // fixed to 0.3, hidden from menu
        public static ConfigEntry<float> DeadBandMeters; // fixed to 0.4, hidden from menu
        public static ConfigEntry<int> ConfirmFrames; // fixed to 3, hidden from menu
        public static ConfigEntry<float> Smoothing; // fixed to 0.35, hidden from menu
        public static ConfigEntry<float> WindowSeconds; // fixed to 0.5, hidden from menu
        public static ConfigEntry<bool> UseLiveDistances;
        public static ConfigEntry<bool> ShowUnit;
        public static ConfigEntry<bool> SortByDistance; // fixed to true, hidden from menu
        public static ConfigEntry<float> SortHysteresis;

        // which parts of the stock tail " [role] [side] [difficulty] nickname" survive
        public static ConfigEntry<bool> ShowRole;
        public static ConfigEntry<bool> ShowSide;
        public static ConfigEntry<bool> ShowNickname;
        public static ConfigEntry<bool> ShowDifficulty;

        // the "> " prefix of a bot line becomes a rank marker: common / boss / escort
        public static ConfigEntry<string> MarkerCommon;
        public static ConfigEntry<string> MarkerBoss;
        public static ConfigEntry<string> MarkerFollower;
        public static ConfigEntry<bool> MarkerColored; // fixed to true, hidden from menu

        // glyph style presets; the individual fields stay in the file, hidden from the menu
        public static ConfigEntry<string> BearingStyle;
        public static ConfigEntry<string> TrendStyle;

        // glyph style presets, listed in the mod's compass order N NE E SE S SW W NW
        public const string BearingStyleArrows = "\u2191 \u2197 \u2192 \u2198 \u2193 \u2199 \u2190 \u2196";
        public const string BearingStyleDouble = "\u21D1 \u21D7 \u21D2 \u21D8 \u21D3 \u21D9 \u21D0 \u21D6";
        public const string BearingStyleWhite = "\u25B3 \u25F9 \u25B7 \u25FF \u25BD \u25FA \u25C1 \u25F8";
        public const string BearingStyleBlack = "\u25B2 \u25E5 \u25B6 \u25E2 \u25BC \u25E3 \u25C0 \u25E4";
        // trend pairs in the mod's order: grows, shrinks
        public const string TrendStyleThin = "\u2191 \u2193";
        public const string TrendStyleDouble = "\u21D1 \u21D3";
        public const string TrendStyleSmall = "\u25B3 \u25BD";
        public const string TrendStyleFill = "\u25B2 \u25BC";
        public const string TrendStyleTiny = "\u25B5 \u25BF";

        public static ConfigEntry<bool> Diagnostics;

        // bearing arrow
        public static ConfigEntry<bool> ShowBearing;
        public static ConfigEntry<string> BearingN;
        public static ConfigEntry<string> BearingNE;
        public static ConfigEntry<string> BearingE;
        public static ConfigEntry<string> BearingSE;
        public static ConfigEntry<string> BearingS;
        public static ConfigEntry<string> BearingSW;
        public static ConfigEntry<string> BearingW;
        public static ConfigEntry<string> BearingNW;
        public static ConfigEntry<string> BearingColor;
        public static ConfigEntry<string> BearingSource;
        public static ConfigEntry<bool> VariationSelectors;
        public static ConfigEntry<bool> BearingAfterDistance;
        public static ConfigEntry<float> RefreshMeters;

        // bearing arrow tinted by the vertical gap between the bot and the player
        public static ConfigEntry<bool> Elevation;
        public static ConfigEntry<float> ElevationThreshold;
        public static ConfigEntry<string> ElevationUpColor;
        public static ConfigEntry<string> ElevationDownColor;
        // close distance highlight
        public static ConfigEntry<bool> HighlightClose;
        public static ConfigEntry<float> CloseDistanceMeters;
        public static ConfigEntry<string> CloseColor;

        // sections are listed in the menu in the order they are first bound; inside a section the
        // manager sorts by the Order tag, descending. every bind call below carries an explicit
        // position, so the layout does not depend on how a key happens to be spelled
        internal static void Bind(ConfigFile config)
        {
            // fixed to true: sorting by distance is always on (menu entry removed)
            SortByDistance = Hidden(config, "Distance", "SortByDistance", true, "Fixed to true: bots sorted by distance from the player.");

            // fixed to 1m hysteresis (menu entry removed)
            SortHysteresis = Hidden(config, "Distance", "SortHysteresisMeters", 1f, "Fixed to 1m sort hysteresis.");

            // fixed to true: distance measured from player (menu entry removed)
            UseLiveDistances = Hidden(config, "Distance", "FromPlayerPosition", true, "Fixed to true: distance from local player.");

            ShowUnit = Ordered(config, "Distance", "ShowUnit", true,
                "Keep the \"m\" suffix after the rounded distance.", 10);

            // the stock tail of a bot line is " [role] [side] [difficulty] nickname"; the mod rebuilds
            // it from the parsed pieces, so the user can drop the parts they never read
            ShowRole = Ordered(config, "Tags", "Role", false,
                "Keep the bot role tag, e.g. [assault].", 40);

            ShowSide = Ordered(config, "Tags", "Side", true,
                "Keep the bot side tag, e.g. [Savage].", 30);

            ShowNickname = Ordered(config, "Tags", "Nickname", false,
                "Keep the bot nickname.", 20);

            ShowDifficulty = Ordered(config, "Tags", "Difficulty", false,
                "Keep the bot difficulty tag, e.g. [normal].", 10);

            // the line marker replaces the stock "> " and tells the rank at a glance: a boss, one of
            // his followers, or nobody special. Roles come from WildSpawnType.ToString(), which is
            // "boss*" for every boss, "follower*" for his escorts and "sectant*" for cultists
            MarkerCommon = Ordered(config, "Tags", "MarkerCommon", "\u2218",
                "Line marker for a regular bot, replaces the \"> \" prefix.", 70);

            MarkerBoss = Ordered(config, "Tags", "MarkerBoss", "\u25C8",
                "Line marker for a boss (role starts with \"boss\").", 60);

            MarkerFollower = Ordered(config, "Tags", "MarkerFollower", "\u25C7",
                "Line marker for boss escorts (role starts with \"follower\" or \"sectant\").", 50);

            // fixed to true: markers colored (menu entry removed)
            MarkerColored = Hidden(config, "Tags", "Colored", true, "Fixed to true: rank markers colored.");

            // fixed to 0.5s (menu entry removed)
            WindowSeconds = Hidden(config, "Trend", "WindowSeconds", 0.5f, "Fixed to 0.5s trend window.");

            // fixed to 0.4m (menu entry removed)
            DeadBandMeters = Hidden(config, "Trend", "DeadBandMeters", 0.4f, "Fixed to 0.4m deadband.");

            // fixed to 0.3 m/s (menu entry removed)
            MinSpeed = Hidden(config, "Trend", "MinSpeedMetersPerSecond", 0.3f, "Fixed to 0.3 m/s min trend speed.");

            // fixed to 0.35 (menu entry removed)
            Smoothing = Hidden(config, "Trend", "Smoothing", 0.35f, "Fixed to 0.35 smoothing.");

            // fixed to 3 frames (menu entry removed)
            ConfirmFrames = Hidden(config, "Trend", "ConfirmFrames", 3, "Fixed to 3 frames confirm.");

            ColorArrows = Ordered(config, "Arrows", "Colored", true,
                "Paint the arrows. If the <color> tags show up literally, set this to false.", 60);

            // the two per-arrow fields stay in the config file but are hidden from the menu: the style
            // dropdown below writes into them, exactly like the bearing set does. The dropdown value IS
            // the glyph pair itself, so the list shows the actual sets; "Custom" is the only non-glyph
            // entry and is auto-selected when the stored Up/Down were edited by hand.
            TrendStyle = Choice(config, "Arrows", "Style", TrendStyleThin,
                "Glyph pair for the distance trend: first grows, second shrinks. \"Custom\" appears when "
                    + "the Up/Down entries in the config file were edited by hand.",
                55,
                TrendStyleThin, TrendStyleDouble, TrendStyleSmall, TrendStyleFill, TrendStyleTiny, "Custom");

            ArrowUp = Hidden(config, "Arrows", "Up", "\u2191",
                "Trend glyph for a growing distance.");

            ArrowDown = Hidden(config, "Arrows", "Down", "\u2193",
                "Trend glyph for a shrinking distance.");

            ArrowSteady = Ordered(config, "Arrows", "Steady", "\u25EB",
                "Marker for a bot whose distance is unchanged, e.g. \"=\" or \"-\".", 30);

            UpColor = ColorChoice(config, "Arrows", "UpColor", "#33FF66",
                "Color of the up arrow.", 20);
            DownColor = ColorChoice(config, "Arrows", "DownColor", "#FF3333",
                "Color of the down arrow.", 10);

            ShowBearing = Ordered(config, "Bearing", "ShowBearing", true,
                "Draw an 8-direction emoji arrow showing where the bot is relative to the player's look "
                    + "direction (horizontal plane, Y is up). The last known direction is kept until the "
                    + "bot actually moves again.", 160);

            BearingSource = Choice(config, "Bearing", "Source", "Movement",
                "\"Movement\" - the arrow points the way the bot walks. The heading is re-measured only when "
                    + "the distance value changes, so a bot that stands still keeps showing the last known "
                    + "direction. \"Position\" - the arrow points at the bot around you instead, which is "
                    + "refreshed every frame.", 150, "Movement", "Position");

            // the eight per-direction fields stay in the config file so old values are not lost, but
            // they are hidden from the menu: the style dropdown below writes into them. The dropdown
            // value IS the glyph set itself, so the list shows the actual sets.
            BearingStyle = Choice(config, "Bearing", "Style", BearingStyleArrows,
                "Glyph set for the eight directions, clockwise from the top: N, NE, E, SE, S, SW, W, NW.",
                140,
                BearingStyleArrows, BearingStyleDouble, BearingStyleWhite, BearingStyleBlack);

            // listed clockwise from the top, the way the compass reads on screen
            BearingN = Hidden(config, "Bearing", "N", "\u2B06\uFE0F", "Bearing glyph: straight ahead of your view (up).");
            BearingNE = Hidden(config, "Bearing", "NE", "\u2197\uFE0F", "Bearing glyph: ahead-right.");
            BearingE = Hidden(config, "Bearing", "E", "\u27A1\uFE0F", "Bearing glyph: to your right.");
            BearingSE = Hidden(config, "Bearing", "SE", "\u2198\uFE0F", "Bearing glyph: behind-right.");
            BearingS = Hidden(config, "Bearing", "S", "\u2B07\uFE0F", "Bearing glyph: behind you (down).");
            BearingSW = Hidden(config, "Bearing", "SW", "\u2199\uFE0F", "Bearing glyph: behind-left.");
            BearingW = Hidden(config, "Bearing", "W", "\u2B05\uFE0F", "Bearing glyph: to your left.");
            BearingNW = Hidden(config, "Bearing", "NW", "\u2196\uFE0F", "Bearing glyph: ahead-left.");

            VariationSelectors = Ordered(config, "Bearing", "VariationSelectors", true,
                "Keep the emoji variation selector (U+FE0F) behind each arrow. Turn it off if the game font"
                    + " draws the arrows as empty boxes - the same glyphs then render as plain monochrome arrows.", 60);

            BearingColor = ColorChoice(config, "Bearing", "Color", "#33FFFF",
                "Color of the bearing arrow.", 50);

            Elevation = Ordered(config, "Bearing", "Elevation", true,
                "Tint the bearing arrow by the height difference: bot above you - the up color, "
                    + "below you - the down color, otherwise the plain bearing color.", 45);

            ElevationUpColor = ColorChoice(config, "Bearing", "ElevationUpColor", "#AAFF33",
                "Color of the bearing arrow when the bot is higher than you (Y difference).", 44);

            ElevationDownColor = ColorChoice(config, "Bearing", "ElevationDownColor", "#FF6B1C",
                "Color of the bearing arrow when the bot is lower than you (Y difference).", 43);

            ElevationThreshold = Ordered(config, "Bearing", "ElevationThresholdMeters", 2f,
                "Y difference in meters that counts as a different level: above this the bot is higher, "
                    + "below the negative value - lower, in between - the same level. A floor of a panel "
                    + "building is 2.5-3m, so 2.0 keeps cars, debris and stair landings in the same level.", 42);

            BearingAfterDistance = Ordered(config, "Bearing", "AfterDistance", false,
                "false - the bearing arrow is drawn before the brackets (arrow + trend + distance); "
                    + "true - it is drawn after the distance, inside the brackets.", 40);

            // fixed to 1.0m (menu entry removed)
            RefreshMeters = Hidden(config, "Bearing", "RefreshMeters", 1f, "Fixed to 1.0m refresh threshold.");

            HighlightClose = Ordered(config, "Proximity", "HighlightClose", true,
                "Color the [distance] brackets when a bot is closer than the threshold.", 30);

            CloseDistanceMeters = Ordered(config, "Proximity", "CloseDistanceMeters", 10f,
                "Below this distance the brackets get the close color.", 20);

            CloseColor = ColorChoice(config, "Proximity", "CloseColor", "#FFD93B",
                "Color of the brackets for a close bot.", 10);

            Diagnostics = Ordered(config, "Debug", "LogMeasurements", false,
                "Write the measured distances, trend and bearing decisions to the BepInEx log once per "
                    + "second. Useful when the arrows do not behave as expected.", 10);

            // not a setting: the project page drawn as a browser-style link; clicking it opens the
            // page, and the URL stays in the cfg file, so anyone who opens it can find the repository
            Link(config, "About", "Repository", "https://github.com/spair2k/BotmonTrend",
                "Project page on GitHub: releases, source, issues. Click to open.", 10);

            SyncStyleDropdowns();

            BearingStyle.SettingChanged += (_, __) => ApplyBearingStyle();
            TrendStyle.SettingChanged += (_, __) => ApplyTrendStyle();

            Plugin.Log?.LogInfo("Settings: up=" + Quote(ArrowUp.Value) + " down=" + Quote(ArrowDown.Value)
                + " colored=" + ColorArrows.Value + " minSpeed="
                + MinSpeed.Value.ToString("0.##", CultureInfo.InvariantCulture) + " m/s");
        }

        // writes the glyphs of the selected bearing preset into the hidden N..NW fields
        private static void ApplyBearingStyle()
        {
            if (BearingStyle.Value == "Custom")
            {
                return;
            }

            var parts = BearingStyle.Value.Split(' ');
            if (parts.Length != 8)
            {
                return;
            }

            BearingN.Value = parts[0];
            BearingNE.Value = parts[1];
            BearingE.Value = parts[2];
            BearingSE.Value = parts[3];
            BearingS.Value = parts[4];
            BearingSW.Value = parts[5];
            BearingW.Value = parts[6];
            BearingNW.Value = parts[7];
        }

        // writes the glyphs of the selected trend preset into the hidden Up/Down fields
        private static void ApplyTrendStyle()
        {
            if (TrendStyle.Value == "Custom")
            {
                return;
            }

            var parts = TrendStyle.Value.Split(' ');
            if (parts.Length != 2)
            {
                return;
            }

            ArrowUp.Value = parts[0];
            ArrowDown.Value = parts[1];
        }

        // after loading, re-derive each dropdown from the stored glyphs: a set matching a preset
        // shows that preset, anything hand-edited shows "Custom" and is never overwritten
        private static void SyncStyleDropdowns()
        {
            var bearing = string.Join(" ", BearingN.Value, BearingNE.Value, BearingE.Value, BearingSE.Value,
                BearingS.Value, BearingSW.Value, BearingW.Value, BearingNW.Value);
            BearingStyle.Value = BearingStyleName(bearing);

            var trend = string.Join(" ", ArrowUp.Value, ArrowDown.Value);
            TrendStyle.Value = TrendStyleName(trend);
        }

        // reverse lookup: the dropdown value for a stored glyph set; anything hand-edited
        // falls back to the default set, the dropdown has no "Custom" entry
        public static string BearingStyleName(string glyphs)
        {
            switch (glyphs)
            {
                case BearingStyleDouble: return BearingStyleDouble;
                case BearingStyleWhite: return BearingStyleWhite;
                case BearingStyleBlack: return BearingStyleBlack;
                default: return BearingStyleArrows;
            }
        }

        public static string TrendStyleName(string glyphs)
        {
            switch (glyphs)
            {
                case TrendStyleThin: return TrendStyleThin;
                case TrendStyleDouble: return TrendStyleDouble;
                case TrendStyleSmall: return TrendStyleSmall;
                case TrendStyleFill: return TrendStyleFill;
                case TrendStyleTiny: return TrendStyleTiny;
                default: return "Custom";
            }
        }

        // bright readable colors, used as the initial defaults of the color selectors
        private static readonly string[] ColorChoices = new[]
        {
            "#FFFFFF", // white
            "#FFD93B", // yellow
            "#FFB800", // gold
            "#FF6B1C", // orange
            "#FF5F40", // coral
            "#FF3333", // red
            "#FF3FBF", // hotpink
            "#FF33FF", // magenta
            "#B133FF", // violet
            "#8040FF", // purple
            "#3366FF", // blue
            "#33B5FF", // skyblue
            "#33FFFF", // cyan
            "#33FFCC", // aqua
            "#AAFF33", // lime
            "#33FF66", // green
        };
        // ConfigurationManager orders a section by the Order tag on its entries (descending);
        // without it everything comes out alphabetical and the compass reads E, N, NE, NW, S, SE, SW, W
        public sealed class ConfigurationManagerAttributes
        {
            public int Order;

            // hides the entry from the manager menu while the setting keeps working in the config file
            public bool Browsable = true;
            public Action<ConfigEntryBase> CustomDrawer;
        }

        // config.Bind plus the position the setting takes inside its section
        private static ConfigEntry<T> Ordered<T>(ConfigFile config, string section, string key, T defaultValue,
            string description, int order)
        {
            return config.Bind(section, key, defaultValue,
                new ConfigDescription(description, null, new ConfigurationManagerAttributes { Order = order }));
        }

        // same as Ordered, but the value is picked from a list, which the manager draws as a dropdown
        private static ConfigEntry<string> Choice(ConfigFile config, string section, string key, string defaultValue,
            string description, int order, params string[] values)
        {
            return config.Bind(section, key, defaultValue,
                new ConfigDescription(description, new AcceptableValueList<string>(values),
                    new ConfigurationManagerAttributes { Order = order }));
        }

        // Five contrasting colors from the palette, taken in proportional hue phase
        private static readonly string[] ColorQuickChoices = new[]
        {
            "#FF3333", // red
            "#FFB800", // amber
            "#33FF66", // green
            "#33B5FF", // sky blue
            "#FFFFFF", // white
        };

        // compact color selector: preview swatch, hex field and five one-click preset checkboxes
        private static ConfigEntry<string> ColorChoice(ConfigFile config, string section, string key, string defaultValue,
            string description, int order)
        {
            return config.Bind(section, key, defaultValue,
                new ConfigDescription(description, null,
                    new ConfigurationManagerAttributes { Order = order, CustomDrawer = DrawColorSimple }));
        }

        private static void DrawColorSimple(ConfigEntryBase entry)
        {
            var configEntry = entry as ConfigEntry<string>;
            if (configEntry == null)
            {
                return;
            }

            var oldColor = GUI.color;
            Color previewColor = Color.white;
            ColorUtility.TryParseHtmlString(configEntry.Value, out previewColor);

            // Preview + HEX
            GUILayout.BeginHorizontal();
            GUI.color = previewColor;
            GUILayout.Label("■", GUILayout.Width(14));
            GUI.color = oldColor;
            var newHex = GUILayout.TextField(configEntry.Value ?? "#FFFFFF", GUILayout.Width(72));
            if (!string.Equals(newHex, configEntry.Value, StringComparison.OrdinalIgnoreCase) &&
                ColorUtility.TryParseHtmlString(newHex, out var parsed))
            {
                configEntry.Value = newHex.ToUpperInvariant();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Five unlabeled checkboxes in contrasting colors
            GUILayout.BeginHorizontal();
            for (var i = 0; i < ColorQuickChoices.Length; i++)
            {
                var hexChoice = ColorQuickChoices[i];
                Color boxColor;
                if (!ColorUtility.TryParseHtmlString(hexChoice, out boxColor))
                {
                    boxColor = Color.white;
                }

                var isSelected = string.Equals(configEntry.Value, hexChoice, StringComparison.OrdinalIgnoreCase);
                GUI.color = boxColor;
                var toggled = GUILayout.Toggle(isSelected, GUIContent.none, GUILayout.Width(16), GUILayout.Height(16));
                GUI.color = oldColor;

                if (toggled && !isSelected)
                {
                    configEntry.Value = hexChoice;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // a browser-style link instead of an editable field: the URL is hardcoded into the drawer,
        // drawn blue like a web link; clicking it opens the page. The value still lands in the cfg
        // file, so the repository stays discoverable there too
        private static ConfigEntry<string> Link(ConfigFile config, string section, string key, string url,
            string description, int order)
        {
            return config.Bind(section, key, url,
                new ConfigDescription(description, null,
                    new ConfigurationManagerAttributes { Order = order, CustomDrawer = _ => DrawHyperlink(url) }));
        }

        // a button styled as a plain label, colored like a link in a browser
        private static void DrawHyperlink(string url)
        {
            var oldColor = GUI.color;
            GUI.color = new Color32(0x33, 0x99, 0xFF, 0xFF);
            var linkStyle = new GUIStyle(GUI.skin.label) { stretchWidth = false, wordWrap = false };
            if (GUILayout.Button(url, linkStyle))
            {
                Application.OpenURL(url);
            }

            GUI.color = oldColor;
        }

        // a setting that keeps working but does not show up in the manager menu: the individual
        // glyph fields a style dropdown writes into, and values that are intentionally fixed
        private static ConfigEntry<T> Hidden<T>(ConfigFile config, string section, string key, T defaultValue,
            string description)
        {
            return config.Bind(section, key, defaultValue,
                new ConfigDescription(description, null, new ConfigurationManagerAttributes { Browsable = false }));
        }

        private static string Quote(string value)
        {
            return string.IsNullOrEmpty(value) ? "<none>" : "\"" + value + "\"";
        }
    }

    // reads the live positions of the local player and of every bot the stock overlay is about to
    // print, in the same order, so each printed line can be tied back to a real bot
    internal static class Snapshot
    {
        public static bool HasPlayer;
        public static Vector3 PlayerPosition;
        public static bool HasLook;
        public static Vector3 LookForward;

        public static bool IsLive => HasPlayer && _botsByZone != null;
        public static int ZoneCount => _botsByZone?.Count ?? 0;

        private static Dictionary<string, List<Bot>> _botsByZone;
        private static int _pass;
        private static int _builtFrame = -1;

        private static bool _dataUnavailable;
        private static bool _dataWarningLogged;

        internal sealed class Bot
        {
            public string Id;
            public Vector3 Position;
            public int Pass = -1;
        }

        // harmony prefix on BotmonitorScript.OnGUI. positions are refreshed once per frame, while
        // each OnGUI pass (Layout, Repaint, ...) gets its own cursor over the lists
        internal static void Capture(object __instance)
        {
            _pass++;

            if (!Settings.UseLiveDistances.Value || __instance == null)
            {
                Clear();
                return;
            }

            if (_builtFrame == Time.frameCount && _botsByZone != null)
            {
                return;
            }

            try
            {
                var mainPlayer = ReflectionUtil.Read(__instance, "_player");

                HasPlayer = (mainPlayer != null && ReflectionUtil.TryGetPosition(mainPlayer, out PlayerPosition))
                            || ReflectionUtil.TryGetCameraPosition(__instance, "_camera", out PlayerPosition);

                // looking straight up or down flattens the view vector to nothing, in which case the
                // last usable yaw is kept so the bearing does not jump onto the world axes
                if (ReflectionUtil.TryGetViewDirection(__instance, mainPlayer, out var view))
                {
                    LookForward = view;
                    HasLook = true;
                }

                if (!HasPlayer)
                {
                    Clear();
                    NoteDataUnavailable("local player position");
                    return;
                }

                if (!TryReadBots(__instance, out var zones))
                {
                    Clear();
                    NoteDataUnavailable("bot positions");
                    return;
                }

                _botsByZone = zones;
                _builtFrame = Time.frameCount;
                _dataUnavailable = false;
            }
            catch (Exception ex)
            {
                WarnOnce("Reading positions failed: " + ex.Message);
                Clear();
            }
        }

        public static void Reset()
        {
            _pass = 0;
            _builtFrame = -1;
            Clear();
        }

        // bots of a zone, taken one by one in list order. the overlay keeps per-zone lists, so the
        // same zone can be printed in several blocks; every block walks the list from the start
        public static Bot TakeNext(string zone)
        {
            if (_botsByZone == null || zone == null
                || !_botsByZone.TryGetValue(zone, out var bots) || bots.Count == 0)
            {
                return null;
            }

            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i].Pass != _pass)
                {
                    bots[i].Pass = _pass;
                    return bots[i];
                }
            }

            return null;
        }

        private static void Clear()
        {
            HasPlayer = false;
            HasLook = false;
            _botsByZone = null;
        }

        private static bool TryReadBots(object script, out Dictionary<string, List<Bot>> zones)
        {
            zones = null;

            var dict = ReflectionUtil.Read(script, "_zoneAndPlayers") as IDictionary;
            if (dict == null)
            {
                return false;
            }

            var result = new Dictionary<string, List<Bot>>(StringComparer.Ordinal);

            foreach (DictionaryEntry entry in dict)
            {
                var zone = entry.Key as string;
                if (zone == null)
                {
                    continue;
                }

                var list = new List<Bot>();
                var index = 0;

                if (entry.Value is IEnumerable players)
                {
                    foreach (var bot in players)
                    {
                        index++;

                        if (bot == null || !ReflectionUtil.IsAlive(bot))
                        {
                            // the overlay only prints alive entries, so the counters have to match
                            continue;
                        }

                        if (!ReflectionUtil.TryGetPosition(bot, out var position))
                        {
                            return false;
                        }

                        var id = ReflectionUtil.Read(bot, "ProfileId") as string;
                        list.Add(new Bot
                        {
                            Id = string.IsNullOrEmpty(id) ? zone + "#" + index.ToString(CultureInfo.InvariantCulture) : id,
                            Position = position,
                        });
                    }
                }

                result[zone] = list;
            }

            zones = result;
            return true;
        }

        private static void NoteDataUnavailable(string what)
        {
            if (_dataUnavailable)
            {
                return;
            }

            _dataUnavailable = true;

            Plugin.Log?.LogWarning(
                "Could not read " + what + " from BotmonitorScript. Falling back to the"
                + " distance value the overlay prints itself.");
        }

        private static void WarnOnce(string message)
        {
            if (_dataWarningLogged)
            {
                return;
            }

            _dataWarningLogged = true;
            Plugin.Log?.LogWarning(message);
        }
    }

    // reflection helper for the game and overlay objects, which are not referenced at compile time
    internal static class ReflectionUtil
    {
        private static readonly Dictionary<string, MemberInfo> Cache =
            new Dictionary<string, MemberInfo>(StringComparer.Ordinal);

        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);

        public static object Read(object target, params string[] path)
        {
            var current = target;

            foreach (var name in path)
            {
                if (current == null)
                {
                    return null;
                }

                var member = Find(current.GetType(), name);
                if (member == null)
                {
                    return null;
                }

                try
                {
                    current = member is FieldInfo field
                        ? field.GetValue(current)
                        : ((PropertyInfo)member).GetValue(current, null);
                }
                catch (TargetInvocationException)
                {
                    return null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return current;
        }

        public static bool IsAlive(object player)
        {
            return Read(player, "HealthController", "IsAlive") is bool alive && alive;
        }

        // the player object exposes "Transform" as a game-side wrapper type (not a
        // UnityEngine.Transform), which carries a "position" property, so both shapes are supported
        public static bool TryGetPosition(object target, out Vector3 position)
        {
            position = default;

            if (UnwrapPosition(Read(target, "Transform"), out position))
            {
                return true;
            }

            return UnwrapPosition(Read(target, "transform"), out position);
        }

        public static bool TryGetCameraPosition(object owner, string fieldName, out Vector3 position)
        {
            position = default;

            var camera = Read(owner, fieldName);
            if (camera == null)
            {
                return false;
            }

            return UnwrapPosition(Read(camera, "transform"), out position)
                   || UnwrapPosition(camera, out position);
        }

        // horizontal view direction of the local player: camera forward first, then LookDirection,
        // then the player transform. Y is dropped, the bearing is measured around the Y axis only
        public static bool TryGetViewDirection(object script, object player, out Vector3 forward)
        {
            forward = default;

            return (TryGetCameraForward(script, "_camera", ref forward)
                    || TryGetVectorMember(player, "LookDirection", ref forward)
                    || TryGetPlayerTransformForward(player, ref forward))
                   && Flatten(forward, out forward);
        }

        private static bool TryGetCameraForward(object owner, string fieldName, ref Vector3 forward)
        {
            var camera = Read(owner, fieldName);
            return camera != null && TryGetVectorMember(Read(camera, "transform"), "forward", ref forward);
        }

        private static bool TryGetPlayerTransformForward(object player, ref Vector3 forward)
        {
            return TryGetVectorMember(Read(player, "Transform"), "forward", ref forward);
        }

        private static bool TryGetVectorMember(object target, string name, ref Vector3 value)
        {
            if (target == null)
            {
                return false;
            }

            var result = Read(target, name);
            if (result is Vector3 vector && vector.sqrMagnitude > 1e-6f)
            {
                value = vector;
                return true;
            }

            return false;
        }

        private static bool Flatten(Vector3 vector, out Vector3 flat)
        {
            flat = new Vector3(vector.x, 0f, vector.z);
            return flat.sqrMagnitude > 1e-6f;
        }

        private static bool UnwrapPosition(object value, out Vector3 position)
        {
            position = default;

            if (value == null)
            {
                return false;
            }

            if (value is Transform transform && transform != null)
            {
                position = transform.position;
                return true;
            }

            if (Read(value, "position") is Vector3 vector)
            {
                position = vector;
                return true;
            }

            return false;
        }

        private static MemberInfo Find(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var key = type.FullName + "." + name;

            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                if (Missing.Contains(key))
                {
                    return null;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                           | BindingFlags.FlattenHierarchy;

                MemberInfo member = type.GetProperty(name, flags);
                if (member == null)
                {
                    member = type.GetField(name, flags);
                }

                if (member == null)
                {
                    Missing.Add(key);
                    return null;
                }

                Cache[key] = member;
                return member;
            }
        }
    }

    // redirects one call site inside BotmonitorScript.OnGUI so the panel text is reformatted
    // before Unity measures and draws it: GUIStyle.CalcSize(GUIContent) keeps the box sized
    // correctly, GUI.Box is the fallback for a build that does not measure first
    internal static class Transpiler
    {
        private static readonly MethodInfo CalcSizeMethod =
            AccessTools.Method(typeof(GUIStyle), "CalcSize", new[] { typeof(GUIContent) });

        private static readonly MethodInfo BoxMethod =
            AccessTools.Method(typeof(GUI), "Box", new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) });

        private static readonly MethodInfo CalcSizeHook =
            AccessTools.Method(typeof(Transpiler), nameof(OnCalcSize));

        private static readonly MethodInfo BoxHook =
            AccessTools.Method(typeof(Transpiler), nameof(OnBox));

        private static bool _reported;

        public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var replaced = string.Empty;

            for (var i = 0; i < codes.Count; i++)
            {
                var code = codes[i];

                if (code.opcode == OpCodes.Callvirt && Equals(code.operand as MethodInfo, CalcSizeMethod))
                {
                    // argument layout is identical, only the callee changes
                    codes[i] = new CodeInstruction(OpCodes.Call, CalcSizeHook);
                    replaced = "GUIStyle.CalcSize";
                    break;
                }

                if (code.opcode == OpCodes.Call && Equals(code.operand as MethodInfo, BoxMethod))
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, BoxHook);
                    replaced = "GUI.Box";
                    break;
                }
            }

            if (!_reported)
            {
                _reported = true;
                Plugin.Log?.LogInfo(string.IsNullOrEmpty(replaced)
                    ? "No known Botmon draw call found - distance format stays original."
                    : "Botmon OnGUI hooked at " + replaced + ".");
            }

            return codes;
        }

        private static Vector2 OnCalcSize(GUIStyle style, GUIContent content)
        {
            Apply(style, content);
            return style.CalcSize(content);
        }

        private static void OnBox(Rect rect, GUIContent content, GUIStyle style)
        {
            Apply(style, content);
            GUI.Box(rect, content, style);
        }

        private static void Apply(GUIStyle style, GUIContent content)
        {
            if (content == null || string.IsNullOrEmpty(content.text))
            {
                return;
            }

            try
            {
                // Arrows/Colored is the master switch for every rich text decoration: trend colors,
                // the close brackets and the bearing arrow
                var rich = Settings.ColorArrows.Value;

                // IMGUI renders rich text only when the style allows it, and measuring has to use the
                // same flag as the draw call, so the switch happens on the Layout pass.
                if (style != null && style.richText != rich
                    && Event.current != null && Event.current.type == EventType.Layout)
                {
                    style.richText = rich;
                }

                content.text = Tracker.Process(content.text, rich);
            }
            catch (Exception ex)
            {
                // the original overlay must survive any formatting problem
                Plugin.Log?.LogWarning("Distance formatting failed: " + ex.Message);
            }
        }
    }

    // rewrites the bot lines the stock overlay produced:
    // "> [42.13m] [assault] [Savage] [normal] Kaban" becomes
    // "> ↗[↑42m] [Savage] ..." with a bearing arrow, a colored trend arrow and the tags
    // rebuilt from the Tags settings; the brackets recolor while a bot is close
    internal static class Tracker
    {
        private const string BotLinePattern = @"^> \[(?<distance>[0-9\u00A0\u202F.,\- ]+)m?\](?<rest>.*)$";
        private const string ZoneHeaderPattern = @"^(?<zone>.+) = (?<count>\d+)$";

        private static readonly Regex BotLine = new Regex(BotLinePattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ZoneHeader = new Regex(ZoneHeaderPattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly StringBuilder Builder = new StringBuilder(1024);

        // one bot line and everything needed to move it around
        private struct BotEntry
        {
            public double Distance;
            public string Text;
            public string Zone;
            public string Id;
            public int Order;
        }

        // rank of every bot in the last sorted frame, so the hysteresis knows who used to be closer
        private static readonly Dictionary<string, int> LastRank = new Dictionary<string, int>(StringComparer.Ordinal);

        private const int KindOther = 0;
        private const int KindZoneHeader = 1;
        private const int KindBot = 2;

        public static string Process(string text, bool colored)
        {
            if (Settings.Diagnostics.Value)
            {
                LogSnapshot();
            }

            var lines = text.Split('\n');
            var sort = Settings.SortByDistance.Value;

            // positions can only be tied to the printed lines while they are still in the order the
            // overlay wrote them, so the walk below never reorders anything; sorting happens on the
            // formatted results afterwards
            var kind = sort ? new int[lines.Length] : null;
            var zones = sort ? new string[lines.Length] : null;

            string zone = null;
            var changed = false;
            var bots = sort ? new List<BotEntry>() : null;

            for (var i = 0; i < lines.Length; i++)
            {
                var header = ZoneHeader.Match(lines[i]);
                if (header.Success)
                {
                    zone = header.Groups["zone"].Value.Trim();

                    if (sort)
                    {
                        kind[i] = KindZoneHeader;
                        zones[i] = zone;
                    }

                    continue;
                }

                var match = BotLine.Match(lines[i]);
                if (!match.Success)
                {
                    continue;
                }

                var rest = match.Groups["rest"].Value;

                double? distance;
                string id;
                var live = false;
                var botPosition = default(Vector3);

                var bot = Snapshot.TakeNext(zone);
                if (bot != null && Snapshot.HasPlayer)
                {
                    live = true;
                    botPosition = bot.Position;
                    distance = Vector3.Distance(Snapshot.PlayerPosition, bot.Position);
                    id = bot.Id ?? zone + "|" + rest.Trim();
                }
                else
                {
                    distance = ParseDistance(match.Groups["distance"].Value);
                    id = zone + "|" + rest.Trim();
                }

                if (distance == null)
                {
                    continue;
                }

                var direction = Motion.Direction(id, distance.Value);
                var sector = live ? Bearing.Sector(id, botPosition, distance.Value) : -1;
                var verticalGap = live
                    ? botPosition.y - Snapshot.PlayerPosition.y
                    : 0d;

                var formatted = FormatLine(distance.Value, direction, sector, verticalGap, rest, colored);

                changed = true;

                if (sort)
                {
                    kind[i] = KindBot;
                    zones[i] = zone;
                    bots.Add(new BotEntry
                    {
                        Distance = distance.Value,
                        Text = formatted,
                        Zone = zone,
                        Id = id,
                        Order = bots.Count,
                    });
                }
                else
                {
                    lines[i] = formatted;
                }

                if (Settings.Diagnostics.Value)
                {
                    LogMeasure(zone, id, distance.Value, direction, sector, live);
                }
            }

            Motion.Prune();
            Bearing.Prune();

            if (!changed)
            {
                // an empty panel means the previous raid's bots are gone, their ranks must go too
                if (LastRank.Count > 0)
                {
                    LastRank.Clear();
                }

                return text;
            }

            if (!sort)
            {
                // no sorted frame is produced while the feature is off, so forget the old ranks
                if (LastRank.Count > 0)
                {
                    LastRank.Clear();
                }

                return string.Join("\n", lines);
            }

            return RebuildSorted(lines, kind, zones, bots);
        }

        public static void Reset()
        {
            LastRank.Clear();
        }

        private static int RankOf(string id)
        {
            if (id == null)
            {
                return int.MaxValue;
            }

            return LastRank.TryGetValue(id, out var rank) ? rank : int.MaxValue;
        }

        // insertion sort that only lets a bot overtake another one once it is closer by more than
        // the band; seeding from the previous frame's order keeps the panel from reshuffling
        private static List<BotEntry> InsertionSorted(List<BotEntry> bots, double band)
        {
            if (LastRank.Count == 0)
            {
                band = 0d;
            }

            var seed = new List<BotEntry>(bots);
            seed.Sort((a, b) =>
            {
                var rank = RankOf(a.Id).CompareTo(RankOf(b.Id));
                return rank != 0 ? rank : a.Order.CompareTo(b.Order);
            });

            var result = new List<BotEntry>(bots.Count);

            foreach (var entry in seed)
            {
                // slide the new entry left over every bot that is closer by more than the band
                var at = result.Count;
                result.Add(entry);

                while (at > 0 && result[at - 1].Distance > entry.Distance + band)
                {
                    result[at] = result[at - 1];
                    at--;
                }

                result[at] = entry;
            }

            return result;
        }

        // one flat list, nearest first, zone headers folded away. the sorted block is put back where
        // the first bot line originally stood, so counters and the trailing blank keep their places;
        // a header only drops when that zone actually printed a bot (the stock "Alive & Loading = N"
        // counters match the same pattern and must survive)
        private static string RebuildSorted(string[] lines, int[] kind, string[] zones, List<BotEntry> bots)
        {
            var zonesWithBots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in bots)
            {
                if (b.Zone != null)
                {
                    zonesWithBots.Add(b.Zone);
                }
            }

            // nearest first. Inside the hysteresis band the order of the previous frame is kept, so
            // two bots that are about equally far away do not swap places every single frame
            var band = Math.Max(0f, Settings.SortHysteresis.Value);
            var order = InsertionSorted(bots, band);

            LastRank.Clear();
            for (var r = 0; r < order.Count; r++)
            {
                if (order[r].Id != null)
                {
                    LastRank[order[r].Id] = r;
                }
            }

            var output = new List<string>(lines.Length);
            var inserted = false;

            for (var i = 0; i < lines.Length; i++)
            {
                if (kind[i] == KindBot)
                {
                    if (!inserted)
                    {
                        output.AddRange(order.Select(entry => entry.Text));
                        inserted = true;
                    }

                    continue;
                }

                if (kind[i] == KindZoneHeader && zonesWithBots.Contains(zones[i]))
                {
                    continue;
                }

                output.Add(lines[i]);
            }

            if (!inserted)
            {
                output.AddRange(order.Select(entry => entry.Text));
            }

            return string.Join("\n", output);
        }

        // once per second, per bot: which distance the trend was fed and what it decided
        private static readonly Dictionary<string, float> DiagStamps = new Dictionary<string, float>();
        private static float _lastSnapshotLog;

        private static void LogSnapshot()
        {
            var now = Time.realtimeSinceStartup;
            if (now - _lastSnapshotLog < 1f)
            {
                return;
            }

            _lastSnapshotLog = now;
            Plugin.Log?.LogInfo("[botpos-diag] snapshot live=" + Snapshot.IsLive
                + " player=" + Snapshot.PlayerPosition.ToString("F1")
                + " look=" + (Snapshot.HasLook ? Snapshot.LookForward.ToString("F2") : "none")
                + " bearing=" + Settings.BearingSource.Value
                + " zones=" + Snapshot.ZoneCount);
        }

        private static void LogMeasure(string zone, string id, double distance, int direction, int sector, bool live)
        {
            var now = Time.realtimeSinceStartup;

            if (DiagStamps.TryGetValue(id, out var stamp) && now - stamp < 1f)
            {
                return;
            }

            DiagStamps[id] = now;

            Plugin.Log?.LogInfo("[botpos-diag] " + (live ? "player->bot " : "overlay ")
                + zone + " | " + id + " | d=" + distance.ToString("0.0", CultureInfo.InvariantCulture)
                + " m | trend=" + (direction > 0 ? "up" : direction < 0 ? "down" : "steady")
                + " | bearing=" + (sector < 0 ? "none" : sector.ToString(CultureInfo.InvariantCulture)));
        }

        private static string FormatLine(double distance, int direction, int sector, double verticalGap, string rest, bool colored)
        {
            var meters = (int)Math.Round(distance, MidpointRounding.AwayFromZero);

            var trend = direction > 0
                ? Settings.ArrowUp.Value
                : direction < 0
                    ? Settings.ArrowDown.Value
                    : Settings.ArrowSteady.Value;

            var trendColor = direction > 0 ? Settings.UpColor.Value : direction < 0 ? Settings.DownColor.Value : null;

            // "> [42m] [assault] ..." - both brackets go the close color when the bot is that near
            var close = Settings.HighlightClose.Value && distance < Math.Max(0f, Settings.CloseDistanceMeters.Value);

            // the stock "> " prefix is replaced by the rank marker of this bot's role; the tail is
            // built once here and reused at the end of the line
            var tail = BuildTail(rest, colored, out var rank);

            Builder.Clear();
            Builder.Append(string.IsNullOrEmpty(rank) ? "> " : rank).Append(' ');

            var bearing = Settings.ShowBearing.Value ? Bearing.Glyph(sector, colored, verticalGap) : string.Empty;

            if (!string.IsNullOrEmpty(bearing) && !Settings.BearingAfterDistance.Value)
            {
                Builder.Append(bearing).Append(' ');
            }

            AppendBracket(Builder, "[", close, colored);

            if (!string.IsNullOrEmpty(trend))
            {
                if (colored && !string.IsNullOrEmpty(trendColor))
                {
                    Builder.Append("<color=").Append(trendColor).Append('>')
                        .Append(trend).Append("</color>");
                }
                else
                {
                    Builder.Append(trend);
                }
            }

            Builder.Append(meters);

            if (Settings.ShowUnit.Value)
            {
                Builder.Append('m');
            }

            if (Settings.BearingAfterDistance.Value && !string.IsNullOrEmpty(bearing))
            {
                Builder.Append(' ').Append(bearing);
            }

            AppendBracket(Builder, "]", close, colored);
            Builder.Append(tail);
            return Builder.ToString();
        }

        // the stock tail of a bot line is " [role] [side] [difficulty] nickname"; it is split apart
        // and reassembled from the Tags settings. anything that does not match the expected shape
        // is passed through untouched, so a parse problem never eats part of a real line.
        // the role also decides the line marker written to rank
        private static string BuildTail(string rest, bool colored, out string rank)
        {
            var match = Tail.Match(rest);
            if (!match.Success)
            {
                rank = null;
                return rest;
            }

            var role = match.Groups["role"].Value;
            var side = match.Groups["side"].Value;
            var difficulty = match.Groups["difficulty"].Value;
            var nickname = match.Groups["nickname"].Value.TrimEnd();

            // WildSpawnType.ToString() gives "bossKilla", "followerGluhar", "sectantPriest" etc.
            if (role.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
            {
                rank = Settings.MarkerBoss.Value;
            }
            else if (role.StartsWith("follower", StringComparison.OrdinalIgnoreCase)
                     || role.StartsWith("sectant", StringComparison.OrdinalIgnoreCase))
            {
                rank = Settings.MarkerFollower.Value;
            }
            else
            {
                rank = Settings.MarkerCommon.Value;
            }

            if (colored && Settings.MarkerColored.Value && !string.IsNullOrEmpty(rank))
            {
                var isBoss = string.Equals(rank, Settings.MarkerBoss.Value, StringComparison.Ordinal);
                var tone = isBoss ? BossColor : FollowerColor;
                // tones only make sense while the marker glyph itself is not a color emoji
                if (!string.IsNullOrEmpty(tone))
                {
                    rank = "<color=" + tone + ">" + rank + "</color>";
                }
            }

            Builder2.Clear();

            if (Settings.ShowRole.Value && !string.IsNullOrEmpty(role))
            {
                Builder2.Append(" [").Append(role).Append(']');
            }

            if (Settings.ShowSide.Value && !string.IsNullOrEmpty(side))
            {
                Builder2.Append(" [").Append(side).Append(']');
            }

            if (Settings.ShowDifficulty.Value && !string.IsNullOrEmpty(difficulty))
            {
                Builder2.Append(" [").Append(difficulty).Append(']');
            }

            if (Settings.ShowNickname.Value && !string.IsNullOrEmpty(nickname))
            {
                Builder2.Append(' ').Append(nickname);
            }

            var tail = Builder2.ToString();
            return tail.Length > 0 ? tail : " ...";
        }

        // " [role] [side] [difficulty] nickname" - every field is optional: an empty role or
        // difficulty is printed as "[]" by the stock overlay, the nickname may contain spaces
        private static readonly Regex Tail = new Regex(
            @"^\s*(?:\[(?<role>[^\]]*)\])?\s*(?:\[(?<side>[^\]]*)\])?\s*(?:\[(?<difficulty>[^\]]*)\])?\s*(?<nickname>.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly StringBuilder Builder2 = new StringBuilder(256);

        // marker tones: a boss pops out red, an escort orange, a common bot stays uncolored (white)
        private const string BossColor = "#FF4C4C";
        private const string FollowerColor = "#FFB13D";

        private static void AppendBracket(StringBuilder builder, string bracket, bool close, bool colored)
        {
            var color = Settings.CloseColor.Value;
            if (!close || !colored || string.IsNullOrEmpty(color))
            {
                builder.Append(bracket);
                return;
            }

            builder.Append("<color=").Append(color).Append('>').Append(bracket).Append("</color>");
        }

        // the stock overlay prints "{_distance:n2}", which is culture and grouping dependent:
        // "42.13", "42,13" and "1 234,56" all have to land on one number
        private static double? ParseDistance(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var text = raw
                .Replace(" ", string.Empty)
                .Replace("\u00A0", string.Empty)
                .Replace("\u202F", string.Empty)
                .Trim();

            var lastComma = text.LastIndexOf(',');
            var lastDot = text.LastIndexOf('.');

            if (lastComma >= 0 && lastDot >= 0)
            {
                text = lastComma > lastDot
                    ? text.Replace(".", string.Empty).Replace(',', '.')
                    : text.Replace(",", string.Empty);
            }
            else if (lastComma >= 0)
            {
                text = text.Replace(',', '.');
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                return value;
            }

            return null;
        }
    }

    // trend of the player-to-bot gap: the current smoothed distance compared with the value from
    // WindowSeconds ago. the reference lags behind instead of following the movement, so the arrow
    // also shows up when only the local player moves
    internal static class Motion
    {
        private const double Steady = 0d;
        private const double Receding = 1d;
        private const double Approaching = -1d;

        private sealed class State
        {
            public double Smoothed;
            public bool Primed;
            public double Direction;
            public double PendingDirection;
            public int PendingCount;
            public double LastSeen;
            public int LastFrame = -1;
            public readonly Queue<double[]> Samples = new Queue<double[]>(16);
        }

        private static readonly Dictionary<string, State> States =
            new Dictionary<string, State>(StringComparer.Ordinal);

        private static double _lastPrune = double.NegativeInfinity;

        public static void Reset()
        {
            States.Clear();
        }

        public static int Direction(string id, double distance)
        {
            var now = (double)Time.unscaledTime;
            var frame = Time.frameCount;

            if (!States.TryGetValue(id, out var state))
            {
                state = new State { Smoothed = distance, LastSeen = now, LastFrame = frame };
                States[id] = state;
                return (int)Steady;
            }

            state.LastSeen = now;

            // OnGUI runs several passes per frame, positions only change between frames
            if (state.LastFrame == frame)
            {
                return (int)state.Direction;
            }

            state.LastFrame = frame;

            var window = Math.Max(0.1f, Settings.WindowSeconds.Value);
            var minSpeed = Math.Max(0.01f, Settings.MinSpeed.Value);
            var band = Math.Max(0.02f, Settings.DeadBandMeters.Value);

            // exponential smoothing absorbs single frame jitter
            var alpha = Math.Min(1.0, Math.Max(0.02, Settings.Smoothing.Value));
            state.Smoothed = state.Primed
                ? state.Smoothed + alpha * (distance - state.Smoothed)
                : distance;
            state.Primed = true;

            state.Samples.Enqueue(new[] { now, state.Smoothed });
            while (state.Samples.Count > 2 && state.Samples.Peek()[0] < now - window * 2)
            {
                state.Samples.Dequeue();
            }

            var reference = ReferenceSample(state.Samples, now - window);
            var delta = state.Smoothed - reference[1];

            // the gap has to cover both the jitter band and the configured minimum speed
            var elapsed = Math.Max(1e-3, now - reference[0]);
            var threshold = Math.Max(band, minSpeed * elapsed);

            var crossed = delta >= threshold ? Receding : delta <= -threshold ? Approaching : Steady;

            if (crossed == state.Direction)
            {
                state.PendingCount = 0;
                state.PendingDirection = 0;
                return (int)state.Direction;
            }

            if (crossed == Steady)
            {
                // let the arrow linger until the reading really settles, not on the first dip
                state.PendingCount++;
                if (state.PendingCount >= Math.Max(1, Settings.ConfirmFrames.Value))
                {
                    state.Direction = Steady;
                    state.PendingCount = 0;
                }

                return (int)state.Direction;
            }

            if (crossed == state.PendingDirection)
            {
                state.PendingCount++;
            }
            else
            {
                state.PendingDirection = crossed;
                state.PendingCount = 1;
            }

            if (state.PendingCount >= Math.Max(1, Settings.ConfirmFrames.Value))
            {
                state.Direction = crossed;
                state.PendingCount = 0;
                state.PendingDirection = 0;
            }

            return (int)state.Direction;
        }

        // oldest sample that is still inside the window, so the comparison always spans roughly
        // WindowSeconds of movement
        private static double[] ReferenceSample(Queue<double[]> samples, double targetTime)
        {
            double[] best = null;

            foreach (var sample in samples)
            {
                if (sample[0] <= targetTime)
                {
                    best = sample;
                }
                else
                {
                    break;
                }
            }

            return best ?? samples.ToArray()[0];
        }

        public static void Prune()
        {
            var now = (double)Time.unscaledTime;
            if (now - _lastPrune < 1.0 || States.Count == 0)
            {
                return;
            }

            _lastPrune = now;

            List<string> stale = null;
            foreach (var pair in States)
            {
                if (now - pair.Value.LastSeen > 3.0)
                {
                    (stale ?? (stale = new List<string>())).Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (var id in stale)
            {
                States.Remove(id);
            }
        }
    }

    // eight-way arrow around the local player, measured in the horizontal plane against the
    // direction the player looks (camera forward, yaw only). "Position" points at the bot,
    // "Movement" points the way it walks and is latched until the distance value moves again
    internal static class Bearing
    {
        private sealed class State
        {
            public double LastDistance = double.NaN;
            public Vector3 LastPosition;
            public double Heading;          // world yaw of the last measured movement, radians
            public bool HasHeading;
            public double LastSeen;
            public int LastFrame = -1;
        }

        private static readonly Dictionary<string, State> States =
            new Dictionary<string, State>(StringComparer.Ordinal);

        private static double _lastPrune = double.NegativeInfinity;

        public static void Reset()
        {
            States.Clear();
        }

        // sector index 0..7, clockwise from "straight ahead of your view", or -1 while no heading
        // has been measured yet
        public static int Sector(string id, Vector3 botPosition, double distance)
        {
            if (!Settings.ShowBearing.Value)
            {
                return -1;
            }

            var now = (double)Time.unscaledTime;
            var frame = Time.frameCount;

            if (!States.TryGetValue(id, out var state))
            {
                state = new State
                {
                    LastDistance = distance,
                    LastPosition = botPosition,
                    LastSeen = now,
                    LastFrame = frame,
                };

                States[id] = state;

                if (!IsMovementMode())
                {
                    state.HasHeading = TryHeadingToBot(botPosition, out state.Heading);
                }

                return IsMovementMode() ? -1 : SectorOf(state);
            }

            state.LastSeen = now;

            // OnGUI runs several passes per frame, positions only change between frames
            if (state.LastFrame == frame)
            {
                return SectorOf(state);
            }

            state.LastFrame = frame;

            if (!IsMovementMode())
            {
                // point at the bot: the horizontal vector from the player to it, in world axes
                if (TryHeadingToBot(botPosition, out var heading))
                {
                    state.Heading = heading;
                    state.HasHeading = true;
                }

                state.LastDistance = distance;
                state.LastPosition = botPosition;
                return SectorOf(state);
            }

            var refresh = Math.Max(0.01f, Settings.RefreshMeters.Value);
            var delta = botPosition - state.LastPosition;
            delta.y = 0f;

            // the heading is re-measured when the distance value moved, and also when the bot moved
            // sideways while keeping the gap; jitter stays under the threshold so it holds still
            if (Math.Abs(distance - state.LastDistance) >= refresh || delta.sqrMagnitude >= refresh * refresh)
            {
                if (delta.sqrMagnitude > 1e-4f)
                {
                    state.Heading = Math.Atan2(delta.x, delta.z);
                    state.HasHeading = true;
                }

                state.LastDistance = distance;
                state.LastPosition = botPosition;
            }

            return SectorOf(state);
        }

        private static bool IsMovementMode()
        {
            return string.Equals(Settings.BearingSource.Value, "Movement", StringComparison.OrdinalIgnoreCase);
        }

        // world yaw of the horizontal vector from the player to the bot; undefined when both stand
        // on the same spot, in which case the arrow is left out entirely
        private static bool TryHeadingToBot(Vector3 botPosition, out double heading)
        {
            var toBot = botPosition - Snapshot.PlayerPosition;
            toBot.y = 0f;

            heading = Math.Atan2(toBot.x, toBot.z);
            return toBot.sqrMagnitude > 1e-4f;
        }

        private static int SectorOf(State state)
        {
            if (!state.HasHeading)
            {
                return -1;
            }

            // relative to the look direction; without it the world axes are used instead
            var view = Snapshot.HasLook ? Math.Atan2(Snapshot.LookForward.x, Snapshot.LookForward.z) : 0d;

            var degrees = (state.Heading - view) * (180.0 / Math.PI);
            degrees %= 360.0;
            if (degrees < 0d)
            {
                degrees += 360.0;
            }

            return (int)Math.Round(degrees / 45.0) % 8;
        }

        public static string Glyph(int sector, bool colored, double verticalGap)
        {
            var glyph = GlyphOf(sector);
            if (string.IsNullOrEmpty(glyph))
            {
                return string.Empty;
            }

            if (!Settings.VariationSelectors.Value)
            {
                glyph = glyph.Replace("\uFE0F", string.Empty);
            }

            if (!colored)
            {
                return glyph;
            }

            // the arrow takes the elevation color when the Y gap clears the threshold, the plain
            // bearing color otherwise (same level)
            string color;
            if (Settings.Elevation.Value && verticalGap > Math.Max(0f, Settings.ElevationThreshold.Value))
            {
                color = Settings.ElevationUpColor.Value;
            }
            else if (Settings.Elevation.Value && verticalGap < -Math.Max(0f, Settings.ElevationThreshold.Value))
            {
                color = Settings.ElevationDownColor.Value;
            }
            else
            {
                color = Settings.BearingColor.Value;
            }

            if (string.IsNullOrEmpty(color))
            {
                return glyph;
            }

            return "<color=" + color + ">" + glyph + "</color>";
        }

        private static string GlyphOf(int sector)
        {
            switch (sector)
            {
                case 0: return Settings.BearingN.Value;
                case 1: return Settings.BearingNE.Value;
                case 2: return Settings.BearingE.Value;
                case 3: return Settings.BearingSE.Value;
                case 4: return Settings.BearingS.Value;
                case 5: return Settings.BearingSW.Value;
                case 6: return Settings.BearingW.Value;
                case 7: return Settings.BearingNW.Value;
                default: return null;
            }
        }

        public static void Prune()
        {
            var now = (double)Time.unscaledTime;
            if (now - _lastPrune < 1.0 || States.Count == 0)
            {
                return;
            }

            _lastPrune = now;

            List<string> stale = null;
            foreach (var pair in States)
            {
                if (now - pair.Value.LastSeen > 3.0)
                {
                    (stale ?? (stale = new List<string>())).Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (var id in stale)
            {
                States.Remove(id);
            }
        }
    }
}
