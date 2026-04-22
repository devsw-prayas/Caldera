using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace Caldera.UI
{
    public class DiffHighlightRenderer : IBackgroundRenderer
    {
        private Dictionary<int, Core.DiffOp> _diffLines = new();
        
        // Use soft semi-transparent colors for AvalonEdit background
        private readonly SolidColorBrush _insertBrush;
        private readonly SolidColorBrush _deleteBrush;

        public DiffHighlightRenderer()
        {
            _insertBrush = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0));
            _deleteBrush = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0));
            _insertBrush.Freeze();
            _deleteBrush.Freeze();
        }

        public void SetDiffMap(Dictionary<int, Core.DiffOp> diffLines)
        {
            _diffLines = diffLines;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_diffLines == null || _diffLines.Count == 0) return;

            textView.EnsureVisualLines();

            foreach (var line in textView.VisualLines)
            {
                var docLine = line.FirstDocumentLine;
                if (docLine == null) continue;

                if (_diffLines.TryGetValue(docLine.LineNumber, out var op))
                {
                    Brush bg = op == Core.DiffOp.Insert ? _insertBrush : _deleteBrush;
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine))
                    {
                        var fullRect = new System.Windows.Rect(0, rect.Top, textView.ActualWidth, rect.Height);
                        drawingContext.DrawRectangle(bg, null, fullRect);
                    }
                }
            }
        }
    }
}
