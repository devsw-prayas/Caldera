using System.Net;
using System.Text;

namespace Caldera.Core
{
    public static class ExportService
    {
        public static string GenerateMarkdown(TabSession session)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Caldera Session Export");
            sb.AppendLine();
            sb.AppendLine($"**Compiler:** `{session.Compiler}`  ");
            sb.AppendLine($"**Standard:** `{session.Std}`  ");
            sb.AppendLine($"**Flags:** `{session.Flags}`");
            sb.AppendLine();
            
            if (!string.IsNullOrWhiteSpace(session.Document.Text))
            {
                sb.AppendLine("### Source (`C++`)");
                sb.AppendLine("```cpp");
                sb.AppendLine(session.Document.Text.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(session.AsmText))
            {
                sb.AppendLine("### Assembly Output");
                sb.AppendLine("```asm");
                sb.AppendLine(session.AsmText.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(session.McaText))
            {
                sb.AppendLine("### llvm-mca Analysis");
                sb.AppendLine("```text");
                sb.AppendLine(session.McaText.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        public static string GenerateHtml(TabSession session)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <title>Caldera Export</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: -apple-system, system-ui, sans-serif; background: #0d0d0d; color: #d4d4d4; padding: 2rem; max-width: 1200px; margin: auto; }");
            sb.AppendLine("        h1, h3 { color: #ffffff; }");
            sb.AppendLine("        .badge { background: #2d2d2d; padding: 4px 8px; border-radius: 4px; font-family: monospace; }");
            sb.AppendLine("        pre { background: #000000; padding: 1rem; border-radius: 8px; overflow-x: auto; border: 1px solid #333; }");
            sb.AppendLine("        code { font-family: 'Consolas', 'Courier New', monospace; font-size: 14px; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("    <h1>Caldera Session Export</h1>");
            sb.AppendLine($"    <p><strong>Compiler:</strong> <span class=\"badge\">{WebUtility.HtmlEncode(session.Compiler)}</span> | <strong>Standard:</strong> <span class=\"badge\">{WebUtility.HtmlEncode(session.Std)}</span> | <strong>Flags:</strong> <span class=\"badge\">{WebUtility.HtmlEncode(session.Flags)}</span></p>");
            
            if (!string.IsNullOrWhiteSpace(session.Document.Text))
            {
                sb.AppendLine("    <h3>Source (C++)</h3>");
                sb.AppendLine($"    <pre><code class=\"language-cpp\">{WebUtility.HtmlEncode(session.Document.Text.TrimEnd())}</code></pre>");
            }

            if (!string.IsNullOrWhiteSpace(session.AsmText))
            {
                sb.AppendLine("    <h3>Assembly Output</h3>");
                sb.AppendLine($"    <pre><code class=\"language-asm\">{WebUtility.HtmlEncode(session.AsmText.TrimEnd())}</code></pre>");
            }

            if (!string.IsNullOrWhiteSpace(session.McaText))
            {
                sb.AppendLine("    <h3>llvm-mca Analysis</h3>");
                sb.AppendLine($"    <pre><code class=\"language-text\">{WebUtility.HtmlEncode(session.McaText.TrimEnd())}</code></pre>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }
    }
}
