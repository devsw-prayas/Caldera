using System.Collections.Generic;
using System.Windows.Media;
using System.Windows;

namespace Caldera
{
    // ── Theme manager ─────────────────────────────────────────────────────────
    //
    // Owns the current theme / font state and fires events consumed by
    // MainWindow so editors and chrome refresh without coupling to the
    // preferences dialog directly.

    public static class ThemeManager
    {
        public static string CurrentTheme        { get; private set; } = "Corium";
        public static string CurrentFontFamily   { get; private set; } = "Consolas";
        public static double CurrentFontSize     { get; private set; } = 13;
        public static double CurrentOutputFontSize { get; private set; } = 12;

        public static event Action?               ThemeChanged;
        public static event Action<string, double>? FontChanged;
        public static event Action<double>?       OutputFontSizeChanged;

        public static void ApplyTheme(string name, Dictionary<string, Color> colors)
        {
            CurrentTheme = name;
            var res = Application.Current.Resources;
            foreach (var (key, value) in colors)
                res[key] = value;

            res["AccentBrush"]    = new SolidColorBrush(colors["AccentColor"]);
            res["AccentDimBrush"] = new SolidColorBrush(colors["AccentDimColor"]);
            res["BorderDimBrush"] = new SolidColorBrush(colors["BorderDim"]);
            res["BorderMidBrush"] = new SolidColorBrush(colors["BorderMid"]);
            res["PanelBgBrush"]   = new SolidColorBrush(colors["PanelBg"]);
            res["TextDimBrush"]   = new SolidColorBrush(colors["TextDim"]);

            ThemeChanged?.Invoke();
        }

        public static void ApplyFont(string family, double size)
        {
            CurrentFontFamily = family;
            CurrentFontSize   = size;
            FontChanged?.Invoke(family, size);
        }

        public static void ApplyOutputFont(double size)
        {
            CurrentOutputFontSize = size;
            OutputFontSizeChanged?.Invoke(size);
        }
    }
}
