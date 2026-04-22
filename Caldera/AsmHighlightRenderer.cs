using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Caldera
{
    /// <summary>
    /// Paints a highlight band behind asm lines that correspond to the
    /// currently hovered/selected source line.
    /// </summary>
    public class AsmHighlightRenderer : IBackgroundRenderer
    {
        private readonly ICSharpCode.AvalonEdit.TextEditor _editor;
        private List<int> _highlightedLines = new(); // 1-based asm line numbers
        private Brush _brush = Brushes.Transparent;

        public KnownLayer Layer => KnownLayer.Background;

        public AsmHighlightRenderer(ICSharpCode.AvalonEdit.TextEditor editor, Color accentColor)
        {
            _editor = editor;
            UpdateColor(accentColor);
        }

        public void UpdateColor(Color accentColor)
        {
            _brush = new SolidColorBrush(Color.FromArgb(45, accentColor.R, accentColor.G, accentColor.B));
            _brush.Freeze();
        }

        public void SetHighlightedLines(List<int> asmLines)
        {
            _highlightedLines = asmLines ?? new List<int>();
            _editor.TextArea.TextView.Redraw();
        }

        public void Clear()
        {
            if (_highlightedLines.Count == 0) return;
            _highlightedLines = new List<int>();
            _editor.TextArea.TextView.Redraw();
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_highlightedLines.Count == 0) return;

            var doc = _editor.Document;
            if (doc == null) return;

            foreach (int lineNum in _highlightedLines)
            {
                if (lineNum < 1 || lineNum > doc.LineCount) continue;

                var docLine = doc.GetLineByNumber(lineNum);
                var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine);

                foreach (var rect in rects)
                {
                    // Stretch highlight to full editor width
                    var fullRect = new Rect(0, rect.Top, textView.ActualWidth, rect.Height);
                    drawingContext.DrawRectangle(_brush, null, fullRect);
                }
            }
        }
    }
}