using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Multi-compiler compare ─────────────────────────────────────────────

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

                var results = await System.Threading.Tasks.Task.WhenAll(tasks);

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
    }
}
