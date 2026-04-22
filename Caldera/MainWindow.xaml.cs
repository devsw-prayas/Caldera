using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Xml;

namespace Caldera
{
    public partial class MainWindow : Window
    {
        // ── Tab state ─────────────────────────────────────────────────────────
        private readonly ObservableCollection<TabSession> _tabs = new();
        private TabSession? _activeSession;

        // ── ASM highlight ─────────────────────────────────────────────────────
        private AsmHighlightRenderer? _asmHighlighter;
        private UI.DiffHighlightRenderer? _diffHighlighter;
        private UI.AsmSearchRenderer? _searchHighlighter;

        // ── Opcode reference panel ────────────────────────────────────────────
        private bool _opcodePanelOpen = false;

        // ── Persistent preferences ────────────────────────────────────────────
        private PreferencesData _prefs = new();

        private bool _compareRunning = false;

        // ── CancellationTokenSources ──────────────────────────────────────────
        private System.Threading.CancellationTokenSource? _compileCts;
        private System.Threading.CancellationTokenSource? _mcaCts;

        // ── Ctor ──────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            var chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
                NonClientFrameEdges = NonClientFrameEdges.None
            };
            WindowChrome.SetWindowChrome(this, chrome);

            Loaded += MainWindow_Loaded;
            ThemeManager.FontChanged     += OnFontChanged;
            ThemeManager.ThemeChanged    += OnThemeChanged;
            ThemeManager.OutputFontSizeChanged += OnOutputFontSizeChanged;

            // Keyboard shortcut: Ctrl+Enter → Run
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => RunButton_Click(this, new RoutedEventArgs())),
                Key.Enter, ModifierKeys.Control));

            // Ctrl+T → new tab, Ctrl+W → close tab
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => NewTab()),
                Key.T, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => CloseActiveTab()),
                Key.W, ModifierKeys.Control));

            // Ctrl+S → save, Ctrl+O → open
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => Save()),
                Key.S, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => Open()),
                Key.O, ModifierKeys.Control));
        }

        // ── Loaded ────────────────────────────────────────────────────────────
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1; DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int round = DWMWCP_ROUND; DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            var margins = new MARGINS();
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            StateChanged += OnStateChanged;

            // Restore window geometry
            _prefs = PreferencesStore.Load();
            if (!double.IsNaN(_prefs.WindowLeft) && !double.IsNaN(_prefs.WindowTop))
            {
                Left = _prefs.WindowLeft;
                Top  = _prefs.WindowTop;
            }
            Width  = _prefs.WindowWidth  > 400 ? _prefs.WindowWidth  : 1400;
            Height = _prefs.WindowHeight > 300 ? _prefs.WindowHeight : 800;
            if (_prefs.WindowMaximized)
                WindowState = WindowState.Maximized;

            Closing += (s, _) => 
            {
                SaveWindowGeometry();
                SessionStore.Save(_tabs);
            };

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, async () =>
            {
                InitAutoCompile();
                InitAsmHighlighter();
                RefreshFlagPicker();

                await CompilerDiscovery.DiscoverAsync();
                CompilerSelector.ItemsSource = CompilerDiscovery.Discovered;

                RestoreToolbarState(_prefs);

                if (_prefs.EditorSplitRatio > 0.1 && _prefs.EditorSplitRatio < 0.9)
                {
                    var totalWidth = EditorAreaGrid.ActualWidth;
                    if (totalWidth > 0)
                    {
                        EditorAreaGrid.ColumnDefinitions[0].Width = new GridLength(_prefs.EditorSplitRatio, GridUnitType.Star);
                        EditorAreaGrid.ColumnDefinitions[2].Width = new GridLength(1.0 - _prefs.EditorSplitRatio, GridUnitType.Star);
                    }
                }

                var savedTabs = SessionStore.Load();
                if (savedTabs.Count > 0)
                {
                    foreach (var pt in savedTabs)
                        RestoreTab(pt);
                }
                else
                {
                    NewTab(null, "int main() {\n    return 0;\n}");
                }

                // Defer everything to UI construction completion
                CompilerSelector.SelectionChanged += CompilerSelector_SelectionChanged;
                StdSelector.SelectionChanged += StdSelector_SelectionChanged;
                FlagsInput.TextChanged += ToolbarInput_TextChanged;
                McaFlagsInput.TextChanged += ToolbarInput_TextChanged;
            });
        }

        private void SaveWindowGeometry()
        {
            _prefs.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _prefs.WindowLeft   = Left;
                _prefs.WindowTop    = Top;
                _prefs.WindowWidth  = Width;
                _prefs.WindowHeight = Height;
            }
            var total = EditorAreaGrid.ColumnDefinitions[0].ActualWidth +
                        EditorAreaGrid.ColumnDefinitions[2].ActualWidth;
            if (total > 0)
                _prefs.EditorSplitRatio = EditorAreaGrid.ColumnDefinitions[0].ActualWidth / total;
            if (_outputVisible)
                _prefs.OutputPanelHeight = OutputRow.Height.Value;
            PreferencesStore.Save(_prefs);
        }

        private void UpdateTitle()
        {
            var name = _activeSession?.DisplayName ?? "untitled";
            Title = $"CALDERA  //  {name}";
            if (TitleFileLabel != null)
                TitleFileLabel.Text = name;
        }
    }
}