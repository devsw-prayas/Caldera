using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Caldera
{
    public static class AsmMapper
    {
        private static readonly Regex GccLineTag =
            new(@"#\s+\S+:(\d+):", RegexOptions.Compiled);

        private static readonly Regex MsvcLineTag =
            new(@"^\s*;\s+Line\s+(\d+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex QuotedLabelLine =
            new(@"^""([^""]+)""\s*:", RegexOptions.Compiled);

        private static readonly Regex PlainLabelLine =
            new(@"^([A-Za-z_?][A-Za-z0-9_?@$]*)\s*:", RegexOptions.Compiled);

        public enum CompilerKind { ClangOrGcc, Msvc, Nvcc }

        public static Dictionary<int, List<int>> Parse(
            string asmText,
            CompilerKind kind,
            string? sourceText = null,
            string? displayAsm = null)
        {
            var map = new Dictionary<int, List<int>>();
            if (string.IsNullOrEmpty(asmText)) return map;

            if (kind == CompilerKind.Msvc)
                ParseMsvc(asmText, map);
            else if (kind == CompilerKind.Nvcc)
                ParseNvcc(asmText, map);
            else
                ParseGcc(asmText, map, sourceText, displayAsm);

            return map;
        }

        // ── MSVC ─────────────────────────────────────────────────────────────

        private static void ParseMsvc(string asmText, Dictionary<int, List<int>> map)
        {
            var lines = asmText.Split('\n');
            int currentSourceLine = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                int asmLine = i + 1;
                var line = lines[i];

                var m = MsvcLineTag.Match(line);
                if (m.Success)
                {
                    currentSourceLine = int.Parse(m.Groups[1].Value);
                    continue;
                }

                if (currentSourceLine > 0 && !string.IsNullOrWhiteSpace(line))
                {
                    var t = line.TrimStart();
                    if (t.StartsWith(';') ||
                        Regex.IsMatch(t, @"\bPROC\b|\bENDP\b|SEGMENT|ENDS|^\.",
                            RegexOptions.IgnoreCase))
                        continue;

                    if (!map.TryGetValue(currentSourceLine, out var list))
                        map[currentSourceLine] = list = new List<int>();
                    list.Add(asmLine);
                }
            }
        }

        // ── NVCC (CUDA) ────────────────────────────────────────────────────────

        private static readonly Regex NvccLocTag =
            new(@"^\s*\.loc\s+\d+\s+(\d+)", RegexOptions.Compiled);

        private static readonly Regex NvccSassFileTag =
            new(@"//\s+File\s+"".*""\s*,\s*line\s+(\d+)", RegexOptions.Compiled);

        private static void ParseNvcc(string asmText, Dictionary<int, List<int>> map)
        {
            var lines = asmText.Split('\n');
            int currentSourceLine = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                int asmLine = i + 1;
                var line = lines[i];

                var mLoc = NvccLocTag.Match(line);
                if (mLoc.Success)
                {
                    currentSourceLine = int.Parse(mLoc.Groups[1].Value);
                    continue;
                }

                var mSass = NvccSassFileTag.Match(line);
                if (mSass.Success)
                {
                    currentSourceLine = int.Parse(mSass.Groups[1].Value);
                    continue;
                }

                if (currentSourceLine > 0 && !string.IsNullOrWhiteSpace(line))
                {
                    var t = line.TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith('#') || t.StartsWith('.'))
                    {
                        if (t.StartsWith(".visible") || t.StartsWith(".entry") || t.StartsWith(".func"))
                            currentSourceLine = -1; // reset on new function
                        continue;
                    }

                    // For SASS, we have instructions like:
                    // /*0010*/  {  LDC R0,...  }
                    // It's definitely an instruction.

                    if (!map.TryGetValue(currentSourceLine, out var list))
                        map[currentSourceLine] = list = new List<int>();
                    list.Add(asmLine);
                }
            }
        }

        // ── GCC / clang ───────────────────────────────────────────────────────

        private static void ParseGcc(
            string asmText,
            Dictionary<int, List<int>> map,
            string? sourceText,
            string? displayAsm = null)
        {
            var lines = asmText.Split('\n');
            int currentSourceLine = -1;
            bool anyTagFound = false;

            for (int i = 0; i < lines.Length; i++)
            {
                int asmLine = i + 1;
                var line = lines[i];

                var m = GccLineTag.Match(line);
                if (m.Success)
                {
                    anyTagFound = true;
                    currentSourceLine = int.Parse(m.Groups[1].Value);
                    if (line.TrimStart().StartsWith('#')) continue;

                    if (!map.TryGetValue(currentSourceLine, out var list))
                        map[currentSourceLine] = list = new List<int>();
                    list.Add(asmLine);
                }
                else if (currentSourceLine > 0 && !string.IsNullOrWhiteSpace(line))
                {
                    var t = line.TrimStart();
                    if (t.StartsWith('#') || t.StartsWith('.')) continue;

                    // A label (non-indented, ends with ':', no spaces) is not an
                    // instruction. Function-boundary labels also signal that the
                    // current source-line context no longer applies — clang sometimes
                    // doesn't emit a new source tag at every function, so without
                    // resetting here we'd keep attributing subsequent instructions
                    // (including sentinel wrapper lines) to the wrong source line.
                    bool isLabel = !line.StartsWith(' ') && !line.StartsWith('\t')
                                   && t.EndsWith(':')
                                   && !t.Contains(' ') && !t.Contains('\t');
                    if (isLabel)
                    {
                        currentSourceLine = -1;
                        continue;
                    }

                    if (!map.TryGetValue(currentSourceLine, out var list))
                        map[currentSourceLine] = list = new List<int>();
                    list.Add(asmLine);
                }
            }

            // Clang on Windows (MSVC ABI) emits no source-location tags.
            // Fall back to matching label names to function definitions in source.
            if (!anyTagFound && sourceText != null)
                ParseClangWindowsFallback(displayAsm ?? asmText, map, sourceText);
        }

        // ── Clang-Windows fallback ────────────────────────────────────────────

        private static void ParseClangWindowsFallback(
            string asmText,
            Dictionary<int, List<int>> map,
            string sourceText)
        {
            // Walk the DISPLAY asm (already formatted) and map each display line
            // directly to the source function line. Since clang-Windows emits no
            // source-location tags, the best we can do is: all instructions inside
            // a function body map to that function's definition line in the source.
            var funcSourceLine = BuildFuncSourceMap(sourceText);
            int currentSourceLine = -1;
            var lines = asmText.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                int displayLine = i + 1;
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Detect label (display ASM has demangled unquoted labels)
                // e.g.  "add(int, int):"  or  "main:"
                bool isLabel = !line.StartsWith(' ') && !line.StartsWith('\t')
                               && trimmed.EndsWith(':')
                               && !trimmed.StartsWith('.')
                               && !trimmed.StartsWith('#');

                if (isLabel)
                {
                    // Strip trailing colon and derive the bare function name.
                    // displayAsm labels are already demangled, e.g.:
                    //   "add(int, int):"  →  shortName = "add"
                    //   "Foo::bar(int):"  →  shortName = "bar"  (last component)
                    //   "~Foo():"         →  skip (destructor, no source-map entry needed)
                    var labelText = trimmed.TrimEnd(':');

                    // Skip special labels we can't usefully map
                    if (labelText.StartsWith('~') || labelText.StartsWith("operator"))
                    {
                        currentSourceLine = -1;
                        continue;
                    }

                    // Take the last '::'-separated component before the first '('
                    int paren = labelText.IndexOf('(');
                    var basePart = paren > 0 ? labelText[..paren] : labelText;
                    int colonColon = basePart.LastIndexOf("::", StringComparison.Ordinal);
                    var shortName = (colonColon >= 0 ? basePart[(colonColon + 2)..] : basePart)
                                    .Trim().ToLowerInvariant();

                    currentSourceLine = !string.IsNullOrEmpty(shortName) &&
                                        funcSourceLine.TryGetValue(shortName, out var sl) ? sl : -1;
                    continue;
                }

                // Skip directives and comments
                if (trimmed.StartsWith('.') || trimmed.StartsWith('#')) continue;

                if (currentSourceLine > 0)
                {
                    if (!map.TryGetValue(currentSourceLine, out var list))
                        map[currentSourceLine] = list = new List<int>();
                    list.Add(displayLine);
                }
            }
        }

        // Matches a function *definition* line:
        //   optional return type tokens, then an identifier, then '('
        //   The identifier must NOT be a C++ keyword (if/for/while/return/switch/catch/…).
        //   We require at least a return-type word before the name, OR the name at column 0,
        //   to avoid matching bare keyword statements like "if (" or "while (".
        private static readonly Regex FuncDefPattern = new(
            @"^[ \t]*(?:[\w:*&<>,\s]+?\s)(\w+)\s*\(",
            RegexOptions.Compiled);

        private static readonly HashSet<string> CppKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "if", "for", "while", "do", "switch", "return", "catch", "else",
            "case", "break", "continue", "goto", "throw", "new", "delete",
            "sizeof", "alignof", "decltype", "static_assert", "namespace",
            "using", "typedef", "template", "typename", "class", "struct",
            "enum", "union", "operator", "explicit", "virtual", "override",
            "final", "inline", "static", "extern", "const", "constexpr",
            "volatile", "mutable", "friend", "public", "private", "protected",
        };

        private static Dictionary<string, int> BuildFuncSourceMap(string source)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sourceLines = source.Split('\n');

            for (int i = 0; i < sourceLines.Length; i++)
            {
                var line = sourceLines[i];
                if (!line.Contains('(')) continue;
                var t = line.TrimStart();
                // Skip comments, preprocessor, and lines that are clearly not definitions
                if (t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith('#')) continue;
                // Skip lines that start with a keyword directly (e.g. "if (", "return (")
                var firstWord = t.Split(new[] { ' ', '\t', '(' }, 2)[0];
                if (CppKeywords.Contains(firstWord)) continue;

                var m = FuncDefPattern.Match(line);
                if (m.Success)
                {
                    var name = m.Groups[1].Value;
                    // Never record a keyword as a function name
                    if (CppKeywords.Contains(name)) continue;
                    var key = name.ToLowerInvariant();
                    if (!result.ContainsKey(key))
                        result[key] = i + 1;
                }
            }
            return result;
        }

        private static string? ExtractShortName(string label)
        {
            // MSVC mangled: ?add@@...
            if (label.StartsWith('?'))
            {
                var s = label[1..];
                int at = s.IndexOf('@');
                return at > 0 ? s[..at] : s;
            }
            // Itanium: _Z3add...
            if (label.StartsWith("_Z") || label.StartsWith("__Z"))
            {
                var s = label.StartsWith("__Z") ? label[3..] : label[2..];
                if (s.StartsWith("N")) s = s[1..];
                if (s.Length > 0 && char.IsDigit(s[0]))
                {
                    int j = 0;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                    if (int.TryParse(s[..j], out var len) && len <= s.Length - j)
                        return s[j..(j + len)];
                }
                return null;
            }
            return label.TrimStart('_');
        }
    }
}