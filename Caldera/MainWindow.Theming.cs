using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Theming ───────────────────────────────────────────────────────────

        private void ThemeEditors()
        {
            var accent = (Color)Application.Current.Resources["AccentColor"];
            SetLineNumberColor(SourceEditor, accent);
            SetLineNumberColor(AsmOutput, accent);
            ApplyCppTheme();
            ApplyAsmTheme();
        }

        private static void SetLineNumberColor(TextEditor editor, Color accent)
        {
            var margin = editor.TextArea.LeftMargins
                .OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                .FirstOrDefault();
            if (margin != null)
                margin.SetValue(Control.ForegroundProperty,
                    new SolidColorBrush(accent) { Opacity = 0.35 });
        }

        private static IHighlightingDefinition LoadThemeHighlighting(string themeName)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var file = System.IO.Path.Combine(exeDir, "Highlighting", $"Cpp-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                file = System.IO.Path.Combine("Highlighting", $"Cpp-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                return HighlightingManager.Instance.GetDefinition("C++")!;
            using var reader = new XmlTextReader(file);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        private static IHighlightingDefinition LoadAsmHighlighting(string themeName)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var file = System.IO.Path.Combine(exeDir, "Highlighting", $"Asm-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                file = System.IO.Path.Combine("Highlighting", $"Asm-{themeName}.xshd");
            if (!System.IO.File.Exists(file))
                return HighlightingManager.Instance.GetDefinition("C++")!;
            using var reader = new XmlTextReader(file);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        private void ApplyCppTheme() =>
            SourceEditor.SyntaxHighlighting = LoadThemeHighlighting(ThemeManager.CurrentTheme);

        private void ApplyAsmTheme() =>
            AsmOutput.SyntaxHighlighting = LoadAsmHighlighting(ThemeManager.CurrentTheme);

        private void OnThemeChanged()
        {
            var res = Application.Current.Resources;
            GradStop0.Color = (Color)res["BgLight"];
            GradStop1.Color = (Color)res["BgMid"];
            GradStop2.Color = (Color)res["BgDark"];
            TitleGlow.Color = (Color)res["AccentColor"];
            Background = new SolidColorBrush((Color)res["BgDark"]);
            ApplyCppTheme();
            ApplyAsmTheme();
            var accent = (Color)res["AccentColor"];
            var lineNumBrush = new SolidColorBrush(accent) { Opacity = 0.35 };
            SourceEditor.TextArea.LeftMargins.OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                .FirstOrDefault()?.SetValue(Control.ForegroundProperty, lineNumBrush);
            AsmOutput.TextArea.LeftMargins.OfType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>()
                .FirstOrDefault()?.SetValue(Control.ForegroundProperty, lineNumBrush);
            _asmHighlighter?.UpdateColor(accent);
        }

        private void OnOutputFontSizeChanged(double size)
        {
            AsmOutput.FontSize = size;
            AsmOutput.TextArea.FontSize = size;
            AsmOutput.TextArea.TextView.Redraw();
            CompilerOutput.FontSize = size;
            McaOutputRaw.FontSize = size;
            McaSummaryText.FontSize = size;
            McaDataGrid.FontSize = size;
        }

        private void OnFontChanged(string family, double size)
        {
            SourceEditor.FontFamily = new FontFamily(family);
            SourceEditor.FontSize = size;
        }
    }
}
