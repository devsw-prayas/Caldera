using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Run (compile) ─────────────────────────────────────────────────────

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            _compileCts?.Cancel();
            _compileCts = new System.Threading.CancellationTokenSource();
            var ct = _compileCts.Token;

            if (_activeSession == null || string.IsNullOrWhiteSpace(SourceEditor.Text)) return;

            // Sync UI state to session before compiling
            SaveToolbarState();

            RunButton.IsEnabled = false; // Disable to prevent double-clicks
            ShowOutputPanel();

            CompilerOutput.Text = "Compiling...";
            AsmOutput.Text = string.Empty;
            SetMcaDisplay(string.Empty);
            _asmHighlighter?.Clear();

            try
            {
                var compiler = _activeSession.Compiler;
                var flags = _activeSession.Flags;
                var std = _activeSession.Std;

                var result = await CompilerService.CompileAsync(compiler, std, flags, SourceEditor.Text, ct);

                if (ct.IsCancellationRequested) return;

                _activeSession.AsmText = result.AsmOutput;
                _activeSession.RawAsmText = result.RawAsmOutput;
                _activeSession.CompilerText = result.CompilerOutput;
                _activeSession.McaText = string.Empty;
                _activeSession.AsmMap = result.AsmMap;
                _activeSession.CompilerKind = result.CompilerKind;

                CompilerOutput.Text = result.CompilerOutput;

                if (_activeSession.PinnedAsmText != null)
                {
                    var (diffText, diffMap) = BuildMyersDiff(_activeSession.PinnedAsmText, result.AsmOutput,
                                                             _activeSession.PinnedLabel, $"{compiler} {flags}");
                    AsmOutput.Text = diffText;
                    _diffHighlighter?.SetDiffMap(diffMap);
                }
                else
                {
                    AsmOutput.Text = result.AsmOutput;
                    _diffHighlighter?.SetDiffMap(new System.Collections.Generic.Dictionary<int, Core.DiffOp>());
                }
                AsmOutput.TextArea.TextView.Redraw();

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
                if (!ct.IsCancellationRequested)
                {
                    RunButton.IsEnabled = true;
                }
            }
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
            SetMcaDisplay(string.Empty);
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

            var godboltCompiler = compiler switch
            {
                "clang++" => "clang_trunk",
                "g++"     => "gsnapshot",
                "cl.exe"  => "vcpp_v19_latest_x64",
                _         => "clang_trunk"
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
    }
}
