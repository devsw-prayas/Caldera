using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
    public partial class MainWindow : Window
    {
        // ── DWM P/Invokes ─────────────────────────────────────────────────────
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        const uint MONITOR_DEFAULTTONEAREST = 2;

        // ── Tab state ─────────────────────────────────────────────────────────
        private readonly ObservableCollection<TabSession> _tabs = new();
        private TabSession? _activeSession;

        // ── ASM highlight ─────────────────────────────────────────────────────
        private AsmHighlightRenderer? _asmHighlighter;

        // ── Opcode reference panel ────────────────────────────────────────────
        private bool _opcodePanelOpen = false;

        // ── Run state ─────────────────────────────────────────────────────────
        private bool _isRunning = false;
        private bool _mcaRunning = false;

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
            ThemeManager.FontChanged += OnFontChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;
            ThemeManager.OutputFontSizeChanged += OnOutputFontSizeChanged;

            // Keyboard shortcut: Ctrl+Enter → Run
            var runBinding = new KeyBinding(
                new RelayCommand(_ => RunButton_Click(this, new RoutedEventArgs())),
                Key.Enter, ModifierKeys.Control);
            InputBindings.Add(runBinding);

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

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                InitAsmHighlighter();
                RefreshFlagPicker();

                // Restore toolbar state from prefs
                var prefs = PreferencesStore.Load();
                RestoreToolbarState(prefs);

                // Create initial tab
                NewTab();
            });
        }

        // ── Tab management ────────────────────────────────────────────────────

        private void NewTab(string? filePath = null, string? content = null)
        {
            var session = new TabSession { FilePath = filePath };
            if (content != null)
                session.Document.Text = content;

            _tabs.Add(session);
            SourceTabControl.ItemsSource = _tabs;
            SourceTabControl.SelectedItem = session;
            ActivateTab(session);
        }

        private void ActivateTab(TabSession session)
        {
            if (_activeSession == session) return;
            _activeSession = session;

            // Swap the editor document
            SourceEditor.Document = session.Document;

            // Restore compile results
            AsmOutput.Text = session.AsmText;
            CompilerOutput.Text = session.CompilerText;
            McaOutput.Text = session.McaText;

            // Restore ASM highlight
            _asmHighlighter?.Clear();
            if (session.AsmMap.Count > 0)
                OnSourceCaretMoved(null, EventArgs.Empty);

            // Update title
            UpdateTitle();

            // Re-hook dirty tracking
            session.Document.TextChanged -= OnDocumentTextChanged;
            session.Document.TextChanged += OnDocumentTextChanged;
        }

        private void OnDocumentTextChanged(object? sender, EventArgs e)
        {
            if (_activeSession == null) return;
            _activeSession.IsDirty = true;
            UpdateTitle();
            RefreshTabHeader(_activeSession);
        }

        private void RefreshTabHeader(TabSession session)
        {
            // Force the tab header binding to refresh
            var idx = _tabs.IndexOf(session);
            if (idx >= 0)
            {
                _tabs.RemoveAt(idx);
                _tabs.Insert(idx, session);
                SourceTabControl.SelectedItem = session;
            }
        }

        private void SourceTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceTabControl.SelectedItem is TabSession session)
                ActivateTab(session);
        }

        private void NewTabButton_Click(object sender, RoutedEventArgs e) => NewTab();

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is TabSession session)
                TryCloseTab(session);
        }

        private void CloseActiveTab()
        {
            if (_activeSession != null)
                TryCloseTab(_activeSession);
        }

        private void TryCloseTab(TabSession session)
        {
            if (session.IsDirty)
            {
                var dlg = new UnsavedChangesDialog(
                    $"Save changes to {session.DisplayName.TrimEnd('•').TrimEnd(' ')}?", this);
                dlg.ShowDialog();
                var result = dlg.Result;
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes && !SaveSession(session)) return;
            }

            var idx = _tabs.IndexOf(session);
            _tabs.Remove(session);

            if (_tabs.Count == 0)
                NewTab();
            else
                SourceTabControl.SelectedItem = _tabs[Math.Max(0, idx - 1)];
        }

        // ── File operations ───────────────────────────────────────────────────

        private void NewFile_Click(object sender, RoutedEventArgs e) => NewTab();
        private void OpenFile_Click(object sender, RoutedEventArgs e) => Open();
        private void SaveFile_Click(object sender, RoutedEventArgs e) => Save();
        private void SaveAsFile_Click(object sender, RoutedEventArgs e) => SaveAs();

        private void Open()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open Source File",
                Filter = "C++ Files (*.cpp;*.cxx;*.cc;*.h;*.hpp)|*.cpp;*.cxx;*.cc;*.h;*.hpp|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            // Check if already open
            var existing = _tabs.FirstOrDefault(t => t.FilePath == dlg.FileName);
            if (existing != null) { SourceTabControl.SelectedItem = existing; return; }

            var content = System.IO.File.ReadAllText(dlg.FileName);
            NewTab(dlg.FileName, content);
            _activeSession!.IsDirty = false;
            UpdateTitle();
        }

        private void Save()
        {
            if (_activeSession == null) return;
            SaveSession(_activeSession);
        }

        private void SaveAs()
        {
            if (_activeSession == null) return;
            SaveSessionAs(_activeSession);
        }

        private bool SaveSession(TabSession session)
        {
            if (session.FilePath == null)
                return SaveSessionAs(session);

            System.IO.File.WriteAllText(session.FilePath, session.Document.Text);
            session.IsDirty = false;
            UpdateTitle();
            RefreshTabHeader(session);
            return true;
        }

        private bool SaveSessionAs(TabSession session)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save Source File",
                Filter = "C++ Files (*.cpp)|*.cpp|Header Files (*.h;*.hpp)|*.h;*.hpp|All Files (*.*)|*.*",
                DefaultExt = ".cpp",
                FileName = session.FilePath ?? "untitled.cpp"
            };
            if (dlg.ShowDialog() != true) return false;

            session.FilePath = dlg.FileName;
            System.IO.File.WriteAllText(session.FilePath, session.Document.Text);
            session.IsDirty = false;
            UpdateTitle();
            RefreshTabHeader(session);
            return true;
        }

        private void UpdateTitle()
        {
            var name = _activeSession?.DisplayName ?? "untitled";
            Title = $"CALDERA  //  {name}";
            if (TitleFileLabel != null)
                TitleFileLabel.Text = name;
        }

        // ── Clear ─────────────────────────────────────────────────────────────

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null) return;
            _activeSession.Document.Text = string.Empty;
            _activeSession.AsmText = string.Empty;
            _activeSession.CompilerText = string.Empty;
            _activeSession.McaText = string.Empty;
            _activeSession.AsmMap = new();
            AsmOutput.Text = string.Empty;
            CompilerOutput.Text = string.Empty;
            McaOutput.Text = string.Empty;
            _asmHighlighter?.Clear();
            HideOutputPanel();
        }

        // ── Copy ASM ──────────────────────────────────────────────────────────

        private void CopyAsmButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(AsmOutput.Text))
                Clipboard.SetText(AsmOutput.Text);
        }

        // ── Godbolt ───────────────────────────────────────────────────────────

        private void GodboltButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null) return;

            var compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            var flags = FlagsInput.Text.Trim();
            var source = _activeSession.Document.Text;

            // Encode as Godbolt clientstate JSON → base64 URL
            var godboltCompiler = compiler switch
            {
                "clang++" => "clang_trunk",
                "g++" => "gsnapshot",
                "cl.exe" => "vcpp_v19_latest_x64",
                _ => "clang_trunk"
            };

            var json = $@"{{""sessions"":[{{""id"":1,""language"":""c++"",""source"":""{EscapeJson(source)}"",""compilers"":[{{""id"":""{godboltCompiler}"",""options"":""{EscapeJson(flags)}""}}]}}]}}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var url = $"https://godbolt.org/clientstate/{encoded}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = url, UseShellExecute = true });
        }

        private static string EscapeJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")
             .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

        // ── Theming ───────────────────────────────────────────────────────────

        private void ThemeEditors()
        {
            var accent = (Color)Application.Current.Resources["AccentColor"];
            SetLineNumberColor(SourceEditor, accent);
            SetLineNumberColor(AsmOutput, accent);
            ApplyCppTheme();
            ApplyAsmTheme();
        }

        private static void SetLineNumberColor(TextEditor editor, Color accent)
        {
            var margin = editor.TextArea.LeftMargins
                .OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                .FirstOrDefault();
            if (margin != null)
                margin.SetValue(Control.ForegroundProperty,
                    new SolidColorBrush(accent) { Opacity = 0.35 });
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

        private void ApplyCppTheme() =>
            SourceEditor.SyntaxHighlighting = LoadThemeHighlighting(ThemeManager.CurrentTheme);

        private void ApplyAsmTheme() =>
            AsmOutput.SyntaxHighlighting = LoadAsmHighlighting(ThemeManager.CurrentTheme);

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

        // ── ASM highlight ─────────────────────────────────────────────────────

        private void InitAsmHighlighter()
        {
            var accent = (Color)Application.Current.Resources["AccentColor"];
            _asmHighlighter = new AsmHighlightRenderer(AsmOutput, accent);
            AsmOutput.TextArea.TextView.BackgroundRenderers.Add(_asmHighlighter);
            SourceEditor.TextArea.Caret.PositionChanged += OnSourceCaretMoved;
            AsmOutput.TextArea.Caret.PositionChanged += OnAsmCaretMoved;
            ThemeEditors();
        }

        private void OnSourceCaretMoved(object? sender, EventArgs e)
        {
            if (_asmHighlighter == null || _activeSession == null) return;
            var map = _activeSession.AsmMap;
            if (map.Count == 0) return;
            int srcLine = SourceEditor.TextArea.Caret.Line;
            if (map.TryGetValue(srcLine, out var asmLines))
                _asmHighlighter.SetHighlightedLines(asmLines);
            else
                _asmHighlighter.Clear();
        }

        // ── Flag picker ───────────────────────────────────────────────────────

        private void RefreshFlagPicker()
        {
            if (FlagGroupsControl is null) return;
            var compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            FlagGroupsControl.ItemsSource = FlagPickerData.CompilerFlags.TryGetValue(compiler, out var groups)
                ? groups : new List<FlagGroup>();
        }

        private void CompilerSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            RefreshFlagPicker();

        private void FlagPickerButton_Click(object sender, RoutedEventArgs e) =>
            FlagPickerPopup.IsOpen = !FlagPickerPopup.IsOpen;

        private void FlagItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string flag)
            {
                var current = FlagsInput.Text.TrimEnd();
                FlagsInput.Text = string.IsNullOrWhiteSpace(current) ? flag : current + " " + flag;
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
                McaFlagsInput.Text = string.IsNullOrWhiteSpace(current) ? flag : current + " " + flag;
                McaFlagsInput.CaretIndex = McaFlagsInput.Text.Length;
                McaFlagPickerPopup.IsOpen = false;
            }
        }

        // ── Toolbar state persistence ─────────────────────────────────────────

        private void RestoreToolbarState(PreferencesData prefs)
        {
            // Compiler
            foreach (ComboBoxItem item in CompilerSelector.Items)
                if (item.Content?.ToString() == prefs.Compiler)
                { CompilerSelector.SelectedItem = item; break; }

            // Std
            foreach (ComboBoxItem item in StdSelector.Items)
                if (item.Content?.ToString() == prefs.Std)
                { StdSelector.SelectedItem = item; break; }

            FlagsInput.Text = prefs.CompilerFlags;
            McaFlagsInput.Text = prefs.McaFlags;
        }

        private void SaveToolbarState()
        {
            var prefs = PreferencesStore.Load();
            prefs.Compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            prefs.Std = (StdSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "c++20";
            prefs.CompilerFlags = FlagsInput.Text.Trim();
            prefs.McaFlags = McaFlagsInput.Text.Trim();
            PreferencesStore.Save(prefs);
        }

        // ── Menu ──────────────────────────────────────────────────────────────

        private void FileMenuButton_Click(object sender, RoutedEventArgs e) =>
            FileMenuPopup.IsOpen = !FileMenuPopup.IsOpen;

        private void HelpButton_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = "https://github.com/placeholder", UseShellExecute = true });

        private void PreferencesMenuButton_Click(object sender, RoutedEventArgs e) =>
            new PreferencesWindow { Owner = this }.ShowDialog();

        // ── Opcode reference panel ───────────────────────────────────────────

        private void OnAsmCaretMoved(object? sender, EventArgs e)
        {
            if (!_opcodePanelOpen) return;
            var line = AsmOutput.Document?.GetLineByNumber(AsmOutput.TextArea.Caret.Line);
            if (line == null) { OpcodeRefPanel.Show(null); return; }
            var text = AsmOutput.Document.GetText(line.Offset, line.Length).TrimStart();
            var mnemonic = ExtractMnemonic(text);
            OpcodeRefPanel.Show(mnemonic);
        }

        private static string? ExtractMnemonic(string asmLine)
        {
            if (string.IsNullOrWhiteSpace(asmLine)) return null;
            // Skip labels (end with ':') and directives
            if (asmLine.TrimEnd().EndsWith(':') || asmLine.StartsWith('.') || asmLine.StartsWith('#') || asmLine.StartsWith(';'))
                return null;
            // First token is the mnemonic — stop at space/tab
            int end = 0;
            while (end < asmLine.Length && asmLine[end] != ' ' && asmLine[end] != '	')
                end++;
            var raw = asmLine[..end].ToUpperInvariant();
            // Strip size suffixes that assemblers sometimes add (MOVQ→MOV etc.)
            // but preserve known mnemonics like MOVAPS, MOVZX, etc.
            return raw;
        }

        private void OpcodeRefToggle_Click(object sender, RoutedEventArgs e)
        {
            _opcodePanelOpen = !_opcodePanelOpen;

            var targetWidth = _opcodePanelOpen ? new GridLength(300) : new GridLength(0);
            var anim = new GridLengthAnimation
            {
                From = OpcodeCol.Width,
                To = targetWidth,
                Duration = new Duration(System.TimeSpan.FromMilliseconds(200)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            OpcodeCol.BeginAnimation(ColumnDefinition.WidthProperty, anim);

            OpcodeRefToggle.Content = _opcodePanelOpen ? "⊟ ref" : "⊞ ref";

            if (_opcodePanelOpen)
                OnAsmCaretMoved(null, EventArgs.Empty);
            else
                OpcodeRefPanel.Show(null);
        }

        // ── Run ───────────────────────────────────────────────────────────────

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _activeSession == null) return;
            if (string.IsNullOrWhiteSpace(SourceEditor.Text)) return;

            _isRunning = true;
            RunButton.IsEnabled = false;
            ShowOutputPanel();

            CompilerOutput.Text = "Compiling...";
            AsmOutput.Text = string.Empty;
            McaOutput.Text = string.Empty;
            _asmHighlighter?.Clear();

            try
            {
                var compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
                var flags = FlagsInput.Text.Trim();
                var std = (StdSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "c++20";

                var result = await CompilerService.CompileAsync(compiler, std, flags, SourceEditor.Text);

                // Store in session
                _activeSession.AsmText = result.AsmOutput;
                _activeSession.CompilerText = result.CompilerOutput;
                _activeSession.McaText = string.Empty;
                _activeSession.AsmMap = result.AsmMap;
                _activeSession.CompilerKind = result.CompilerKind;

                CompilerOutput.Text = result.CompilerOutput;
                AsmOutput.Text = result.AsmOutput;

                if (result.AsmMap.Count > 0)
                    OnSourceCaretMoved(null, EventArgs.Empty);

                SaveToolbarState();
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

        private async void McaButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mcaRunning || _activeSession == null) return;
            _mcaRunning = true;
            McaButton.IsEnabled = false;
            McaOutput.Text = "Running llvm-mca...";

            try
            {
                var result = await CompilerService.RunMcaAsync(
                    _activeSession.AsmText,
                    McaFlagsInput.Text.Trim(),
                    _activeSession.CompilerKind
                );
                _activeSession.McaText = result.Output;
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
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => RootBorder.Margin = GetMaximizedOverhang());
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

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Check for unsaved tabs
            var dirty = _tabs.Where(t => t.IsDirty).ToList();
            if (dirty.Any())
            {
                var dlg = new UnsavedChangesDialog(
                    $"{dirty.Count} unsaved tab(s). Save before closing?", this);
                dlg.ShowDialog();
                var result = dlg.Result;
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes)
                    foreach (var t in dirty) SaveSession(t);
            }
            Close();
        }

        // ── Output panel animation ────────────────────────────────────────────

        private bool _outputVisible = false;

        public void ShowOutputPanel()
        {
            if (_outputVisible) return;
            _outputVisible = true;
            OutputPanel.Visibility = Visibility.Visible;
            OutputSplitter.Visibility = Visibility.Visible;
            var anim = new GridLengthAnimation
            {
                From = new GridLength(0),
                To = new GridLength(220),
                Duration = new Duration(TimeSpan.FromMilliseconds(280)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (s, ev) =>
            {
                OutputRow.BeginAnimation(RowDefinition.HeightProperty, null);
                OutputRow.Height = new GridLength(220);
            };
            OutputRow.BeginAnimation(RowDefinition.HeightProperty, anim);
        }

        public void HideOutputPanel()
        {
            if (!_outputVisible) return;
            double h = OutputRow.ActualHeight;
            var anim = new GridLengthAnimation
            {
                From = new GridLength(h),
                To = new GridLength(0),
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (s, ev) =>
            {
                OutputRow.BeginAnimation(RowDefinition.HeightProperty, null);
                OutputRow.Height = new GridLength(0);
                OutputPanel.Visibility = Visibility.Collapsed;
                OutputSplitter.Visibility = Visibility.Collapsed;
                _outputVisible = false;
            };
            OutputRow.BeginAnimation(RowDefinition.HeightProperty, anim);
        }
    }

    // ── RelayCommand (for keyboard bindings) ──────────────────────────────────

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}