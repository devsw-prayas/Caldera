using System.Text;
using System.Windows.Controls;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── ASM search / filter ───────────────────────────────────────────────

        private void AsmSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = AsmSearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query) || _activeSession == null)
            {
                AsmOutput.Text = _activeSession?.AsmText ?? string.Empty;
                return;
            }

            var lines = _activeSession.AsmText.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var t = line.TrimStart();
                bool isLabel = !line.StartsWith(' ') && !line.StartsWith('\t') && t.EndsWith(':');
                if (isLabel || line.Contains(query, System.StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(line.TrimEnd());
            }
            AsmOutput.Text = sb.ToString().TrimEnd();
        }
    }
}
