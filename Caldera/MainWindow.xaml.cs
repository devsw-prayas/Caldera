using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    // ── MainWindow ────────────────────────────────────────────────────────────

    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

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
            ThemeManager.FontChanged += OnFontChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;
            ThemeManager.OutputFontSizeChanged += OnOutputFontSizeChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 0 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            StateChanged += OnStateChanged;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                ThemeEditors();
                InitAsmHighlighter();
                RefreshFlagPicker();   // populate picker for default compiler
            });
        }

        // ── Theming ───────────────────────────────────────────────────────────

        private void ThemeEditors()
        {
            var accent = (Color)Application.Current.Resources["AccentColor"];
            SetLineNumberColor(SourceEditor, accent);
            SetLineNumberColor(AsmOutput, accent);
            ApplyCppTheme();
            ApplyAsmTheme();
        }

        private static void SetLineNumberColor(ICSharpCode.AvalonEdit.TextEditor editor, Color accent)
        {
            editor.Loaded += (s, e) =>
            {
                var margin = editor.TextArea.LeftMargins
                    .OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                    .FirstOrDefault();
                if (margin != null)
                    margin.SetValue(System.Windows.Controls.Control.ForegroundProperty,
                        new SolidColorBrush(accent) { Opacity = 0.35 });
            };
        }

        private static IHighlightingDefinition LoadThemeHighlighting(string themeName)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var file = System.IO.Path.Combine(exeDir, "Highlighting", $"Cpp-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                file = System.IO.Path.Combine("Highlighting", $"Cpp-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                return HighlightingManager.Instance.GetDefinition("C++")!;
            using var reader = new XmlTextReader(file);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        private void ApplyCppTheme()
        {
            SourceEditor.SyntaxHighlighting = LoadThemeHighlighting(ThemeManager.CurrentTheme);
        }

        private static IHighlightingDefinition LoadAsmHighlighting(string themeName)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var file = System.IO.Path.Combine(exeDir, "Highlighting", $"Asm-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                file = System.IO.Path.Combine("Highlighting", $"Asm-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                return HighlightingManager.Instance.GetDefinition("C++")!;
            using var reader = new XmlTextReader(file);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        private void ApplyAsmTheme()
        {
            AsmOutput.SyntaxHighlighting = LoadAsmHighlighting(ThemeManager.CurrentTheme);
        }

        private void OnThemeChanged()
        {
            var res = Application.Current.Resources;
            GradStop0.Color = (Color)res["BgLight"];
            GradStop1.Color = (Color)res["BgMid"];
            GradStop2.Color = (Color)res["BgDark"];
            TitleGlow.Color = (Color)res["AccentColor"];
            Background = new SolidColorBrush((Color)res["BgDark"]);
            ApplyCppTheme();
            ApplyAsmTheme();
            var accent = (Color)res["AccentColor"];
            var lineNumBrush = new SolidColorBrush(accent) { Opacity = 0.35 };
            SourceEditor.TextArea.LeftMargins.OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                .FirstOrDefault()?.SetValue(Control.ForegroundProperty, lineNumBrush);
            AsmOutput.TextArea.LeftMargins.OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                .FirstOrDefault()?.SetValue(Control.ForegroundProperty, lineNumBrush);
            _asmHighlighter?.UpdateColor(accent);
        }

        private void OnOutputFontSizeChanged(double size)
        {
            AsmOutput.FontSize = size;
            AsmOutput.TextArea.FontSize = size;
            AsmOutput.TextArea.TextView.Redraw();
            CompilerOutput.FontSize = size;
            McaOutput.FontSize = size;
        }

        private void OnFontChanged(string family, double size)
        {
            SourceEditor.FontFamily = new FontFamily(family);
            SourceEditor.FontSize = size;
        }

        // ── Asm highlight ─────────────────────────────────────────────────────

        private Dictionary<int, List<int>> _asmMap = new();
        private AsmHighlightRenderer? _asmHighlighter;

        private void InitAsmHighlighter()
        {
            var accent = (Color)Application.Current.Resources["AccentColor"];
            _asmHighlighter = new AsmHighlightRenderer(AsmOutput, accent);
            AsmOutput.TextArea.TextView.BackgroundRenderers.Add(_asmHighlighter);
            SourceEditor.TextArea.Caret.PositionChanged += OnSourceCaretMoved;
        }

        private void OnSourceCaretMoved(object? sender, EventArgs e)
        {
            if (_asmHighlighter == null || _asmMap.Count == 0) return;
            int srcLine = SourceEditor.TextArea.Caret.Line;
            if (_asmMap.TryGetValue(srcLine, out var asmLines))
                _asmHighlighter.SetHighlightedLines(asmLines);
            else
                _asmHighlighter.Clear();
        }

        // ── Flag picker ───────────────────────────────────────────────────────

        /// <summary>
        /// Repopulates the flag picker ItemsControl based on the currently selected compiler.
        /// </summary>
        private void RefreshFlagPicker()
        {
            if (FlagGroupsControl is null) return;
            var compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            FlagGroupsControl.ItemsSource = FlagPickerData.CompilerFlags.TryGetValue(compiler, out var groups)
                ? groups
                : new List<FlagGroup>();
        }

        private void CompilerSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshFlagPicker();
        }

        private void FlagPickerButton_Click(object sender, RoutedEventArgs e)
        {
            FlagPickerPopup.IsOpen = !FlagPickerPopup.IsOpen;
        }

        private void FlagItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string flag)
            {
                var current = FlagsInput.Text.TrimEnd();
                FlagsInput.Text = string.IsNullOrWhiteSpace(current)
                    ? flag
                    : current + " " + flag;
                FlagsInput.CaretIndex = FlagsInput.Text.Length;
                FlagPickerPopup.IsOpen = false;
            }
        }

        private void McaFlagPickerButton_Click(object sender, RoutedEventArgs e)
        {
            if (McaFlagGroupsControl.ItemsSource is null)
                McaFlagGroupsControl.ItemsSource = FlagPickerData.McaFlags;
            McaFlagPickerPopup.IsOpen = !McaFlagPickerPopup.IsOpen;
        }

        private void McaFlagItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string flag)
            {
                var current = McaFlagsInput.Text.TrimEnd();
                McaFlagsInput.Text = string.IsNullOrWhiteSpace(current)
                    ? flag
                    : current + " " + flag;
                McaFlagsInput.CaretIndex = McaFlagsInput.Text.Length;
                McaFlagPickerPopup.IsOpen = false;
            }
        }

        // ── Menu / toolbar ────────────────────────────────────────────────────

        private const string GitHubUrl = "https://github.com/placeholder";

        private void HelpButton_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = GitHubUrl, UseShellExecute = true });

        private void PreferencesMenuButton_Click(object sender, RoutedEventArgs e)
        {
            new PreferencesWindow { Owner = this }.ShowDialog();
        }

        // ── Run ───────────────────────────────────────────────────────────────

        private bool _isRunning = false;

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            if (string.IsNullOrWhiteSpace(SourceEditor.Text)) return;

            _isRunning = true;
            RunButton.IsEnabled = false;

            ShowOutputPanel();
            CompilerOutput.Text = "Compiling...";
            AsmOutput.Text = string.Empty;
            McaOutput.Text = string.Empty;
            _asmMap.Clear();
            _asmHighlighter?.Clear();

            try
            {
                var compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
                var flags = FlagsInput.Text.Trim();
                var std = (StdSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "c++20";

                var result = await CompilerService.CompileAsync(compiler, std, flags, SourceEditor.Text);

                CompilerOutput.Text = result.CompilerOutput;
                AsmOutput.Text = result.AsmOutput;
                _asmMap = result.AsmMap;
                _lastCompilerKind = result.CompilerKind;

                if (_asmMap.Count > 0)
                    OnSourceCaretMoved(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                CompilerOutput.Text = $"Error launching compiler:\n{ex.Message}";
            }
            finally
            {
                _isRunning = false;
                RunButton.IsEnabled = true;
            }
        }

        // ── MCA ───────────────────────────────────────────────────────────────

        private bool _mcaRunning = false;

        private AsmMapper.CompilerKind _lastCompilerKind = AsmMapper.CompilerKind.ClangOrGcc;

        private async void McaButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mcaRunning) return;
            _mcaRunning = true;
            McaButton.IsEnabled = false;
            McaOutput.Text = "Running llvm-mca...";

            try
            {
                var result = await CompilerService.RunMcaAsync(
                    AsmOutput.Text, McaFlagsInput.Text.Trim(), _lastCompilerKind);
                McaOutput.Text = result.Output;
            }
            catch (Exception ex)
            {
                McaOutput.Text = $"Error launching llvm-mca:\n{ex.Message}";
            }
            finally
            {
                _mcaRunning = false;
                McaButton.IsEnabled = true;
            }
        }

        // ── Window chrome ─────────────────────────────────────────────────────

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                    RootBorder.Margin = GetMaximizedOverhang());
            else
            {
                RootBorder.Margin = new Thickness(0);
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;
            }
        }

        private Thickness GetMaximizedOverhang()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            GetWindowRect(hwnd, out RECT winRect);
            var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(hMon, ref mi);
            var work = mi.rcWork;
            int left = work.Left - winRect.Left, top = work.Top - winRect.Top;
            int right = winRect.Right - work.Right, bottom = winRect.Bottom - work.Bottom;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return new Thickness(left, top, right, bottom);
            var m = source.CompositionTarget.TransformFromDevice;
            return new Thickness(left * m.M11, top * m.M22, right * m.M11, bottom * m.M22);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { MaximizeButton_Click(sender, e); return; }
            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // ── Output panel animation ────────────────────────────────────────────

        private bool _outputVisible = false;

        public void ShowOutputPanel()
        {
            if (_outputVisible) return;
            _outputVisible = true;
            OutputPanel.Visibility = Visibility.Visible;
            OutputSplitter.Visibility = Visibility.Visible;
            var animation = new GridLengthAnimation
            {
                From = new GridLength(0),
                To = new GridLength(220),
                Duration = new Duration(TimeSpan.FromMilliseconds(280)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (s, ev) =>
            {
                OutputRow.BeginAnimation(RowDefinition.HeightProperty, null);
                OutputRow.Height = new GridLength(220);
            };
            OutputRow.BeginAnimation(RowDefinition.HeightProperty, animation);
        }

        public void HideOutputPanel()
        {
            if (!_outputVisible) return;
            double currentHeight = OutputRow.ActualHeight;
            var animation = new GridLengthAnimation
            {
                From = new GridLength(currentHeight),
                To = new GridLength(0),
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (s, ev) =>
            {
                OutputRow.BeginAnimation(RowDefinition.HeightProperty, null);
                OutputRow.Height = new GridLength(0);
                OutputPanel.Visibility = Visibility.Collapsed;
                OutputSplitter.Visibility = Visibility.Collapsed;
                _outputVisible = false;
            };
            OutputRow.BeginAnimation(RowDefinition.HeightProperty, animation);
        }
    }
}