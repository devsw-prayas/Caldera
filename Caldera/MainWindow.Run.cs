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

                _activeSession.AsmText = result.AsmOutput;
                _activeSession.RawAsmText = result.RawAsmOutput;
                _activeSession.CompilerText = result.CompilerOutput;
                _activeSession.McaText = string.Empty;
                _activeSession.AsmMap = result.AsmMap;
                _activeSession.CompilerKind = result.CompilerKind;

                CompilerOutput.Text = result.CompilerOutput;

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
