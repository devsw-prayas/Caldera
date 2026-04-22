using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Caldera
{
    public class PersistedTab
    {
        public string? FilePath { get; set; }
        public string? Content { get; set; }
        public string Compiler { get; set; } = "clang++";
        public string Std { get; set; } = "c++20";
        public string Flags { get; set; } = "-O2 -march=native";
        public string McaFlags { get; set; } = "--mcpu=native";
    }

    public static class SessionStore
    {
        private static readonly string SettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Caldera");
        private static readonly string SessionFile = Path.Combine(SettingsDir, "session.json");

        public static void Save(IEnumerable<TabSession> tabs)
        {
            try
            {
                if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir);
                var persisted = tabs.Select(t => new PersistedTab
                {
                    FilePath = t.FilePath,
                    Content = t.Document.Text,
                    Compiler = t.Compiler,
                    Std = t.Std,
                    Flags = t.Flags,
                    McaFlags = t.McaFlags
                }).ToList();
                var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SessionFile, json);
            }
            catch { }
        }

        public static List<PersistedTab> Load()
        {
            try
            {
                if (File.Exists(SessionFile))
                {
                    var json = File.ReadAllText(SessionFile);
                    return JsonSerializer.Deserialize<List<PersistedTab>>(json) ?? new List<PersistedTab>();
                }
            }
            catch { }
            return new List<PersistedTab>();
        }
    }
}
