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
            // 1. Extract all instruction info using the existing proven parser
            var parsed = Core.McaParser.Parse(mcaOutput);
            var infoList = parsed.Instructions;

            if (infoList.Count == 0) return displayAsm;

            // 2. Map them sequentially to the display assembly
            // We skip lines that are labels (ending in :) or directives (starting with .)
            var instrRx = new Regex(@"^\s+([a-zA-Z]\w*)", RegexOptions.Compiled);
            var sb = new StringBuilder();
            int infoIdx = 0;

            foreach (var line in displayAsm.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    sb.AppendLine(line);
                    continue;
                }

                // Match instructions (lines starting with whitespace and a word, not a label or directive)
                var m = instrRx.Match(line);
                if (m.Success && !trimmed.EndsWith(":") && !trimmed.StartsWith(".") && infoIdx < infoList.Count)
                {
                    var info = infoList[infoIdx++];
                    sb.AppendLine(line.TrimEnd().PadRight(48) + $"  ; lat:{info.Latency} rtp:{info.RThroughput}");
                }
                else
                {
                    sb.AppendLine(line.TrimEnd());
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}

