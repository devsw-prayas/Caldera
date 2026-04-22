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
        // ── Pending selections (committed on Apply) ───────────────────────────
        private string _pendingTheme;
        private string _pendingFontFamily;
        private double _pendingFontSize;
        private double _pendingOutputFontSize;
        private PreferencesData _prefs = null!;

        // ── Theme colour tables (public so PreferencesStore can access) ────────
        public static readonly Dictionary<string, Dictionary<string, Color>> Themes = new()
        {
            // 1. Bloodshed
            ["Bloodshed"] = new()
            {
                ["AccentColor"]    = (Color)ColorConverter.ConvertFromString("#ef4444"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#dc2626"),
                ["AccentGlowColor"]= (Color)ColorConverter.ConvertFromString("#f87171"),
                ["BgDark"]         = (Color)ColorConverter.ConvertFromString("#0a0101"),
                ["BgMid"]          = (Color)ColorConverter.ConvertFromString("#1e0202"),
                ["BgLight"]        = (Color)ColorConverter.ConvertFromString("#360404"),
                ["BorderDim"]      = (Color)ColorConverter.ConvertFromString("#290303"),
                ["BorderMid"]      = (Color)ColorConverter.ConvertFromString("#480606"),
                ["PanelBg"]        = (Color)ColorConverter.ConvertFromString("#140202"),
                ["PanelBgDark"]    = (Color)ColorConverter.ConvertFromString("#050000"),
                ["TextDim"]        = (Color)ColorConverter.ConvertFromString("#fca5a5"),
            },

            // 2. Outerspace
            ["Outerspace"] = new()
            {
                ["AccentColor"]    = (Color)ColorConverter.ConvertFromString("#d946ef"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#c026d3"),
                ["AccentGlowColor"]= (Color)ColorConverter.ConvertFromString("#f0abfc"),
                ["BgDark"]         = (Color)ColorConverter.ConvertFromString("#0c0012"),
                ["BgMid"]          = (Color)ColorConverter.ConvertFromString("#1a0028"),
                ["BgLight"]        = (Color)ColorConverter.ConvertFromString("#320050"),
                ["BorderDim"]      = (Color)ColorConverter.ConvertFromString("#240038"),
                ["BorderMid"]      = (Color)ColorConverter.ConvertFromString("#420068"),
                ["PanelBg"]        = (Color)ColorConverter.ConvertFromString("#140020"),
                ["PanelBgDark"]    = (Color)ColorConverter.ConvertFromString("#08000a"),
                ["TextDim"]        = (Color)ColorConverter.ConvertFromString("#f0abfc"),
            },

            // 3. Slipstream
            ["Slipstream"] = new()
            {
                ["AccentColor"]    = (Color)ColorConverter.ConvertFromString("#22d3ee"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#06b6d4"),
                ["AccentGlowColor"]= (Color)ColorConverter.ConvertFromString("#67e8f9"),
                ["BgDark"]         = (Color)ColorConverter.ConvertFromString("#02080c"),
                ["BgMid"]          = (Color)ColorConverter.ConvertFromString("#041a24"),
                ["BgLight"]        = (Color)ColorConverter.ConvertFromString("#062e40"),
                ["BorderDim"]      = (Color)ColorConverter.ConvertFromString("#052230"),
                ["BorderMid"]      = (Color)ColorConverter.ConvertFromString("#0a3f58"),
                ["PanelBg"]        = (Color)ColorConverter.ConvertFromString("#03121a"),
                ["PanelBgDark"]    = (Color)ColorConverter.ConvertFromString("#010609"),
                ["TextDim"]        = (Color)ColorConverter.ConvertFromString("#7dd3fc"),
            },

            // 4. Amber Steel
            ["Amber Steel"] = new()
            {
                ["AccentColor"]    = (Color)ColorConverter.ConvertFromString("#f59e0b"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#d97706"),
                ["AccentGlowColor"]= (Color)ColorConverter.ConvertFromString("#fcd34d"),
                ["BgDark"]         = (Color)ColorConverter.ConvertFromString("#080400"),
                ["BgMid"]          = (Color)ColorConverter.ConvertFromString("#1a0c00"),
                ["BgLight"]        = (Color)ColorConverter.ConvertFromString("#2e1600"),
                ["BorderDim"]      = (Color)ColorConverter.ConvertFromString("#231100"),
                ["BorderMid"]      = (Color)ColorConverter.ConvertFromString("#3a1c00"),
                ["PanelBg"]        = (Color)ColorConverter.ConvertFromString("#120800"),
                ["PanelBgDark"]    = (Color)ColorConverter.ConvertFromString("#040200"),
                ["TextDim"]        = (Color)ColorConverter.ConvertFromString("#c2a36b"),
            },

            // 5. Zenith
            ["Zenith"] = new()
            {
                ["AccentColor"]    = (Color)ColorConverter.ConvertFromString("#4ade80"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#22c55e"),
                ["AccentGlowColor"]= (Color)ColorConverter.ConvertFromString("#86efac"),
                ["BgDark"]         = (Color)ColorConverter.ConvertFromString("#020c05"),
                ["BgMid"]          = (Color)ColorConverter.ConvertFromString("#041e0a"),
                ["BgLight"]        = (Color)ColorConverter.ConvertFromString("#073d14"),
                ["BorderDim"]      = (Color)ColorConverter.ConvertFromString("#062d0e"),
                ["BorderMid"]      = (Color)ColorConverter.ConvertFromString("#0a5420"),
                ["PanelBg"]        = (Color)ColorConverter.ConvertFromString("#031407"),
                ["PanelBgDark"]    = (Color)ColorConverter.ConvertFromString("#010602"),
                ["TextDim"]        = (Color)ColorConverter.ConvertFromString("#86efac"),
            },

            // 6. Hailstorm
            ["Hailstorm"] = new()
            {
                ["AccentColor"]    = (Color)ColorConverter.ConvertFromString("#a8a29e"),
                ["AccentDimColor"] = (Color)ColorConverter.ConvertFromString("#78716c"),
                ["AccentGlowColor"]= (Color)ColorConverter.ConvertFromString("#d6d3d1"),
                ["BgDark"]         = (Color)ColorConverter.ConvertFromString("#0b0907"),
                ["BgMid"]          = (Color)ColorConverter.ConvertFromString("#1a1610"),
                ["BgLight"]        = (Color)ColorConverter.ConvertFromString("#2e2820"),
                ["BorderDim"]      = (Color)ColorConverter.ConvertFromString("#241e17"),
                ["BorderMid"]      = (Color)ColorConverter.ConvertFromString("#3e3628"),
                ["PanelBg"]        = (Color)ColorConverter.ConvertFromString("#13100a"),
                ["PanelBgDark"]    = (Color)ColorConverter.ConvertFromString("#060504"),
                ["TextDim"]        = (Color)ColorConverter.ConvertFromString("#d6d3d1"),
            },
        };

        public PreferencesWindow(PreferencesData prefs)
        {
            InitializeComponent();

            _prefs = prefs;
            _pendingTheme           = ThemeManager.CurrentTheme;
            _pendingFontFamily      = ThemeManager.CurrentFontFamily;
            _pendingFontSize        = ThemeManager.CurrentFontSize;
            _pendingOutputFontSize  = ThemeManager.CurrentOutputFontSize;

            var fonts = Fonts.SystemFontFamilies
                             .Select(f => f.Source)
                             .OrderBy(f => f)
                             .ToList();
            FontFamilyPicker.ItemsSource  = fonts;
            FontFamilyPicker.SelectedItem = _pendingFontFamily;
            FontSizeSlider.Value          = _pendingFontSize;
            OutputFontSizeSlider.Value    = _pendingOutputFontSize;

            UpdateFontPreview();
            
            ThemeComboBox.ItemsSource = Themes.Keys.OrderBy(k => k).ToList();
            ThemeComboBox.SelectedItem = _pendingTheme;

            ClangPath.Text = CompilerPaths.Clang;
            GppPath.Text   = CompilerPaths.Gpp;
            ClPath.Text    = CompilerPaths.Cl;
            McaPath.Text   = CompilerPaths.Mca;
        }

        // ── Theme card click ──────────────────────────────────────────────────

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is string name)
            {
                _pendingTheme = name;
            }
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
            FontPreview.FontSize   = _pendingFontSize > 0 ? _pendingFontSize : 13;
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        private void ApplyPrefs_Click(object sender, RoutedEventArgs e)
        {
            _prefs.Theme = _pendingTheme;
            ThemeManager.ApplyTheme(_pendingTheme);
            ThemeManager.ApplyFont(_pendingFontFamily, _pendingFontSize);
            ThemeManager.ApplyOutputFont(_pendingOutputFontSize);

            CompilerPaths.Clang = ClangPath.Text.Trim();
            CompilerPaths.Gpp   = GppPath.Text.Trim();
            CompilerPaths.Cl    = ClPath.Text.Trim();
            CompilerPaths.Mca   = McaPath.Text.Trim();

            _prefs.Theme            = _pendingTheme;
            _prefs.FontFamily       = _pendingFontFamily;
            _prefs.FontSize         = _pendingFontSize;
            _prefs.OutputFontSize   = _pendingOutputFontSize;
            _prefs.ClangPath        = CompilerPaths.Clang;
            _prefs.GppPath          = CompilerPaths.Gpp;
            _prefs.ClPath           = CompilerPaths.Cl;
            _prefs.McaPath          = CompilerPaths.Mca;
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
                Title  = $"Locate {title}.exe",
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                setter(dlg.FileName);
        }
    }
}