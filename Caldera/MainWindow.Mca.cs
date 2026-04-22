using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── llvm-mca ──────────────────────────────────────────────────────────

        private void SetMcaDisplay(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                McaOutputGrid.Visibility = Visibility.Collapsed;
                McaOutputRaw.Visibility = Visibility.Visible;
                McaOutputRaw.Text = string.Empty;
                return;
            }

            var parsed = Core.McaParser.Parse(text);
            if (parsed.Instructions.Count > 0)
            {
                McaOutputRaw.Visibility = Visibility.Collapsed;
                McaOutputGrid.Visibility = Visibility.Visible;
                McaSummaryText.Text = parsed.Summary;
                McaDataGrid.ItemsSource = parsed.Instructions;
            }
            else
            {
                McaOutputGrid.Visibility = Visibility.Collapsed;
                McaOutputRaw.Visibility = Visibility.Visible;
                McaOutputRaw.Text = text;
            }
        }

        private async void McaButton_Click(object sender, RoutedEventArgs e)
        {
            _mcaCts?.Cancel();
            _mcaCts = new System.Threading.CancellationTokenSource();
            var ct = _mcaCts.Token;

            if (_activeSession == null) return;
            SetMcaDisplay("Running llvm-mca...");

            try
            {
                var result = await McaService.RunMcaAsync(
                    _activeSession.RawAsmText,
                    _activeSession.McaFlags,
                    _activeSession.CompilerKind, ct);
                    
                if (ct.IsCancellationRequested) return;
                
                _activeSession.McaText = result.Output;
                SetMcaDisplay(result.Output);
            }
            catch (Exception ex)
            {
                SetMcaDisplay($"Error launching llvm-mca:\n{ex.Message}");
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                }
            }
        }

        // ── Inline MCA annotations ────────────────────────────────────────────

        private async void InlineMcaButton_Click(object sender, RoutedEventArgs e)
        {
            _mcaCts?.Cancel();
            _mcaCts = new System.Threading.CancellationTokenSource();
            var ct = _mcaCts.Token;

            if (_activeSession == null || string.IsNullOrWhiteSpace(_activeSession.RawAsmText)) return;

            InlineMcaButton.Content = "⏳ annotating";

            try
            {
                var mcaResult = await McaService.RunMcaAsync(
                    _activeSession.RawAsmText,
                    _activeSession.McaFlags + " --instruction-info",
                    _activeSession.CompilerKind, ct);

                if (ct.IsCancellationRequested) return;

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
                if (!ct.IsCancellationRequested)
                {
                    InlineMcaButton.Content = "⚡ annotate";
                }
            }
        }

        private static string InjectMcaAnnotations(string displayAsm, string mcaOutput)
        {
            // Parse the llvm-mca instruction-info table:
            // Columns: [#]  [Latency]  [RThroughput]  [NumMicroOpcodes]  [Mnemonic]
            var rowRx = new Regex(
                @"^\s*\[(\d+)\]\s+(\d+)\s+([\d.]+)\s+\d+\s+(\w+)",
                RegexOptions.Compiled | RegexOptions.Multiline);

            var annotations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in rowRx.Matches(mcaOutput))
            {
                var mnemonic = m.Groups[4].Value;
                var latency = m.Groups[2].Value;
                var rtput = m.Groups[3].Value;
                annotations[mnemonic] = $"lat:{latency} rtp:{rtput}";
            }

            if (annotations.Count == 0) return displayAsm;

            var mnemonicRx = new Regex(@"^\s+(\w+)");
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
    }
}
