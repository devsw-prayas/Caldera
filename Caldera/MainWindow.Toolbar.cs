using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Toolbar state persistence ─────────────────────────────────────────

        private void RestoreToolbarState(PreferencesData prefs)
        {
            foreach (ComboBoxItem item in CompilerSelector.Items)
                if (item.Content?.ToString() == prefs.Compiler)
                { CompilerSelector.SelectedItem = item; break; }

            foreach (ComboBoxItem item in StdSelector.Items)
                if (item.Content?.ToString() == prefs.Std)
                { StdSelector.SelectedItem = item; break; }

            FlagsInput.Text = prefs.CompilerFlags;
            McaFlagsInput.Text = prefs.McaFlags;
        }

        private void SaveToolbarState()
        {
            _prefs.Compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            _prefs.Std = (StdSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "c++20";
            _prefs.CompilerFlags = FlagsInput.Text.Trim();
            _prefs.McaFlags = McaFlagsInput.Text.Trim();
            PreferencesStore.Save(_prefs);
        }

        // ── Compiler / flag picker ────────────────────────────────────────────

        private void RefreshFlagPicker()
        {
            if (FlagGroupsControl is null) return;
            var compiler = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clang++";
            FlagGroupsControl.ItemsSource = FlagPickerData.CompilerFlags.TryGetValue(compiler, out var groups)
                ? groups : new List<FlagGroup>();
        }

        private void CompilerSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshFlagPicker();
            var isMsvc = (CompilerSelector.SelectedItem as ComboBoxItem)?.Content as string == "cl.exe";
            if (McaButton == null) return;
            McaButton.IsEnabled = !isMsvc;
            McaButton.ToolTip = isMsvc ? "llvm-mca is not supported for cl.exe" : null;
        }

        private void FlagPickerButton_Click(object sender, RoutedEventArgs e) =>
            FlagPickerPopup.IsOpen = !FlagPickerPopup.IsOpen;

        private void FlagItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string flag)
            {
                var current = FlagsInput.Text.TrimEnd();
                FlagsInput.Text = string.IsNullOrWhiteSpace(current) ? flag : current + " " + flag;
                FlagsInput.CaretIndex = FlagsInput.Text.Length;
                FlagPickerPopup.IsOpen = false;
            }
        }

        private void McaFlagPickerButton_Click(object sender, RoutedEventArgs e)
        {
            if (McaFlagGroupsControl.ItemsSource is null)
                McaFlagGroupsControl.ItemsSource = FlagPickerData.McaFlags;
            McaFlagPickerPopup.IsOpen = !McaFlagPickerPopup.IsOpen;
        }

        private void McaFlagItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string flag)
            {
                var current = McaFlagsInput.Text.TrimEnd();
                McaFlagsInput.Text = string.IsNullOrWhiteSpace(current) ? flag : current + " " + flag;
                McaFlagsInput.CaretIndex = McaFlagsInput.Text.Length;
                McaFlagPickerPopup.IsOpen = false;
            }
        }
    }
}
