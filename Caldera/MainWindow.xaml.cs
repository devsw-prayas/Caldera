using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using System.Linq;
using System.Runtime.InteropServices;
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
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

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

            // ── AvalonEdit theming — defer until after first render ───────────
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                ThemeEditors();
            });
        }

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
                {
                    margin.SetValue(
                        System.Windows.Controls.Control.ForegroundProperty,
                        new SolidColorBrush(accent) { Opacity = 0.35 }
                    );
                }
            };
        }

        private static IHighlightingDefinition LoadThemeHighlighting(string themeName)
        {
            // Build absolute path relative to the exe so it works regardless of working dir
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var file = System.IO.Path.Combine(exeDir, "Highlighting", $"Cpp-{themeName}.xshd");

            if (!System.IO.File.Exists(file))
            {
                // Fallback: try relative path (design-time / debug without output copy)
                file = System.IO.Path.Combine("Highlighting", $"Cpp-{themeName}.xshd");
            }

            if (!System.IO.File.Exists(file))
                return HighlightingManager.Instance.GetDefinition("C++")!;

            using var reader = new XmlTextReader(file);
            return ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(
                reader, HighlightingManager.Instance);
        }

        private void ApplyCppTheme()
        {
            var def = LoadThemeHighlighting(ThemeManager.CurrentTheme);
            SourceEditor.SyntaxHighlighting = def;
        }

        private static IHighlightingDefinition LoadAsmHighlighting(string themeName)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var file = System.IO.Path.Combine(exeDir, "Highlighting", $"Asm-{themeName}.xshd");

            if (!System.IO.File.Exists(file))
            {
                // Fallback: try relative path (design-time / debug without output copy)
                file = System.IO.Path.Combine("Highlighting", $"Asm-{themeName}.xshd");
            }

            if (!System.IO.File.Exists(file))
                return HighlightingManager.Instance.GetDefinition("C++")!;

            using var reader = new XmlTextReader(file);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        private void ApplyAsmTheme()
        {
            var def = LoadAsmHighlighting(ThemeManager.CurrentTheme);
            AsmOutput.SyntaxHighlighting = def;
        }

        private void OnThemeChanged()
        {
            var res = Application.Current.Resources;
            GradStop0.Color = (Color)res["BgLight"];
            GradStop1.Color = (Color)res["BgMid"];
            GradStop2.Color = (Color)res["BgDark"];
            TitleGlow.Color = (Color)res["AccentColor"];
            Background = new SolidColorBrush((Color)res["BgDark"]);

            // Swap syntax highlighting for new theme
            ApplyCppTheme();
            ApplyAsmTheme();

            // Re-theme line number colors to match new accent
            var accent = (Color)res["AccentColor"];
            var lineNumBrush = new SolidColorBrush(accent) { Opacity = 0.35 };
            var srcMargin = SourceEditor.TextArea.LeftMargins
                                        .OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                                        .FirstOrDefault();
            var asmMargin = AsmOutput.TextArea.LeftMargins
                                     .OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                                     .FirstOrDefault();
            srcMargin?.SetValue(Control.ForegroundProperty, lineNumBrush);
            asmMargin?.SetValue(Control.ForegroundProperty, lineNumBrush);
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
            var ff = new FontFamily(family);
            SourceEditor.FontFamily = ff;
            SourceEditor.FontSize = size;
            // AsmOutput intentionally keeps Consolas for asm readability
        }

        private const string GitHubUrl = "https://github.com/placeholder";

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = GitHubUrl,
                UseShellExecute = true
            });
        }

        private void PreferencesMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var prefs = new PreferencesWindow { Owner = this };
            prefs.ShowDialog();
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                // Defer until after OS has finished positioning the window.
                // StateChanged fires before GetWindowRect reflects the final rect.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    RootBorder.Margin = GetMaximizedOverhang();
                });
            }
            else
            {
                RootBorder.Margin = new Thickness(0);
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;
            }
        }

        /// <summary>
        /// Measures how far the maximized window rect overshoots the monitor
        /// work area on each side and returns that as the margin to pull inward.
        /// This is exact — no system metric guessing.
        /// </summary>
        private Thickness GetMaximizedOverhang()
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // Get the actual window rect in physical pixels
            GetWindowRect(hwnd, out RECT winRect);

            // Get the work area of the monitor this window is on
            var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(hMon, ref mi);
            var work = mi.rcWork;

            // Overhang = how many physical pixels the window extends beyond the work area
            int left = work.Left - winRect.Left;
            int top = work.Top - winRect.Top;
            int right = winRect.Right - work.Right;
            int bottom = winRect.Bottom - work.Bottom;

            // Convert physical → logical pixels
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null)
                return new Thickness(left, top, right, bottom);

            var m = source.CompositionTarget.TransformFromDevice;
            return new Thickness(
                left * m.M11,
                top * m.M22,
                right * m.M11,
                bottom * m.M22);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { MaximizeButton_Click(sender, e); return; }
            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private bool _outputVisible = false;

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            ShowOutputPanel();
            CompilerOutput.Text = "clang++ -O3 -std=c++20 source.cpp -o output\nCompilation successful. (0 errors, 0 warnings)";
            AsmOutput.Text = "main:\n\tpush rbp\n\tmov rbp, rsp\n\txor eax, eax\n\tpop rbp\n\tret";
            McaOutput.Text = "[0] Code Region\n\nIteration: 1\nInstructions: 5\nTotal Cycles: 4\nTotal uOps: 5\n\nDispatch Width: 6\nUops Per Cycle: 1.25\nIPC: 1.25";
        }

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
                FillBehavior = FillBehavior.Stop   // release the property when done
            };
            animation.Completed += (s, e) =>
            {
                // Hand control back to the GridSplitter by setting a plain value
                // and removing the animation clock from the property
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
            animation.Completed += (s, e) =>
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