using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Drawing;
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
        // ── DWM P/Invokes ─────────────────────────────────────────────────────
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        // ── Tab state ─────────────────────────────────────────────────────────
        private readonly ObservableCollection<TabSession> _tabs = new();

        private TabSession? _activeSession;

        // ── ASM highlight ─────────────────────────────────────────────────────
        private AsmHighlightRenderer? _asmHighlighter;

        // ── Opcode reference panel ────────────────────────────────────────────
        private bool _opcodePanelOpen = false;

        // ── Persistent preferences (single in-memory instance) ────────────────
        private PreferencesData _prefs = new();

        // ── Run state ─────────────────────────────────────────────────────────
        private bool _isRunning = false;

        private bool _mcaRunning = false;

        // ── Multi-compiler compare state ──────────────────────────────────────
        private bool _compareRunning = false;

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

            // Restore window geometry
            _prefs = PreferencesStore.Load();
            if (!double.IsNaN(_prefs.WindowLeft) && !double.IsNaN(_prefs.WindowTop))
            {
                Left = _prefs.WindowLeft;
                Top = _prefs.WindowTop;
            }
            Width = _prefs.WindowWidth > 400 ? _prefs.WindowWidth : 1400;
            Height = _prefs.WindowHeight > 300 ? _prefs.WindowHeight : 800;
            if (_prefs.WindowMaximized)
                WindowState = WindowState.Maximized;

            Closing += (s, _) => SaveWindowGeometry();

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                InitAsmHighlighter();
                RefreshFlagPicker();

                // Restore toolbar state from prefs
                RestoreToolbarState(_prefs);

                // Restore panel split ratio
                if (prefs.EditorSplitRatio > 0.1 && prefs.EditorSplitRatio < 0.9)
                {
                    var totalWidth = EditorAreaGrid.ActualWidth;
                    if (totalWidth > 0)
                    {
                        EditorAreaGrid.ColumnDefinitions[0].Width = new GridLength(prefs.EditorSplitRatio, GridUnitType.Star);
                        EditorAreaGrid.ColumnDefinitions[2].Width = new GridLength(1.0 - prefs.EditorSplitRatio, GridUnitType.Star);
                    }
                }

                // Create initial tab
                NewTab();
            });
        }

        private void SaveWindowGeometry()
        {
            _prefs.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _prefs.WindowLeft = Left;
                _prefs.WindowTop = Top;
                _prefs.WindowWidth = Width;
                _prefs.WindowHeight = Height;
            }
            // Save editor split ratio
            var total = EditorAreaGrid.ColumnDefinitions[0].ActualWidth +
                        EditorAreaGrid.ColumnDefinitions[2].ActualWidth;
            if (total > 0)
                _prefs.EditorSplitRatio = EditorAreaGrid.ColumnDefinitions[0].ActualWidth / total;
            // Save output panel height
            if (_outputVisible)
                _prefs.OutputPanelHeight = OutputRow.Height.Value;
            PreferencesStore.Save(_prefs);
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

            // Restore pin button state
            if (PinButton != null)
            {
                if (session.PinnedAsmText != null)
                { PinButton.Content = "⊟ unpin"; PinButton.ToolTip = $"Pinned: {session.PinnedLabel}"; }
                else
                { PinButton.Content = "⊞ pin"; PinButton.ToolTip = "Pin current ASM as baseline for diff"; }
            }

            // Restore stats
            UpdateAsmStats(session.AsmText);

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
            _activeSession.RawAsmText = string.Empty;
            _activeSession.CompilerText = string.Empty;
            _activeSession.McaText = string.Empty;
            _activeSession.PinnedAsmText = null;
            _activeSession.PinnedLabel = null;
            _activeSession.AsmMap = new();
            AsmOutput.Text = string.Empty;
            CompilerOutput.Text = string.Empty;
            McaOutput.Text = string.Empty;
            if (AsmStatsLabel != null) AsmStatsLabel.Text = string.Empty;
            if (PinButton != null) { PinButton.Content = "⊞ pin"; PinButton.ToolTip = null; }
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
            // Double-click ASM line → jump to source
            AsmOutput.TextArea.MouseDoubleClick += AsmOutput_MouseDoubleClick;
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

        private void CompilerSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshFlagPicker();
            var isMsvc = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content as string == "cl.exe";
            if (McaButton == null) return;
            McaButton.IsEnabled = !isMsvc;
            McaButton.ToolTip = isMsvc ? "llvm-mca is not supported for cl.exe" : null;
        }

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
            _prefs.Compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            _prefs.Std = (StdSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "c++20";
            _prefs.CompilerFlags = FlagsInput.Text.Trim();
            _prefs.McaFlags = McaFlagsInput.Text.Trim();
            PreferencesStore.Save(_prefs);
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
                _activeSession.RawAsmText = result.RawAsmOutput;
                _activeSession.CompilerText = result.CompilerOutput;
                _activeSession.McaText = string.Empty;
                _activeSession.AsmMap = result.AsmMap;
                _activeSession.CompilerKind = result.CompilerKind;

                CompilerOutput.Text = result.CompilerOutput;

                // Show diff or plain ASM
                if (_activeSession.PinnedAsmText != null)
                    AsmOutput.Text = BuildDiff(_activeSession.PinnedAsmText, result.AsmOutput,
                                               _activeSession.PinnedLabel, $"{compiler} {flags}");
                else
                    AsmOutput.Text = result.AsmOutput;

                UpdateAsmStats(result.AsmOutput);

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
                    _activeSession.RawAsmText,
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
        // ── ASM stats (instruction count, line count) ─────────────────────────

        private void UpdateAsmStats(string asmText)
        {
            if (AsmStatsLabel == null) return;
            if (string.IsNullOrWhiteSpace(asmText))
            {
                AsmStatsLabel.Text = string.Empty;
                return;
            }
            var lines = asmText.Split('\n');
            int instrCount = 0;
            int funcCount = 0;
            foreach (var line in lines)
            {
                var t = line.TrimStart();
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith(';') || t.StartsWith('#') || t.StartsWith('.'))
                    continue;
                // Function label: non-indented, ends with ':'
                if (!line.StartsWith(' ') && !line.StartsWith('\t') && t.EndsWith(':'))
                    funcCount++;
                else
                    instrCount++;
            }
            AsmStatsLabel.Text = $"{instrCount} instr  ·  {funcCount} fn";
        }

        // ── Pin / diff ────────────────────────────────────────────────────────

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null || string.IsNullOrWhiteSpace(_activeSession.AsmText)) return;
            var compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            var flags = FlagsInput.Text.Trim();
            _activeSession.PinnedAsmText = _activeSession.AsmText;
            _activeSession.PinnedLabel = $"{compiler} {flags}";
            PinButton.Content = "⊟ unpin";
            PinButton.ToolTip = $"Pinned: {_activeSession.PinnedLabel}";
            // Refresh display to show diff against current
            AsmOutput.Text = _activeSession.AsmText;
        }

        private void UnpinOrPin_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null) return;
            if (_activeSession.PinnedAsmText != null)
            {
                _activeSession.PinnedAsmText = null;
                _activeSession.PinnedLabel = null;
                PinButton.Content = "⊞ pin";
                PinButton.ToolTip = "Pin current ASM as baseline for diff";
                AsmOutput.Text = _activeSession.AsmText;
            }
            else
            {
                PinButton_Click(sender, e);
            }
        }

        private static string BuildDiff(string baseAsm, string newAsm, string? baseLabel, string newLabel)
        {
            var baseLines = baseAsm.Split('\n');
            var newLines = newAsm.Split('\n');

            var sb = new StringBuilder();
            sb.AppendLine($"; ── DIFF  [{baseLabel ?? "pinned"}]  vs  [{newLabel}] ──");
            sb.AppendLine();

            // Simple line-by-line diff: find removed and added lines
            var baseSet = new HashSet<string>(baseLines.Select(l => l.TrimEnd()), StringComparer.Ordinal);
            var newSet = new HashSet<string>(newLines.Select(l => l.TrimEnd()), StringComparer.Ordinal);

            // Output new ASM annotated with +/- markers
            foreach (var line in newLines)
            {
                var trimmed = line.TrimEnd();
                if (!baseSet.Contains(trimmed) && !string.IsNullOrWhiteSpace(trimmed))
                    sb.AppendLine("+ " + trimmed);
                else
                    sb.AppendLine("  " + trimmed);
            }

            // Lines only in base (removed)
            sb.AppendLine();
            sb.AppendLine("; ── removed from baseline ──");
            bool anyRemoved = false;
            foreach (var line in baseLines)
            {
                var trimmed = line.TrimEnd();
                if (!newSet.Contains(trimmed) && !string.IsNullOrWhiteSpace(trimmed))
                {
                    sb.AppendLine("- " + trimmed);
                    anyRemoved = true;
                }
            }
            if (!anyRemoved)
                sb.AppendLine("; (none)");

            return sb.ToString().TrimEnd();
        }

        // ── Quick jump: click ASM line → scroll source to that line ──────────

        private void AsmOutput_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_activeSession == null || _activeSession.AsmMap.Count == 0) return;
            var caretLine = AsmOutput.TextArea.Caret.Line;
            // Find the source line that maps to this ASM line
            foreach (var (srcLine, asmLines) in _activeSession.AsmMap)
            {
                if (asmLines.Contains(caretLine))
                {
                    var line = SourceEditor.Document.GetLineByNumber(
                        Math.Min(srcLine, SourceEditor.Document.LineCount));
                    SourceEditor.ScrollTo(srcLine, 1);
                    SourceEditor.TextArea.Caret.Line = srcLine;
                    SourceEditor.TextArea.Caret.Column = 1;
                    SourceEditor.Focus();
                    return;
                }
            }
        }

        // ── Flag presets ──────────────────────────────────────────────────────

        private void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var flags = FlagsInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(flags)) return;
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Preset name:", "Save Flag Preset", flags[..Math.Min(20, flags.Length)]);
            if (string.IsNullOrWhiteSpace(name)) return;
            _prefs.CompilerFlagPresets[name] = flags;
            PreferencesStore.Save(_prefs);
            RefreshPresetPicker();
        }

        private void SaveMcaPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var flags = McaFlagsInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(flags)) return;
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Preset name:", "Save MCA Preset", flags[..Math.Min(20, flags.Length)]);
            if (string.IsNullOrWhiteSpace(name)) return;
            _prefs.McaFlagPresets[name] = flags;
            PreferencesStore.Save(_prefs);
            RefreshMcaPresetPicker();
        }

        private void RefreshPresetPicker()
        {
            PresetPickerList.ItemsSource = _prefs.CompilerFlagPresets
                .Select(kv => new FlagItem { Flag = kv.Key, Description = kv.Value })
                .ToList();
        }

        private void RefreshMcaPresetPicker()
        {
            McaPresetPickerList.ItemsSource = _prefs.McaFlagPresets
                .Select(kv => new FlagItem { Flag = kv.Key, Description = kv.Value })
                .ToList();
        }

        private void PresetItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string flags)
            {
                FlagsInput.Text = flags;
                PresetPickerPopup.IsOpen = false;
            }
        }

        private void McaPresetItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string flags)
            {
                McaFlagsInput.Text = flags;
                McaPresetPickerPopup.IsOpen = false;
            }
        }

        private void PresetPickerButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPresetPicker();
            PresetPickerPopup.IsOpen = !PresetPickerPopup.IsOpen;
        }

        private void McaPresetPickerButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMcaPresetPicker();
            McaPresetPickerPopup.IsOpen = !McaPresetPickerPopup.IsOpen;
        }

        // ── Multi-compiler compare ────────────────────────────────────────────

        private async void CompareButton_Click(object sender, RoutedEventArgs e)
        {
            if (_compareRunning || _activeSession == null) return;
            if (string.IsNullOrWhiteSpace(SourceEditor.Text)) return;

            _compareRunning = true;
            CompareButton.IsEnabled = false;
            ShowOutputPanel();
            CompilerOutput.Text = "Running all compilers...";
            AsmOutput.Text = string.Empty;

            try
            {
                var std = (StdSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "c++20";
                var flags = FlagsInput.Text.Trim();
                var src = SourceEditor.Text;

                var compilers = new[] { "clang++", "g++", "cl.exe" };
                var tasks = compilers.Select(c =>
                    CompilerService.CompileAsync(c, std, flags, src)).ToArray();

                var results = await Task.WhenAll(tasks);

                var sb = new StringBuilder();
                var compSb = new StringBuilder();

                for (int i = 0; i < compilers.Length; i++)
                {
                    var r = results[i];
                    sb.AppendLine($"; ══════════════════════════════════════");
                    sb.AppendLine($"; {compilers[i]}  {flags}");
                    sb.AppendLine($"; ══════════════════════════════════════");
                    sb.AppendLine(r.Success ? r.AsmOutput : $"; FAILED — see compiler output");
                    sb.AppendLine();
                    compSb.AppendLine($"── {compilers[i]} ──");
                    compSb.AppendLine(r.CompilerOutput);
                    compSb.AppendLine();
                }

                AsmOutput.Text = sb.ToString().TrimEnd();
                CompilerOutput.Text = compSb.ToString().TrimEnd();
                UpdateAsmStats(AsmOutput.Text);
            }
            catch (Exception ex)
            {
                CompilerOutput.Text = $"Error during compare:\n{ex.Message}";
            }
            finally
            {
                _compareRunning = false;
                CompareButton.IsEnabled = true;
            }
        }

        // ── Inline MCA annotations ────────────────────────────────────────────

        private async void InlineMcaButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null || string.IsNullOrWhiteSpace(_activeSession.RawAsmText)) return;

            InlineMcaButton.IsEnabled = false;
            InlineMcaButton.Content = "⏳ annotating";

            try
            {
                var mcaResult = await CompilerService.RunMcaAsync(
                    _activeSession.RawAsmText,
                    McaFlagsInput.Text.Trim() + " --instruction-info",
                    _activeSession.CompilerKind);

                if (!mcaResult.Success)
                {
                    CompilerOutput.Text = "llvm-mca failed:\n" + mcaResult.Output;
                    return;
                }

                AsmOutput.Text = InjectMcaAnnotations(_activeSession.AsmText, mcaResult.Output);
            }
            catch (Exception ex)
            {
                CompilerOutput.Text = $"Inline MCA error:\n{ex.Message}";
            }
            finally
            {
                InlineMcaButton.IsEnabled = true;
                InlineMcaButton.Content = "⚡ annotate";
            }
        }

        private static string InjectMcaAnnotations(string displayAsm, string mcaOutput)
        {
            // Parse the llvm-mca instruction-info table:
            // Columns: [#]  [Latency]  [RThroughput]  [NumMicroOpcodes]  [Mnemonic]
            // We match lines like:  [1]    5           1.00         1           vmovdqu
            var rowRx = new System.Text.RegularExpressions.Regex(
                @"^\s*\[(\d+)\]\s+(\d+)\s+([\d.]+)\s+\d+\s+(\w+)",
                System.Text.RegularExpressions.RegexOptions.Compiled |
                System.Text.RegularExpressions.RegexOptions.Multiline);

            // Build mnemonic → "latency/rtput" annotation (last win if duplicate)
            var annotations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in rowRx.Matches(mcaOutput))
            {
                var mnemonic = m.Groups[4].Value;
                var latency = m.Groups[2].Value;
                var rtput = m.Groups[3].Value;
                annotations[mnemonic] = $"lat:{latency} rtp:{rtput}";
            }

            if (annotations.Count == 0) return displayAsm;

            var mnemonicRx = new System.Text.RegularExpressions.Regex(@"^\s+(\w+)");
            var sb = new StringBuilder();
            foreach (var line in displayAsm.Split('\n'))
            {
                var m = mnemonicRx.Match(line);
                if (m.Success && annotations.TryGetValue(m.Groups[1].Value, out var ann))
                    sb.AppendLine(line.TrimEnd().PadRight(48) + "  ; " + ann);
                else
                    sb.AppendLine(line.TrimEnd());
            }
            return sb.ToString().TrimEnd();
        }

        // ── ASM search / filter ───────────────────────────────────────────────

        private void AsmSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = AsmSearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query) || _activeSession == null)
            {
                AsmOutput.Text = _activeSession?.AsmText ?? string.Empty;
                return;
            }

            // Filter: keep matching lines and surrounding context (label + instructions)
            var lines = _activeSession.AsmText.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Always keep labels
                var t = line.TrimStart();
                bool isLabel = !line.StartsWith(' ') && !line.StartsWith('\t') && t.EndsWith(':');
                if (isLabel || line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(line.TrimEnd());
            }
            AsmOutput.Text = sb.ToString().TrimEnd();
        }

    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public RelayCommand(Action<object?> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}