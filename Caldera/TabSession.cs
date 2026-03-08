using System.Collections.Generic;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace Caldera
{
    /// <summary>
    /// All state that belongs to a single editor tab.
    /// </summary>
    public class TabSession
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string? FilePath { get; set; }        // null = unsaved
        public bool IsDirty { get; set; } = false;

        public string DisplayName =>
            FilePath != null
                ? System.IO.Path.GetFileName(FilePath) + (IsDirty ? " •" : "")
                : "untitled" + (IsDirty ? " •" : "");

        // ── Editor document (shared with AvalonEdit) ─────────────────────────
        public TextDocument Document { get; } = new TextDocument();

        // ── Last compile result ───────────────────────────────────────────────
        public string AsmText { get; set; } = string.Empty;
        public string RawAsmText { get; set; } = string.Empty;
        public string CompilerText { get; set; } = string.Empty;
        public string McaText { get; set; } = string.Empty;

        public Dictionary<int, List<int>> AsmMap { get; set; } = new();
        public AsmMapper.CompilerKind CompilerKind { get; set; } = AsmMapper.CompilerKind.ClangOrGcc;
    }
}