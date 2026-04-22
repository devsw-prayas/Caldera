using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Caldera
{
    // ── ASM display formatters ────────────────────────────────────────────────
    //
    // FormatAsmForDisplay          — public entry point
    // FormatGccAsm                 — clang / g++ GAS output
    // FormatMsvcAsm                — MSVC /FA output
    // StripGasDirectives           — strips GAS directives for llvm-mca input
    // SanitizeMsvcAsmForMca        — strips MSVC /FA noise for llvm-mca input

    internal static class AsmFormatter
    {
        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Post-processes raw extracted ASM into clean Compiler Explorer-style output.
        /// • clang/g++: removes GAS directives, verbose-asm source comments, and
        ///   demangles simple labels.
        /// • MSVC: removes variable-offset annotations, PROC/ENDP decorators,
        ///   source-line comments, and noise preamble lines.
        /// </summary>
        public static string FormatAsmForDisplay(string asm, AsmMapper.CompilerKind kind)
        {
            if (string.IsNullOrEmpty(asm)) return asm;
            if (kind == AsmMapper.CompilerKind.Msvc) return FormatMsvcAsm(asm);
            if (kind == AsmMapper.CompilerKind.Nvcc) return FormatNvccAsm(asm);
            return FormatGccAsm(asm);
        }

        // ── GAS / clang display formatter ─────────────────────────────────────

        private static readonly Regex DisplayDropLine = new(
            @"^\s*(" +
            @"\.file\b|\.text\b|\.data\b|\.bss\b|\.section\b" +
            @"|\.globl\b|\.global\b|\.weak\b|\.local\b|\.hidden\b|\.protected\b" +
            @"|\.comm\b|\.lcomm\b" +
            @"|\.type\b|\.size\b" +
            @"|\.p2align\b|\.align\b|\.balign\b|\.nops\b" +
            @"|\.cfi_" +
            @"|\.ident\b|\.addrsig\b|\.addrsig_sym\b|\.attribute\b" +
            @"|\.intel_syntax\b|\.att_syntax\b" +
            @"|\.Lfunc_begin|\.Lfunc_end|\.Ltmp|\.LBB|\.Lcfi" +
            @"|\.def\b|\.scl\b|\.endef\b|\.cv_" +
            @"|\.set\b|\.quad\b|\.long\b|\.short\b|\.byte\b" +
            @"|\.string\b|\.asciz\b|\.ascii\b|\.space\b|\.zero\b" +
            @"|\.loc\b" +
            @")",
            RegexOptions.Compiled);

        // Windows clang constant-pool labels:
        //   __ymm@fffff...  __real@3ff00000...  __xmm@...
        private static readonly Regex ConstPoolLabel = new(
            @"^(__ymm@|__xmm@|__real@|__float@|__int@)[\w@]+\s*:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DisplayDropComment = new(
            @"^\s*(" +
            @"/APP|/NO_APP|#NO_APP|#APP" +
            @"|# %bb\.\d+:" +
            @"|#\s+\d+\s+""[^""]*""" +
            @"|#\s+\S+:\d+" +
            @"|# kill:" +
            @"|# implicit-def:" +
            @"|# dbg_value" +
            @"|# -- End function" +
            @")",
            RegexOptions.Compiled);

        private static readonly Regex DisplayTrailingComment = new(
            @"\s+#.*$",
            RegexOptions.Compiled);

        // Windows clang emits quoted mangled labels:  "?add@@YA_K_K0@Z":
        private static readonly Regex QuotedLabel = new(
            @"^""([^""]+)""\s*:",
            RegexOptions.Compiled);

        private static readonly Regex LocalLabel = new(
            @"^\s*\.[\w.]+\s*:(\s*$|\s+#)",
            RegexOptions.Compiled);

        private static string FormatGccAsm(string asm)
        {
            var sb = new StringBuilder();
            bool firstLabel = true;
            bool inCalderaBody = false;

            foreach (var rawLine in asm.Split('\n'))
            {
                var line = rawLine.TrimEnd();

                if (DisplayDropLine.IsMatch(line)) continue;
                if (DisplayDropComment.IsMatch(line)) continue;

                var trimmed = line.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.StartsWith("@feat.") || trimmed.StartsWith("@comp.id"))
                    continue;

                if (LocalLabel.IsMatch(trimmed)) continue;
                if (ConstPoolLabel.IsMatch(trimmed)) continue;

                // Quoted label (Windows clang):  "?add@@YA_K_K0@Z":
                var quotedMatch = QuotedLabel.Match(trimmed);
                if (quotedMatch.Success)
                {
                    var rawLabel = quotedMatch.Groups[1].Value;
                    if (ConstPoolLabel.IsMatch(rawLabel + ":")) continue;
                    if (rawLabel.Contains("caldera", StringComparison.OrdinalIgnoreCase))
                    { inCalderaBody = true; continue; }
                    inCalderaBody = false;
                    var label = AsmDemangler.TryDemangleMsvcName(rawLabel)
                             ?? AsmDemangler.TryDemangleItanium(rawLabel)
                             ?? rawLabel;
                    if (!firstLabel) sb.AppendLine();
                    sb.AppendLine(label + ":");
                    firstLabel = false;
                    continue;
                }

                line = DisplayTrailingComment.Replace(line, "").TrimEnd();
                trimmed = line.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                bool isFuncLabel = !line.StartsWith(' ') && !line.StartsWith('\t')
                                   && trimmed.EndsWith(':')
                                   && !trimmed.StartsWith('.')
                                   && !trimmed.StartsWith('#')
                                   && !ConstPoolLabel.IsMatch(trimmed);
                if (isFuncLabel)
                {
                    var rawLabel = trimmed.TrimEnd(':');
                    if (rawLabel.Contains("caldera", StringComparison.OrdinalIgnoreCase))
                    { inCalderaBody = true; continue; }
                    inCalderaBody = false;
                    var label = AsmDemangler.TryDemangleItanium(rawLabel)
                             ?? (rawLabel.StartsWith('?') ? AsmDemangler.TryDemangleMsvcName(rawLabel) : null)
                             ?? rawLabel;
                    if (!firstLabel) sb.AppendLine();
                    sb.AppendLine(label + ":");
                    firstLabel = false;
                }
                else
                {
                    if (inCalderaBody) continue;
                    sb.AppendLine("        " + trimmed);
                }
            }

            return sb.ToString().Trim();
        }

        // ── MSVC display formatter ─────────────────────────────────────────────

        private static readonly Regex MsvcDropDisplay = new(
            @"^\s*(" +
            @";\s*(File\s|Line\s|\d+\s*:|Function compile|COMDAT|Listing generated)" +
            @"|[A-Za-z_?][A-Za-z0-9_?@$]*\s+ENDP\b" +
            @"|_TEXT\s+(SEGMENT|ENDS)" +
            @"|CONST\s+(SEGMENT|ENDS)" +
            @"|PUBLIC\b|EXTRN\b|INCLUDELIB\b|include\b" +
            @"|END\s*$" +
            @"|# License|# The use of|# See https" +
            @"|npad\b" +
            @"|\$Size\$|\$Where\$" +
            @")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MsvcOffsetAnnotation = new(
            @"^\s*[A-Za-z_$][A-Za-z0-9_$]*\s*=\s*-?\d+\s*$",
            RegexOptions.Compiled);

        private static readonly Regex MsvcTrailingComment = new(
            @"\s*;.*$",
            RegexOptions.Compiled);

        private static readonly Regex MsvcOffsetFlat = new(
            @"OFFSET\s+FLAT:\s*",
            RegexOptions.Compiled);

        private static readonly Regex MsvcShortKeyword = new(
            @"\bSHORT\s+",
            RegexOptions.Compiled);

        private static readonly Regex MsvcHexLiteral = new(
            @"\b([0-9A-Fa-f]+)H\b",
            RegexOptions.Compiled);

        private static readonly Regex MsvcProcLabel = new(
            @"^([A-Za-z_?][A-Za-z0-9_?@$]*)\s+PROC\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> MsvcSentinelNames =
            new(StringComparer.OrdinalIgnoreCase) { "CalderaBegin", "CalderaEnd" };

        private static string FormatMsvcAsm(string asm)
        {
            var sb = new StringBuilder();
            bool firstLabel = true;
            bool inCalderaBody = false;

            foreach (var rawLine in asm.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.TrimStart();

                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var procMatch = MsvcProcLabel.Match(trimmed);
                if (procMatch.Success)
                {
                    var rawLabel = procMatch.Groups[1].Value;
                    if (MsvcSentinelNames.Contains(rawLabel))
                    { inCalderaBody = true; continue; }
                    inCalderaBody = false;
                    var label = AsmDemangler.TryDemangleMsvcName(rawLabel) ?? rawLabel;
                    if (!firstLabel) sb.AppendLine();
                    sb.AppendLine(label + ":");
                    firstLabel = false;
                    continue;
                }

                if (trimmed.IndexOf("ENDP", StringComparison.OrdinalIgnoreCase) >= 0)
                { inCalderaBody = false; continue; }

                if (MsvcDropDisplay.IsMatch(line)) continue;
                if (MsvcOffsetAnnotation.IsMatch(line)) continue;

                if (inCalderaBody) continue;

                var cleaned = MsvcTrailingComment.Replace(line, "").TrimEnd();
                if (string.IsNullOrWhiteSpace(cleaned)) continue;

                cleaned = MsvcOffsetFlat.Replace(cleaned, "");
                cleaned = MsvcShortKeyword.Replace(cleaned, "");
                cleaned = MsvcHexLiteral.Replace(cleaned,
                    m => Convert.ToInt64(m.Groups[1].Value, 16).ToString());

                cleaned = cleaned.TrimEnd();
                if (string.IsNullOrWhiteSpace(cleaned)) continue;

                sb.AppendLine("        " + cleaned.TrimStart());
            }

            return sb.ToString().Trim();
        }

        // ── NVCC display formatter ─────────────────────────────────────────────

        private static readonly Regex NvccDropLine = new(
            @"^\s*(" +
            @"\.loc\b|\.file\b|\.version\b|\.target\b|\.address_size\b" +
            @")",
            RegexOptions.Compiled);

        private static readonly Regex NvccSassDropLine = new(
            @"^//\s+File\s+"".*""\s*,\s*line\s+\d+",
            RegexOptions.Compiled);

        private static string FormatNvccAsm(string asm)
        {
            var sb = new StringBuilder();
            bool firstFunc = true;

            foreach (var rawLine in asm.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (NvccDropLine.IsMatch(line) || NvccSassDropLine.IsMatch(line)) continue;

                var trimmed = line.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.StartsWith("//") && !trimmed.Contains("File")) 
                {
                    // keep standard comments if they seem useful, or drop them
                    // PTX doesn't have many standard // comments besides function headers.
                }

                if (trimmed.StartsWith(".visible") || trimmed.StartsWith(".entry") || trimmed.StartsWith(".func"))
                {
                    if (!firstFunc) sb.AppendLine();
                    sb.AppendLine(trimmed); 
                    firstFunc = false;
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
            return sb.ToString().Trim();
        }

        // ── GAS directive stripper (clang/g++ → MCA) ─────────────────────────

        private static readonly Regex GasDropLine = new(
            @"^\s*(" +
            @"\.file\b|\.text\b|\.data\b|\.bss\b|\.section\b" +
            @"|\.globl\b|\.global\b|\.weak\b|\.local\b|\.hidden\b|\.protected\b" +
            @"|\.comm\b|\.lcomm\b" +
            @"|\.type\b|\.size\b" +
            @"|\.p2align\b|\.align\b|\.balign\b|\.nops\b" +
            @"|\.cfi_" +
            @"|\.ident\b|\.addrsig\b|\.addrsig_sym\b|\.attribute\b" +
            @"|\.intel_syntax\b|\.att_syntax\b" +
            @"|\.Lfunc_begin|\.Lfunc_end|\.Ltmp|\.LBB|\.Lcfi" +
            @"|\.def\b|\.scl\b|\.endef\b|\.cv_" +
            @"|\.set\b|\.quad\b|\.long\b|\.short\b|\.byte\b" +
            @"|\.string\b|\.asciz\b|\.ascii\b|\.space\b|\.zero\b" +
            @"|\.loc\b" +
            @"|/APP|/NO_APP|#APP|#NO_APP" +
            @"|#\s+\d+\s+""[^""]*""" +
            @"|#\s+\S+:\d+" +
            @")",
            RegexOptions.Compiled);

        private static readonly Regex GasTrailingComment = new(
            @"\s+#.*$",
            RegexOptions.Compiled);

        public static string StripGasDirectives(string asm)
        {
            if (string.IsNullOrEmpty(asm)) return asm;
            var sb = new StringBuilder();
            foreach (var rawLine in asm.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (GasDropLine.IsMatch(line)) continue;
                line = GasTrailingComment.Replace(line, "").TrimEnd();
                if (string.IsNullOrWhiteSpace(line)) continue;
                sb.AppendLine(line);
            }
            return sb.ToString().Trim();
        }

        // ── MSVC ASM sanitizer for llvm-mca ───────────────────────────────────

        public static string SanitizeMsvcAsmForMca(string masm)
        {
            if (string.IsNullOrEmpty(masm)) return masm;

            var dropLine = new Regex(
                @"^\s*(; Line \d+" +
                @"|; Function compile|; COMDAT" +
                @"|_TEXT\s+(SEGMENT|ENDS)" +
                @"|\$Size\$|\$Where\$" +
                @"|\w+ PROC\b|\w+ ENDP\b" +
                @"|END\s*$|include\b|INCLUDELIB\b|EXTRN\b|PUBLIC\b" +
                @"|CONST\s+(SEGMENT|ENDS)|npad\b)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var mangled        = new Regex(@"\?\?|\?[A-Za-z$]|FLAT:|@[A-Za-z0-9_]+@@", RegexOptions.Compiled);
            var offsetFlat     = new Regex(@"OFFSET\s+FLAT:\s*", RegexOptions.Compiled);
            var shortKw        = new Regex(@"\bSHORT\s+", RegexOptions.Compiled);
            var scopedLabel    = new Regex(@"\$([A-Za-z0-9_]+)@([A-Za-z0-9_]+)", RegexOptions.Compiled);
            var trailingComment = new Regex(@"\s*;.*$", RegexOptions.Compiled);
            var hexLit         = new Regex(@"\b([0-9A-Fa-f]+)H\b", RegexOptions.Compiled);

            int block = 0;
            var sb = new StringBuilder();
            foreach (var rawLine in masm.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (dropLine.IsMatch(line)) { block++; continue; }
                line = trailingComment.Replace(line, "");
                line = offsetFlat.Replace(line, "");
                if (mangled.IsMatch(line)) continue;
                line = shortKw.Replace(line, "");
                line = scopedLabel.Replace(line,
                    m => $".{m.Groups[1].Value}_{m.Groups[2].Value}_{block}");
                line = hexLit.Replace(line,
                    m => Convert.ToInt64(m.Groups[1].Value, 16).ToString());
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine(line);
            }
            return sb.ToString().Trim();
        }
    }
}
