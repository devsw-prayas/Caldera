using System;
using System.Windows.Input;
using System.Windows.Media;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── ASM highlight wiring ──────────────────────────────────────────────

        private void InitAsmHighlighter()
        {
            var accent = (Color)System.Windows.Application.Current.Resources["AccentColor"];
            _asmHighlighter = new AsmHighlightRenderer(AsmOutput, accent);
            _diffHighlighter = new UI.DiffHighlightRenderer();
            _searchHighlighter = new UI.AsmSearchRenderer();
            AsmOutput.TextArea.TextView.BackgroundRenderers.Add(_asmHighlighter);
            AsmOutput.TextArea.TextView.BackgroundRenderers.Add(_diffHighlighter);
            AsmOutput.TextArea.TextView.BackgroundRenderers.Add(_searchHighlighter);
            SourceEditor.TextArea.Caret.PositionChanged += OnSourceCaretMoved;
            AsmOutput.TextArea.Caret.PositionChanged += OnAsmCaretMoved;
            AsmOutput.TextArea.MouseDoubleClick += AsmOutput_MouseDoubleClick;
            ThemeEditors();
        }

        private void OnSourceCaretMoved(object? sender, EventArgs e)
        {
            if (_asmHighlighter == null || _activeSession == null) return;
            var map = _activeSession.AsmMap;
            if (map.Count == 0) return;
            int srcLine = SourceEditor.TextArea.Caret.Line;
            if (map.TryGetValue(srcLine, out var asmLines))
                _asmHighlighter.SetHighlightedLines(asmLines);
            else
                _asmHighlighter.Clear();
        }

        // ── Quick jump: double-click ASM line → scroll to source ──────────────

        private void AsmOutput_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_activeSession == null || _activeSession.AsmMap.Count == 0) return;
            var caretLine = AsmOutput.TextArea.Caret.Line;
            foreach (var (srcLine, asmLines) in _activeSession.AsmMap)
            {
                if (asmLines.Contains(caretLine))
                {
                    SourceEditor.Document.GetLineByNumber(
                        Math.Min(srcLine, SourceEditor.Document.LineCount));
                    SourceEditor.ScrollTo(srcLine, 1);
                    SourceEditor.TextArea.Caret.Line = srcLine;
                    SourceEditor.TextArea.Caret.Column = 1;
                    SourceEditor.Focus();
                    return;
                }
            }
        }
    }
}
