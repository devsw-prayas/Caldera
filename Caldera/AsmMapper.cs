using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Caldera
{
    /// <summary>
    /// Parses interleaved asm output (clang/g++ -Msource, or MSVC /Fas)
    /// and builds a mapping from source line number → list of asm line numbers.
    /// </summary>
    public static class AsmMapper
    {
        // clang/g++ -fverbose-asm emits trailing comments like:
        //   movl  %edi, -4(%rbp)          # /path/file.cpp:5:10
        // or as a standalone comment line:
        //   # /path/file.cpp:5:
        private static readonly Regex ClangLineTag =
            new(@"#\s+\S+:(\d+):", RegexOptions.Compiled);

        // MSVC /Fas emits:  ; 42 :   (possibly with leading whitespace)
        private static readonly Regex MsvcLineTag =
            new(@"^\s*;\s+(\d+)\s*:", RegexOptions.Compiled);

        public enum CompilerKind { ClangOrGcc, Msvc }

        /// <summary>
        /// Parse asm text and return mapping: sourceLine (1-based) -> asm lines (1-based).
        /// </summary>
        public static Dictionary<int, List<int>> Parse(string asmText, CompilerKind kind)
        {
            var map = new Dictionary<int, List<int>>();
            if (string.IsNullOrEmpty(asmText)) return map;

            var lines = asmText.Split('\n');
            int currentSourceLine = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                int asmLine = i + 1; // 1-based
                var line = lines[i];

                if (kind == CompilerKind.Msvc)
                {
                    // MSVC /FA: source tag is a standalone comment line preceding the block
                    var m = MsvcLineTag.Match(line);
                    if (m.Success)
                    {
                        currentSourceLine = int.Parse(m.Groups[1].Value);
                        continue; // tag line itself not mapped
                    }

                    if (currentSourceLine > 0 && !string.IsNullOrWhiteSpace(line))
                    {
                        if (!map.TryGetValue(currentSourceLine, out var list))
                            map[currentSourceLine] = list = new List<int>();
                        list.Add(asmLine);
                    }
                }
                else
                {
                    // clang/g++ -fverbose-asm: source tag is an inline trailing comment
                    // on instruction lines, e.g.:  movl %edi, -4(%rbp)  # file.cpp:5:10
                    // Standalone comment lines like  # file.cpp:5:  also appear as section markers
                    var m = ClangLineTag.Match(line);
                    if (m.Success)
                    {
                        currentSourceLine = int.Parse(m.Groups[1].Value);

                        // If this line is purely a comment (no instruction), don't map it
                        var trimmed = line.TrimStart();
                        if (trimmed.StartsWith("#")) continue;

                        // It's an instruction with an inline tag — map this line
                        if (!map.TryGetValue(currentSourceLine, out var list))
                            map[currentSourceLine] = list = new List<int>();
                        list.Add(asmLine);
                    }
                    else if (currentSourceLine > 0 && !string.IsNullOrWhiteSpace(line))
                    {
                        // Continuation instruction under the same source line
                        // (lines without a tag inherit the last seen source line)
                        var trimmed = line.TrimStart();
                        if (trimmed.StartsWith("#") || trimmed.StartsWith(".")) continue;

                        if (!map.TryGetValue(currentSourceLine, out var list))
                            map[currentSourceLine] = list = new List<int>();
                        list.Add(asmLine);
                    }
                }
            }

            return map;
        }
    }
}