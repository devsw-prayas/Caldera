using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Flag presets ──────────────────────────────────────────────────────

        private void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var flags = FlagsInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(flags)) return;
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Preset name:", "Save Flag Preset", flags[..System.Math.Min(20, flags.Length)]);
            if (string.IsNullOrWhiteSpace(name)) return;
            _prefs.CompilerFlagPresets[name] = flags;
            PreferencesStore.Save(_prefs);
            RefreshPresetPicker();
        }

        private void SaveMcaPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var flags = McaFlagsInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(flags)) return;
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Preset name:", "Save MCA Preset", flags[..System.Math.Min(20, flags.Length)]);
            if (string.IsNullOrWhiteSpace(name)) return;
            _prefs.McaFlagPresets[name] = flags;
            PreferencesStore.Save(_prefs);
            RefreshMcaPresetPicker();
        }

        private void RefreshPresetPicker()
        {
            PresetPickerList.ItemsSource = _prefs.CompilerFlagPresets
                .Select(kv => new FlagItem { Flag = kv.Key, Description = kv.Value })
                .ToList();
        }

        private void RefreshMcaPresetPicker()
        {
            McaPresetPickerList.ItemsSource = _prefs.McaFlagPresets
                .Select(kv => new FlagItem { Flag = kv.Key, Description = kv.Value })
                .ToList();
        }

        private void PresetItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border b && b.Tag is string flags)
            {
                FlagsInput.Text = flags;
                PresetPickerPopup.IsOpen = false;
            }
        }

        private void McaPresetItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border b && b.Tag is string flags)
            {
                McaFlagsInput.Text = flags;
                McaPresetPickerPopup.IsOpen = false;
            }
        }

        private void PresetPickerButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPresetPicker();
            PresetPickerPopup.IsOpen = !PresetPickerPopup.IsOpen;
        }

        private void McaPresetPickerButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMcaPresetPicker();
            McaPresetPickerPopup.IsOpen = !McaPresetPickerPopup.IsOpen;
        }
    }
}
