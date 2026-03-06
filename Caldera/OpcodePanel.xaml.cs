using System.Windows;
using System.Windows.Controls;

namespace Caldera
{
    public partial class OpcodePanel : UserControl
    {
        private bool _expanded = false;

        public OpcodePanel()
        {
            InitializeComponent();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Display info for the given mnemonic. Pass null to show the empty state.
        /// </summary>
        public void Show(string? mnemonic)
        {
            var info = mnemonic != null ? OpcodeDb.Lookup(mnemonic) : null;

            if (info == null)
            {
                HeaderMnemonic.Text = string.Empty;
                HeaderCategory.Text = string.Empty;
                EmptyState.Visibility = Visibility.Visible;
                CompactView.Visibility = Visibility.Collapsed;
                ExpandedView.Visibility = Visibility.Collapsed;
                _expanded = false;
                return;
            }

            // Header
            HeaderMnemonic.Text = info.Mnemonic;
            HeaderCategory.Text = info.Category;

            // Compact fields
            SummaryText.Text = info.Summary;
            FlagsReadText.Text = string.IsNullOrWhiteSpace(info.FlagsRead) ? "-" : info.FlagsRead;
            FlagsWriteText.Text = string.IsNullOrWhiteSpace(info.FlagsWritten) ? "-" : info.FlagsWritten;
            LatencyHintText.Text = string.IsNullOrWhiteSpace(info.RepLatency) ? "-" : info.RepLatency;
            LatencyUarchText.Text = info.RepUarch;

            // Expanded fields
            DescriptionText.Text = info.Description;
            FormsControl.ItemsSource = info.Forms;
            LatencyControl.ItemsSource = info.Latencies;
            ExceptionText.Text = string.IsNullOrWhiteSpace(info.ExceptionClass) ? "-" : info.ExceptionClass;

            EmptyState.Visibility = Visibility.Collapsed;

            if (_expanded)
            {
                CompactView.Visibility = Visibility.Collapsed;
                ExpandedView.Visibility = Visibility.Visible;
            }
            else
            {
                CompactView.Visibility = Visibility.Visible;
                ExpandedView.Visibility = Visibility.Collapsed;
            }
        }

        // ── Expand / collapse ─────────────────────────────────────────────────

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _expanded = true;
            CompactView.Visibility = Visibility.Collapsed;
            ExpandedView.Visibility = Visibility.Visible;
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            _expanded = false;
            ExpandedView.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
        }
    }
}