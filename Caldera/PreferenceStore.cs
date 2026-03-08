using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Caldera
{
    // ── Serializable model ────────────────────────────────────────────────────

    public class PreferencesData
    {
        public string Theme { get; set; } = "Corium";
        public string FontFamily { get; set; } = "Consolas";
        public double FontSize { get; set; } = 13;
        public double OutputFontSize { get; set; } = 12;

        public string ClangPath { get; set; } = string.Empty;
        public string GppPath { get; set; } = string.Empty;
        public string ClPath { get; set; } = string.Empty;
        public string McaPath { get; set; } = string.Empty;

        // Toolbar state
        public string Compiler { get; set; } = "clang++";
        public string Std { get; set; } = "c++20";
        public string CompilerFlags { get; set; } = "-O2 -march=native";
        public string McaFlags { get; set; } = "--mcpu=native";

        // Window geometry
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 800;
        public bool WindowMaximized { get; set; } = false;

        // Panel proportions
        public double EditorSplitRatio { get; set; } = 0.5;
        public double OutputPanelHeight { get; set; } = 220;

        // Flag presets: name → flags string
        public Dictionary<string, string> CompilerFlagPresets { get; set; } = new();
        public Dictionary<string, string> McaFlagPresets { get; set; } = new();
    }

    // ── Store ─────────────────────────────────────────────────────────────────

    public static class PreferencesStore
    {
        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "caldera_prefs.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
        };

        public static void Save(PreferencesData data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOpts);
                File.WriteAllText(FilePath, json);
            }
            catch { /* non-fatal */ }
        }

        public static PreferencesData Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new PreferencesData();
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<PreferencesData>(json, JsonOpts)
                       ?? new PreferencesData();
            }
            catch
            {
                return new PreferencesData();
            }
        }

        /// <summary>
        /// Applies a loaded PreferencesData to all runtime managers in one shot.
        /// </summary>
        public static void Apply(PreferencesData data)
        {
            // Compiler paths
            CompilerPaths.Clang = data.ClangPath;
            CompilerPaths.Gpp = data.GppPath;
            CompilerPaths.Cl = data.ClPath;
            CompilerPaths.Mca = data.McaPath;

            // Theme
            if (PreferencesWindow.Themes.TryGetValue(data.Theme, out var colors))
                ThemeManager.ApplyTheme(data.Theme, colors);

            // Font
            ThemeManager.ApplyFont(data.FontFamily, data.FontSize);
            ThemeManager.ApplyOutputFont(data.OutputFontSize);
        }
    }
}