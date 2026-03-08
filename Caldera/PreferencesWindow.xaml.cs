using System.Linq;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Caldera
{
    public partial class PreferencesWindow : Window
    {
        // ── Pending selections (committed on Apply) ──────────────────────────
        private string _pendingTheme;
        private string _pendingFontFamily;
        private double _pendingFontSize;
        private double _pendingOutputFontSize;
        private PreferencesData _prefs = null!;

        // ── Theme colour tables (public so PreferencesStore can access) ───────
        public static readonly Dictionary<string, Dictionary<string, Color>> Themes = new()
        {
            ["Corium"] = new()
            {
                ["AccentColor"] = (Color)ColorConverter.ConvertFromString("#ff4020"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#cc2010"),
                ["AccentGlowColor"] = (Color)ColorConverter.ConvertFromString("#ff4020"),
                ["BgDark"] = (Color)ColorConverter.ConvertFromString("#0a0005"),
                ["BgMid"] = (Color)ColorConverter.ConvertFromString("#1a0505"),
                ["BgLight"] = (Color)ColorConverter.ConvertFromString("#3a0a0a"),
                ["BorderDim"] = (Color)ColorConverter.ConvertFromString("#2a1010"),
                ["BorderMid"] = (Color)ColorConverter.ConvertFromString("#3a1010"),
                ["PanelBg"] = (Color)ColorConverter.ConvertFromString("#1a0808"),
                ["PanelBgDark"] = (Color)ColorConverter.ConvertFromString("#0d0505"),
                ["TextDim"] = (Color)ColorConverter.ConvertFromString("#5a3030"),
            },
            ["Iota"] = new()
            {
                ["AccentColor"] = (Color)ColorConverter.ConvertFromString("#00bcd4"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#0097a7"),
                ["AccentGlowColor"] = (Color)ColorConverter.ConvertFromString("#00bcd4"),
                ["BgDark"] = (Color)ColorConverter.ConvertFromString("#010c18"),
                ["BgMid"] = (Color)ColorConverter.ConvertFromString("#062030"),
                ["BgLight"] = (Color)ColorConverter.ConvertFromString("#0d2a40"),
                ["BorderDim"] = (Color)ColorConverter.ConvertFromString("#0d3048"),
                ["BorderMid"] = (Color)ColorConverter.ConvertFromString("#154060"),
                ["PanelBg"] = (Color)ColorConverter.ConvertFromString("#081a28"),
                ["PanelBgDark"] = (Color)ColorConverter.ConvertFromString("#050f1a"),
                ["TextDim"] = (Color)ColorConverter.ConvertFromString("#3a6878"),
            },
            ["StormSTL"] = new()
            {
                ["AccentColor"] = (Color)ColorConverter.ConvertFromString("#4caf50"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#388e3c"),
                ["AccentGlowColor"] = (Color)ColorConverter.ConvertFromString("#4caf50"),
                ["BgDark"] = (Color)ColorConverter.ConvertFromString("#010a01"),
                ["BgMid"] = (Color)ColorConverter.ConvertFromString("#071a07"),
                ["BgLight"] = (Color)ColorConverter.ConvertFromString("#0d2e0d"),
                ["BorderDim"] = (Color)ColorConverter.ConvertFromString("#103510"),
                ["BorderMid"] = (Color)ColorConverter.ConvertFromString("#1a4a1a"),
                ["PanelBg"] = (Color)ColorConverter.ConvertFromString("#081508"),
                ["PanelBgDark"] = (Color)ColorConverter.ConvertFromString("#040c04"),
                ["TextDim"] = (Color)ColorConverter.ConvertFromString("#386038"),
            },
            ["Spectra"] = new()
            {
                ["AccentColor"] = (Color)ColorConverter.ConvertFromString("#ffc107"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#f9a800"),
                ["AccentGlowColor"] = (Color)ColorConverter.ConvertFromString("#ffc107"),
                ["BgDark"] = (Color)ColorConverter.ConvertFromString("#0c0800"),
                ["BgMid"] = (Color)ColorConverter.ConvertFromString("#1a1000"),
                ["BgLight"] = (Color)ColorConverter.ConvertFromString("#2e1a00"),
                ["BorderDim"] = (Color)ColorConverter.ConvertFromString("#352000"),
                ["BorderMid"] = (Color)ColorConverter.ConvertFromString("#4a2e00"),
                ["PanelBg"] = (Color)ColorConverter.ConvertFromString("#201200"),
                ["PanelBgDark"] = (Color)ColorConverter.ConvertFromString("#100900"),
                ["TextDim"] = (Color)ColorConverter.ConvertFromString("#705000"),
            },
        };

        public PreferencesWindow(PreferencesData prefs)
        {
            InitializeComponent();

            _prefs = prefs;
            _pendingTheme = ThemeManager.CurrentTheme;
            _pendingFontFamily = ThemeManager.CurrentFontFamily;
            _pendingFontSize = ThemeManager.CurrentFontSize;
            _pendingOutputFontSize = ThemeManager.CurrentOutputFontSize;

            // Populate system font list
            var fonts = Fonts.SystemFontFamilies
                             .Select(f => f.Source)
                             .OrderBy(f => f)
                             .ToList();
            FontFamilyPicker.ItemsSource = fonts;
            FontFamilyPicker.SelectedItem = _pendingFontFamily;

            FontSizeSlider.Value = _pendingFontSize;
            OutputFontSizeSlider.Value = _pendingOutputFontSize;

            UpdateFontPreview();
            HighlightSelectedTheme(_pendingTheme);

            // Load saved compiler paths
            ClangPath.Text = CompilerPaths.Clang;
            GppPath.Text = CompilerPaths.Gpp;
            ClPath.Text = CompilerPaths.Cl;
            McaPath.Text = CompilerPaths.Mca;
        }

        // ── Theme card click ─────────────────────────────────────────────────
        private void Theme_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string name)
            {
                _pendingTheme = name;
                HighlightSelectedTheme(name);
            }
        }

        private void HighlightSelectedTheme(string name)
        {
            foreach (var b in new[] { ThemeCorium, ThemeIota, ThemeStormSTL, ThemeSpectra })
                b.Opacity = 0.55;

            var selected = name switch
            {
                "Corium" => ThemeCorium,
                "Iota" => ThemeIota,
                "StormSTL" => ThemeStormSTL,
                "Spectra" => ThemeSpectra,
                _ => ThemeCorium,
            };
            selected.Opacity = 1.0;
        }

        // ── Font controls ─────────────────────────────────────────────────────
        private void FontFamilyPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyPicker.SelectedItem is string f)
            {
                _pendingFontFamily = f;
                UpdateFontPreview();
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _pendingFontSize = FontSizeSlider.Value;
            if (FontSizeLabel != null)
                FontSizeLabel.Text = ((int)_pendingFontSize).ToString();
            UpdateFontPreview();
        }

        private void OutputFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _pendingOutputFontSize = OutputFontSizeSlider.Value;
            if (OutputFontSizeLabel != null)
                OutputFontSizeLabel.Text = ((int)_pendingOutputFontSize).ToString();
        }

        private void UpdateFontPreview()
        {
            if (FontPreview == null) return;
            FontPreview.FontFamily = new FontFamily(_pendingFontFamily ?? "Consolas");
            FontPreview.FontSize = _pendingFontSize > 0 ? _pendingFontSize : 13;
        }

        // ── Apply ─────────────────────────────────────────────────────────────
        private void ApplyPrefs_Click(object sender, RoutedEventArgs e)
        {
            // Apply theme
            if (Themes.TryGetValue(_pendingTheme, out var colors))
                ThemeManager.ApplyTheme(_pendingTheme, colors);

            // Apply fonts
            ThemeManager.ApplyFont(_pendingFontFamily, _pendingFontSize);
            ThemeManager.ApplyOutputFont(_pendingOutputFontSize);

            // Save compiler paths
            CompilerPaths.Clang = ClangPath.Text.Trim();
            CompilerPaths.Gpp = GppPath.Text.Trim();
            CompilerPaths.Cl = ClPath.Text.Trim();
            CompilerPaths.Mca = McaPath.Text.Trim();

            // Persist to JSON — mutate shared _prefs so no fields are clobbered
            _prefs.Theme = _pendingTheme;
            _prefs.FontFamily = _pendingFontFamily;
            _prefs.FontSize = _pendingFontSize;
            _prefs.OutputFontSize = _pendingOutputFontSize;
            _prefs.ClangPath = CompilerPaths.Clang;
            _prefs.GppPath = CompilerPaths.Gpp;
            _prefs.ClPath = CompilerPaths.Cl;
            _prefs.McaPath = CompilerPaths.Mca;
            PreferencesStore.Save(_prefs);

            Close();
        }

        private void ClosePrefs_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    }

    // ── Compiler path browser helpers ─────────────────────────────────────────
    public partial class PreferencesWindow
    {
        private void BrowseClang_Click(object sender, RoutedEventArgs e) =>
            BrowseForExe("clang++", path => ClangPath.Text = path);

        private void BrowseGpp_Click(object sender, RoutedEventArgs e) =>
            BrowseForExe("g++", path => GppPath.Text = path);

        private void BrowseCl_Click(object sender, RoutedEventArgs e) =>
            BrowseForExe("cl", path => ClPath.Text = path);

        private void BrowseMca_Click(object sender, RoutedEventArgs e) =>
            BrowseForExe("llvm-mca", path => McaPath.Text = path);

        private static void BrowseForExe(string title, Action<string> setter)
        {
            var dlg = new OpenFileDialog
            {
                Title = $"Locate {title}.exe",
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                setter(dlg.FileName);
        }
    }

    // ── Static compiler path store ────────────────────────────────────────────
    public static class CompilerPaths
    {
        public static string Clang { get; set; } = string.Empty;
        public static string Gpp { get; set; } = string.Empty;
        public static string Cl { get; set; } = string.Empty;
        public static string Mca { get; set; } = string.Empty;

        public static string Resolve(string compilerName) => compilerName switch
        {
            "clang++" => string.IsNullOrWhiteSpace(Clang) ? "clang++" : Clang,
            "g++" => string.IsNullOrWhiteSpace(Gpp) ? "g++" : Gpp,
            "cl.exe" => string.IsNullOrWhiteSpace(Cl) ? "cl" : Cl,
            "llvm-mca" => string.IsNullOrWhiteSpace(Mca) ? "llvm-mca" : Mca,
            _ => compilerName
        };
    }

    // ── Static theme manager ──────────────────────────────────────────────────
    public static class ThemeManager
    {
        public static string CurrentTheme { get; private set; } = "Corium";
        public static string CurrentFontFamily { get; private set; } = "Consolas";
        public static double CurrentFontSize { get; private set; } = 13;
        public static double CurrentOutputFontSize { get; private set; } = 12;

        public static event Action? ThemeChanged;
        public static event Action<string, double>? FontChanged;
        public static event Action<double>? OutputFontSizeChanged;

        public static void ApplyTheme(string name, Dictionary<string, Color> colors)
        {
            CurrentTheme = name;
            var res = Application.Current.Resources;
            foreach (var (key, value) in colors)
                res[key] = value;

            res["AccentBrush"] = new SolidColorBrush(colors["AccentColor"]);
            res["AccentDimBrush"] = new SolidColorBrush(colors["AccentDimColor"]);
            res["BorderDimBrush"] = new SolidColorBrush(colors["BorderDim"]);
            res["BorderMidBrush"] = new SolidColorBrush(colors["BorderMid"]);
            res["PanelBgBrush"] = new SolidColorBrush(colors["PanelBg"]);
            res["TextDimBrush"] = new SolidColorBrush(colors["TextDim"]);

            ThemeChanged?.Invoke();
        }

        public static void ApplyFont(string family, double size)
        {
            CurrentFontFamily = family;
            CurrentFontSize = size;
            FontChanged?.Invoke(family, size);
        }

        public static void ApplyOutputFont(double size)
        {
            CurrentOutputFontSize = size;
            OutputFontSizeChanged?.Invoke(size);
        }
    }
}