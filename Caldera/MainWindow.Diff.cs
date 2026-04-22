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
            var compiler = (System.Windows.Controls.ComboBoxItem)(CompilerSelector.SelectedItem);
            var flags = FlagsInput.Text.Trim();
            _activeSession.PinnedAsmText = _activeSession.AsmText;
            _activeSession.PinnedLabel = $"{compiler.Content} {flags}";
            PinButton.Content = "⊟ unpin";
            PinButton.ToolTip = $"Pinned: {_activeSession.PinnedLabel}";
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

            var baseSet = new HashSet<string>(baseLines.Select(l => l.TrimEnd()), System.StringComparer.Ordinal);
            var newSet = new HashSet<string>(newLines.Select(l => l.TrimEnd()), System.StringComparer.Ordinal);

            foreach (var line in newLines)
            {
                var trimmed = line.TrimEnd();
                if (!baseSet.Contains(trimmed) && !string.IsNullOrWhiteSpace(trimmed))
                    sb.AppendLine("+ " + trimmed);
                else
                    sb.AppendLine("  " + trimmed);
            }

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
    }
}
