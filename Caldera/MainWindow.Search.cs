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
            _searchHighlighter?.SetSearchTerm(query);
            AsmOutput.TextArea.TextView.Redraw();
        }
    }
}
