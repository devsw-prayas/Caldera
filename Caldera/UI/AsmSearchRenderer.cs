using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace Caldera.UI
{
    public class AsmSearchRenderer : IBackgroundRenderer
    {
        private string _searchTerm = string.Empty;
        private readonly SolidColorBrush _highlightBrush;
        private readonly Pen _borderPen;

        public KnownLayer Layer => KnownLayer.Selection;

        public AsmSearchRenderer()
        {
            _highlightBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0));
            _highlightBrush.Freeze();
            var borderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 0));
            borderBrush.Freeze();
            _borderPen = new Pen(borderBrush, 1);
            _borderPen.Freeze();
        }

        public void SetSearchTerm(string term)
        {
            _searchTerm = term ?? string.Empty;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (string.IsNullOrWhiteSpace(_searchTerm)) return;

            textView.EnsureVisualLines();

            foreach (var line in textView.VisualLines)
            {
                var docLine = line.FirstDocumentLine;
                if (docLine == null) continue;

                var text = textView.Document.GetText(docLine);
                int index = 0;
                while ((index = text.IndexOf(_searchTerm, index, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    var seg = new ICSharpCode.AvalonEdit.Document.TextSegment
                    {
                        StartOffset = docLine.Offset + index,
                        Length = _searchTerm.Length
                    };

                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
                    {
                        drawingContext.DrawRoundedRectangle(_highlightBrush, _borderPen,
                            new System.Windows.Rect(rect.Left, rect.Top, rect.Width, rect.Height), 2, 2);
                    }
                    index += _searchTerm.Length;
                }
            }
        }
    }
}
