using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Opcode reference panel ────────────────────────────────────────────

        private void OnAsmCaretMoved(object? sender, EventArgs e)
        {
            if (!_opcodePanelOpen) return;
            var doc = AsmOutput.Document;
            if (doc == null) return;
            var line = doc.GetLineByNumber(AsmOutput.TextArea.Caret.Line);
            if (line == null) { OpcodeRefPanel.Show(null); return; }
            var text = doc.GetText(line.Offset, line.Length).TrimStart();
            var mnemonic = ExtractMnemonic(text);
            OpcodeRefPanel.Show(mnemonic);
        }

        private static string? ExtractMnemonic(string asmLine)
        {
            if (string.IsNullOrWhiteSpace(asmLine)) return null;
            if (asmLine.TrimEnd().EndsWith(':') || asmLine.StartsWith('.') || asmLine.StartsWith('#') || asmLine.StartsWith(';'))
                return null;
            int end = 0;
            while (end < asmLine.Length && asmLine[end] != ' ' && asmLine[end] != '\t')
                end++;
            return asmLine[..end].ToUpperInvariant();
        }

        private void OpcodeRefToggle_Click(object sender, RoutedEventArgs e)
        {
            _opcodePanelOpen = !_opcodePanelOpen;

            var targetWidth = _opcodePanelOpen ? new GridLength(300) : new GridLength(0);
            var anim = new GridLengthAnimation
            {
                From = OpcodeCol.Width,
                To = targetWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            OpcodeCol.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, anim);

            OpcodeRefToggle.Content = _opcodePanelOpen ? "⊟ ref" : "⊞ ref";

            if (_opcodePanelOpen)
                OnAsmCaretMoved(null, EventArgs.Empty);
            else
                OpcodeRefPanel.Show(null);
        }
    }
}
