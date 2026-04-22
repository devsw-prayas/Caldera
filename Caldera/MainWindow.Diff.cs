using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Pin / diff ────────────────────────────────────────────────────────

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null || string.IsNullOrWhiteSpace(_activeSession.AsmText)) return;
            var compiler = (CompilerSelector.SelectedItem as CompilerInfo)?.Name ?? "clang++";
            var flags = FlagsInput.Text.Trim();
            _activeSession.PinnedAsmText = _activeSession.AsmText;
            _activeSession.PinnedLabel = $"{compiler} {flags}";
            PinButton.Content = "⊟ unpin";
            PinButton.ToolTip = $"Pinned: {_activeSession.PinnedLabel}";
            AsmOutput.Text = _activeSession.AsmText;
            _diffHighlighter?.SetDiffMap(new Dictionary<int, Core.DiffOp>());
            AsmOutput.TextArea.TextView.Redraw();
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
                _diffHighlighter?.SetDiffMap(new Dictionary<int, Core.DiffOp>());
                AsmOutput.TextArea.TextView.Redraw();
            }
            else
            {
                PinButton_Click(sender, e);
            }
        }

        private static (string text, Dictionary<int, Core.DiffOp> map) BuildMyersDiff(string baseAsm, string newAsm, string? baseLabel, string newLabel)
        {
            var baseLines = baseAsm.Split('\n').Select(l => l.TrimEnd()).ToArray();
            var newLines = newAsm.Split('\n').Select(l => l.TrimEnd()).ToArray();

            var diff = Core.MyersDiff.Diff(baseLines, newLines);

            var sb = new StringBuilder();
            sb.AppendLine($"; ── MYERS DIFF  [{baseLabel ?? "pinned"}]  vs  [{newLabel}] ──");
            sb.AppendLine();

            var map = new Dictionary<int, Core.DiffOp>();
            int lineNo = 3;

            foreach (var d in diff)
            {
                if (d.Op == Core.DiffOp.Delete && string.IsNullOrWhiteSpace(d.Text)) 
                    continue;
                
                sb.AppendLine(d.Text);
                if (d.Op != Core.DiffOp.Equal)
                {
                    map[lineNo] = d.Op;
                }
                lineNo++;
            }

            return (sb.ToString(), map);
        }
    }
}
