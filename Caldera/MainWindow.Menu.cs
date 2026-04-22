using System.Windows;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Menu ──────────────────────────────────────────────────────────────

        private void FileMenuButton_Click(object sender, RoutedEventArgs e) =>
            FileMenuPopup.IsOpen = !FileMenuPopup.IsOpen;

        private void HelpButton_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = "https://github.com/placeholder", UseShellExecute = true });

        private void PreferencesMenuButton_Click(object sender, RoutedEventArgs e) =>
            new PreferencesWindow(_prefs) { Owner = this }.ShowDialog();
    }
}
