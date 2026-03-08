using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Caldera
{
    // ── Result types ──────────────────────────────────────────────────────────

    public sealed class CompileResult
    {
        public bool Success { get; init; }
        public string CompilerOutput { get; init; } = string.Empty;
        public string AsmOutput { get; init; } = string.Empty;
        public string RawAsmOutput { get; init; } = string.Empty;
        public AsmMapper.CompilerKind CompilerKind { get; init; }
        public Dictionary<int, List<int>> AsmMap { get; init; } = new();
    }

    public sealed class McaResult
    {
        public bool Success { get; init; }
        public string Output { get; init; } = string.Empty;
    }

    // ── CompilerService ───────────────────────────────────────────────────────

    public static class CompilerService
    {
        // ── Marker strings ────────────────────────────────────────────────────
        //
        // clang / g++  — volatile asm comment markers.
        //   __asm__ volatile("# CALDERA_BEGIN") survives ALL optimisation levels
        //   because volatile asm is never removed by the optimiser.
        //   The previous empty-function sentinel approach failed because the
        //   compiler would still emit a "ret" from the empty function body that
        //   leaked into the captured region, and at high -O levels the label
        //   could even be merged away.
        //
        // MSVC  — extern "C" noinline sentinel functions.
        //   extern "C" suppresses name-mangling so the PROC/ENDP labels in the
        //   /FA listing are exactly  CalderaBegin PROC  /  CalderaEnd PROC,
        //   making extraction trivial without regex-matching mangled names.

        private const string GccBeginMarker = "CALDERA_BEGIN";
        private const string GccEndMarker = "CALDERA_END";
        private const string MsvcBeginFn = "CalderaBegin";
        private const string MsvcEndFn = "CalderaEnd";

        // ── Source wrapper ────────────────────────────────────────────────────

        private static string WrapSource(string source, bool isMsvc)
        {
            if (isMsvc)
            {
                // extern "C" keeps name unmangled in the /FA listing.
                return
                    $"extern \"C\" __declspec(noinline) void {MsvcBeginFn}(){{}}\n" +
                    source + "\n" +
                    $"extern \"C\" __declspec(noinline) void {MsvcEndFn}(){{}}\n";
            }
            else
            {
                // volatile asm markers survive the optimiser on Linux/ELF targets.
                // Windows clang (MSVC ABI) is detected at runtime and bypasses
                // extraction entirely — the formatter handles the full raw output.
                return
                    $"__attribute__((used)) static void __caldera_wrap(){{\n" +
                    $"    __asm__ volatile(\"# {GccBeginMarker}\");\n" +
                    $"}}\n" +
                    source + "\n" +
                    $"__attribute__((used)) static void __caldera_wrap_end(){{\n" +
                    $"    __asm__ volatile(\"# {GccEndMarker}\");\n" +
                    $"}}\n";
            }
        }

        // ── Sentinel extraction ───────────────────────────────────────────────

        public static string ExtractSentinelRegion(string asm, bool isMsvc)
        {
            if (string.IsNullOrEmpty(asm)) return asm;
            return isMsvc ? ExtractMsvcRegion(asm) : ExtractGccRegion(asm);
        }

        // clang / g++ ─────────────────────────────────────────────────────────

        private static string ExtractGccRegion(string asm)
        {
            var lines = asm.Split('\n');

            // ── Strategy A: volatile asm comment markers (Linux clang/g++) ───────
            int beginIdx = -1, endIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (beginIdx < 0 && lines[i].Contains(GccBeginMarker, StringComparison.Ordinal))
                    beginIdx = i;
                else if (beginIdx >= 0 && lines[i].Contains(GccEndMarker, StringComparison.Ordinal))
                { endIdx = i; break; }
            }

            // ── Strategy B: noinline extern "C" sentinel labels (Windows clang) ──
            // Falls back when volatile asm comments were optimised away.
            // Windows clang emits labels in quoted COFF form: "CALDERA_BEGIN_fn":
            if (beginIdx < 0 || endIdx <= beginIdx)
            {
                beginIdx = -1; endIdx = -1;
                var beginToken = GccBeginMarker + "_fn";
                var endToken = GccEndMarker + "_fn";
                for (int i = 0; i < lines.Length; i++)
                {
                    var t = lines[i].TrimStart().Trim('"').TrimEnd(':', '"').Trim();
                    if (beginIdx < 0 && t.Equals(beginToken, StringComparison.Ordinal))
                        beginIdx = i;
                    else if (beginIdx >= 0 && t.Equals(endToken, StringComparison.Ordinal))
                    { endIdx = i; break; }
                }
            }

            if (beginIdx < 0 || endIdx <= beginIdx)
                return asm.Trim(); // no markers found at all — return everything

            // Scan forward past everything until the first real user function label.
            // A real label is: non-indented, ends with ':', not a directive, not caldera.
            int regionStart = beginIdx + 1;
            while (regionStart < endIdx)
            {
                var t = lines[regionStart].TrimStart();
                bool isRealLabel = !string.IsNullOrWhiteSpace(t)
                    && !lines[regionStart].StartsWith(' ')
                    && !lines[regionStart].StartsWith('\t')
                    && t.EndsWith(':')
                    && !t.StartsWith('.')
                    && !t.StartsWith('#')
                    && !t.Contains("caldera", StringComparison.OrdinalIgnoreCase);
                if (isRealLabel) break;
                regionStart++;
            }

            // Scan backward to find the __caldera_wrap_end label, then keep
            // walking backward to drop its entire body (e.g. the ret from the
            // empty function) until we reach the last real user instruction.
            int regionEnd = endIdx - 1;
            // First: find the wrap_end label (or any caldera label)
            while (regionEnd > regionStart)
            {
                var t = lines[regionEnd].TrimStart();
                bool isCalderaLabel = t.EndsWith(':') &&
                    (t.Contains("caldera", StringComparison.OrdinalIgnoreCase) ||
                     t.Contains(GccEndMarker, StringComparison.OrdinalIgnoreCase));
                if (isCalderaLabel)
                {
                    regionEnd--; // step before the label
                    break;
                }
                regionEnd--;
            }
            // Second: skip any instructions that belonged to that wrapper body
            // (walk backward past blank lines and single instructions like ret)
            while (regionEnd > regionStart)
            {
                var t = lines[regionEnd].TrimStart();
                if (string.IsNullOrWhiteSpace(t)) { regionEnd--; continue; }
                // If it's another caldera label, keep trimming
                if (t.EndsWith(':') && t.Contains("caldera", StringComparison.OrdinalIgnoreCase))
                { regionEnd--; continue; }
                break;
            }

            var sb = new StringBuilder();
            for (int i = regionStart; i <= regionEnd; i++)
                sb.AppendLine(lines[i].TrimEnd());

            var result = sb.ToString().Trim();

            // Safety fallback: raw slice between markers
            if (string.IsNullOrWhiteSpace(result))
            {
                sb.Clear();
                for (int i = beginIdx + 1; i < endIdx; i++)
                    sb.AppendLine(lines[i].TrimEnd());
                result = sb.ToString().Trim();
            }

            return result;
        }

        // MSVC ────────────────────────────────────────────────────────────────

        private static string ExtractMsvcRegion(string asm)
        {
            var lines = asm.Split('\n');

            int beginIdx = -1, endIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                // Match:  CalderaBegin PROC
                if (beginIdx < 0 &&
                    t.StartsWith(MsvcBeginFn, StringComparison.OrdinalIgnoreCase) &&
                    t.IndexOf("PROC", StringComparison.OrdinalIgnoreCase) >= 0)
                    beginIdx = i;
                // Match:  CalderaEnd PROC
                if (beginIdx >= 0 &&
                    t.StartsWith(MsvcEndFn, StringComparison.OrdinalIgnoreCase) &&
                    t.IndexOf("PROC", StringComparison.OrdinalIgnoreCase) >= 0)
                { endIdx = i; break; }
            }

            if (beginIdx < 0 || endIdx <= beginIdx)
                return asm.Trim();

            // Skip past CalderaBegin PROC ... CalderaBegin ENDP
            int regionStart = beginIdx + 1;
            while (regionStart < endIdx)
            {
                var t = lines[regionStart].Trim();
                if (t.StartsWith(MsvcBeginFn, StringComparison.OrdinalIgnoreCase) &&
                    t.IndexOf("ENDP", StringComparison.OrdinalIgnoreCase) >= 0)
                { regionStart++; break; }
                regionStart++;
            }

            var sb = new StringBuilder();
            for (int i = regionStart; i < endIdx; i++)
                sb.AppendLine(lines[i].TrimEnd());

            return sb.ToString().Trim();
        }

        // ── Process runner ────────────────────────────────────────────────────

        public static async Task<(string stdout, string stderr, int exitCode)> RunProcessAsync(
            string exe, string args, string? stdinText = null, string? workingDir = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinText != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            if (stdinText != null)
            {
                await proc.StandardInput.WriteAsync(stdinText);
                proc.StandardInput.Close();
            }

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return (stdout, stderr, proc.ExitCode);
        }

        // ── Compile ───────────────────────────────────────────────────────────

        public static async Task<CompileResult> CompileAsync(
            string compiler, string std, string flags, string sourceText)
        {
            if (string.IsNullOrWhiteSpace(sourceText))
                return new CompileResult { CompilerOutput = "No source to compile." };

            var id = Guid.NewGuid().ToString("N")[..8];
            var tmpDir = Path.GetTempPath();
            var srcFile = Path.Combine(tmpDir, $"caldera_{id}.cpp");
            var asmFile = Path.Combine(tmpDir, $"caldera_{id}.asm");

            var isMsvc = compiler == "cl.exe";
            var kind = isMsvc ? AsmMapper.CompilerKind.Msvc : AsmMapper.CompilerKind.ClangOrGcc;

            await File.WriteAllTextAsync(srcFile, WrapSource(sourceText, isMsvc));

            string rawAsm = string.Empty;
            string compilerOutput = string.Empty;
            int exitCode = 0;

            // ── MSVC ─────────────────────────────────────────────────────────

            if (isMsvc)
            {
                var clExe = CompilerPaths.Resolve("cl.exe");
                var clInvoke = File.Exists(clExe) ? $"\"{clExe}\"" : "cl";
                var cleanFlags = Regex.Replace(flags, @"/std:\S+\s*", "").Trim();
                var args = $"/std:{std} {cleanFlags} /c /FA /Fa\"{asmFile}\" \"{srcFile}\"";

                var batFile = Path.Combine(tmpDir, $"caldera_{id}.bat");
                var vcvars = FindVcvars64Anywhere() ?? FindVcvars64(clExe);
                var bat = new StringBuilder();
                bat.AppendLine("@echo off");
                if (vcvars != null) bat.AppendLine($"call \"{vcvars}\" >nul 2>&1");
                bat.AppendLine($"{clInvoke} {args}");
                await File.WriteAllTextAsync(batFile, bat.ToString());

                (string stdout, string stderr, int code) =
                    await RunProcessAsync("cmd.exe", $"/c \"{batFile}\"", null, tmpDir);

                exitCode = code;
                compilerOutput = $"cl.exe {args}\n{stdout}{stderr}";

                if (exitCode == 0)
                {
                    var asmPath = File.Exists(asmFile)
                        ? asmFile
                        : Path.ChangeExtension(srcFile, ".asm");
                    if (File.Exists(asmPath))
                        rawAsm = ExtractSentinelRegion(await File.ReadAllTextAsync(asmPath), isMsvc: true);
                }
            }

            // ── clang++ / g++ ─────────────────────────────────────────────────

            else
            {
                var exe = CompilerPaths.Resolve(compiler);
                var cleanFlags = Regex.Replace(flags, @"-std=\S+\s*", "").Trim();
                var args = $"-std={std} {cleanFlags} -S -fverbose-asm -masm=intel " +
                           $"-fno-asynchronous-unwind-tables -fno-dwarf2-cfi-asm " +
                           $"-fno-stack-protector -o - \"{srcFile}\"";

                (string stdout, string stderr, int code) =
                    await RunProcessAsync(exe, args, null, tmpDir);

                exitCode = code;
                compilerOutput = $"{compiler} {args}\n{stderr}";

                if (exitCode == 0)
                {
                    // Detect Windows clang (MSVC ABI): it emits COFF-style quoted labels
                    // and drops volatile asm markers at -O2/-O3, making sentinel extraction
                    // unreliable. In that case skip extraction — the formatter strips all
                    // noise from the full output directly.
                    bool isWindowsClang = stdout.Contains("\"CALDERA_BEGIN_fn\"") ||
                                         (!stdout.Contains(GccBeginMarker) &&
                                          stdout.Contains("\"\t.text\"") ||
                                          Regex.IsMatch(stdout, @"^""[^""]+"":", RegexOptions.Multiline));

                    rawAsm = isWindowsClang
                        ? stdout  // skip sentinel extraction — formatter handles full output
                        : ExtractSentinelRegion(stdout, isMsvc: false);
                }
            }

            // ── Cleanup ───────────────────────────────────────────────────────

            foreach (var ext in new[] { ".cpp", ".asm", ".obj", ".bat" })
                TryDelete(Path.Combine(tmpDir, $"caldera_{id}{ext}"));
            TryDelete(Path.ChangeExtension(srcFile, ".asm"));

            // ── Finalise ──────────────────────────────────────────────────────

            compilerOutput = compilerOutput.Trim();
            compilerOutput += exitCode == 0
                ? (string.IsNullOrWhiteSpace(rawAsm)
                    ? "\nCompilation succeeded but no ASM captured."
                    : "\nCompilation successful.")
                : string.Empty;

            // Format for display (Compiler Explorer-style clean output)
            var displayAsm = string.IsNullOrWhiteSpace(rawAsm)
                ? rawAsm
                : FormatAsmForDisplay(rawAsm, kind);

            // AsmMapper needs source-location tags that FormatAsmForDisplay strips,
            // so parse rawAsm first, then remap raw line numbers -> display line numbers.
            //
            // Source line offset: the wrapper prepends lines before the user's code,
            // so compiler-reported line numbers are shifted:
            //   MSVC:      1 line  (CalderaBegin fn)
            //   GCC/clang: 3 lines (__caldera_wrap fn body)
            //
            // Clang on Windows (MSVC ABI) emits NO source-location tags, so AsmMapper
            // falls back to scanning the user's sourceText directly — those line numbers
            // are already correct and must NOT have the offset subtracted.
            // Detect whether the compiler emitted source-location tags.
            // Clang on Windows (MSVC ABI) emits none — the fallback in AsmMapper
            // walks displayAsm directly and returns display line numbers already,
            // so we must NOT run RemapAsmLines or subtract any offset for that case.
            bool hasSourceTags = !string.IsNullOrWhiteSpace(rawAsm) &&
                System.Text.RegularExpressions.Regex.IsMatch(rawAsm,
                    isMsvc ? @";\s+Line\s+\d+" : @"#\s+\S+:\d+:");
            Dictionary<int, List<int>> asmMap;
            if (string.IsNullOrWhiteSpace(rawAsm))
            {
                asmMap = new Dictionary<int, List<int>>();
            }
            else if (!hasSourceTags && !isMsvc)
            {
                // Clang-Windows fallback: AsmMapper walks displayAsm and returns
                // display line numbers directly — no remapping or offset needed.
                asmMap = AsmMapper.Parse(rawAsm, kind, sourceText, displayAsm);
            }
            else
            {
                // GCC/clang-Linux/MSVC: parse raw ASM (tags intact), then remap
                // raw line numbers to display line numbers and subtract the wrapper offset.
                int srcLineOffset = isMsvc ? 1 : 3;
                asmMap = RemapAsmLines(
                    AsmMapper.Parse(rawAsm, kind, sourceText),
                    rawAsm, displayAsm, srcLineOffset);
            }

            // Wrap the display ASM with boundary labels so the viewer shows
            // where the user's source begins and ends.
            if (!string.IsNullOrWhiteSpace(displayAsm))
            {
                var header = "; ────── source file begin ──────\n";
                var footer = "\n; ────── source file end ────────";

                // The header adds 1 line before all user code, so every display
                // line number in asmMap must be shifted forward by 1.
                var shiftedMap = new Dictionary<int, List<int>>();
                foreach (var (srcLine, dispLines) in asmMap)
                    shiftedMap[srcLine] = dispLines.ConvertAll(l => l + 1);
                asmMap = shiftedMap;

                displayAsm = header + displayAsm + footer;
            }

            return new CompileResult
            {
                Success = exitCode == 0,
                CompilerOutput = compilerOutput,
                AsmOutput = displayAsm,
                RawAsmOutput = rawAsm,
                CompilerKind = kind,
                AsmMap = asmMap,
            };
        }

        /// <summary>
        /// Translates a source→rawAsmLines map into a source→displayAsmLines map.
        ///
        /// FormatAsmForDisplay drops many lines (directives, blank lines, source-loc
        /// comments) and re-numbers everything. We recover the mapping by matching
        /// the instruction text of each raw line against the display output.
        ///
        /// Matching strategy: strip all whitespace and trailing comments from both
        /// sides and compare the bare instruction text. When a raw instruction
        /// appears in the display output at a certain line, that display line number
        /// is what the highlighter should paint.
        /// </summary>
        private static Dictionary<int, List<int>> RemapAsmLines(
            Dictionary<int, List<int>> rawMap,
            string rawAsm,
            string displayAsm,
            int srcLineOffset = 0)
        {
            if (rawMap.Count == 0) return rawMap;

            // Build a lookup: normalizedInstructionText -> list of 1-based display line numbers.
            // We use a list because the same instruction can appear multiple times.
            var displayLines = displayAsm.Split('\n');
            var displayIndex = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            for (int i = 0; i < displayLines.Length; i++)
            {
                var key = NormalizeAsmLine(displayLines[i]);
                if (string.IsNullOrEmpty(key)) continue;
                if (!displayIndex.TryGetValue(key, out var q))
                    displayIndex[key] = q = new Queue<int>();
                q.Enqueue(i + 1); // 1-based
            }

            // Build raw line → display line translation table.
            var rawLines = rawAsm.Split('\n');
            var rawToDisplay = new Dictionary<int, int>(); // 1-based raw -> 1-based display
            // We walk the display lines in order to consume each instruction once,
            // preserving the same ordering as the raw ASM.
            // Reset the display index cursors by rebuilding per raw-line lookup in order.
            // Simpler: just use the queue — first raw occurrence maps to first display occurrence.
            for (int i = 0; i < rawLines.Length; i++)
            {
                var key = NormalizeAsmLine(rawLines[i]);
                if (string.IsNullOrEmpty(key)) continue;
                if (displayIndex.TryGetValue(key, out var q) && q.Count > 0)
                    rawToDisplay[i + 1] = q.Dequeue(); // consume so duplicates map sequentially
            }

            // Translate the map, subtracting the wrapper's line offset from each
            // source line number so it aligns with the user's editor line numbers.
            var result = new Dictionary<int, List<int>>();
            foreach (var (srcLine, rawAsmLines) in rawMap)
            {
                int editorLine = srcLine - srcLineOffset;
                if (editorLine < 1) continue; // belongs to the wrapper, not user code
                var displayAsmLines = new List<int>();
                foreach (var rawLine in rawAsmLines)
                {
                    if (rawToDisplay.TryGetValue(rawLine, out var dispLine))
                        displayAsmLines.Add(dispLine);
                }
                if (displayAsmLines.Count > 0)
                    result[editorLine] = displayAsmLines;
            }
            return result;
        }

        // Normalize an ASM line for matching: trim whitespace, strip trailing # comments,
        // collapse internal whitespace. Returns empty string for directives/blank lines.
        private static readonly System.Text.RegularExpressions.Regex NormTrailingComment =
            new(@"\s+#.*$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex NormWhitespace =
            new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string NormalizeAsmLine(string line)
        {
            var t = line.TrimStart();
            // Skip blank lines, labels, directives, pure comments
            if (string.IsNullOrWhiteSpace(t)) return string.Empty;
            if (t.StartsWith('.') || t.StartsWith('#') || t.StartsWith(';')) return string.Empty;
            if (t.EndsWith(':') && !t.Contains(' ') && !t.Contains('\t')) return string.Empty; // label
            // Strip trailing comments and normalize spaces
            t = NormTrailingComment.Replace(t, "");
            t = NormWhitespace.Replace(t.Trim(), " ");
            return t;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // ── llvm-mca ──────────────────────────────────────────────────────────

        public static async Task<McaResult> RunMcaAsync(
            string asmText, string mcaFlags,
            AsmMapper.CompilerKind kind = AsmMapper.CompilerKind.ClangOrGcc)
        {
            if (string.IsNullOrWhiteSpace(asmText))
                return new McaResult { Output = "No ASM output to analyse. Compile first." };

            var exe = CompilerPaths.Resolve("llvm-mca");

            string asmForMca;
            if (kind == AsmMapper.CompilerKind.Msvc)
            {
                asmForMca = ".intel_syntax noprefix\n" + SanitizeMsvcAsmForMca(asmText);
                mcaFlags = (mcaFlags + " --skip-unsupported-instructions=parse-failure").Trim();
            }
            else
            {
                asmForMca = ".intel_syntax noprefix\n" + StripGasDirectives(asmText);
            }

            var fullFlags = mcaFlags.Trim();

            try
            {
                (string stdout, string stderr, int code) =
                    await RunProcessAsync(exe, fullFlags, asmForMca);

                string output = code == 0
                    ? (string.IsNullOrWhiteSpace(stdout) ? stderr : stdout)
                    : $"llvm-mca exited with code {code}\n{stderr}"
                      + (string.IsNullOrWhiteSpace(stdout) ? "" : "\n" + stdout);

                return new McaResult { Success = code == 0, Output = output.Trim() };
            }
            catch (Exception ex)
            {
                return new McaResult { Output = $"Error launching llvm-mca:\n{ex.Message}" };
            }
        }

        // ── Compiler Explorer-style ASM formatter ─────────────────────────────

        /// <summary>
        /// Post-processes raw extracted ASM into clean Compiler Explorer-style output.
        /// • clang/g++: removes GAS directives, verbose-asm source comments, and
        ///   demangles simple labels so  _Z3addyy:  →  add(unsigned long long, unsigned long long):
        ///   (best-effort; complex templates are left as-is).
        /// • MSVC: removes variable-offset annotations, PROC/ENDP decorators,
        ///   source-line comments, and noise preamble lines.
        /// </summary>
        public static string FormatAsmForDisplay(string asm, AsmMapper.CompilerKind kind)
        {
            if (string.IsNullOrEmpty(asm)) return asm;
            return kind == AsmMapper.CompilerKind.Msvc
                ? FormatMsvcAsm(asm)
                : FormatGccAsm(asm);
        }

        // ── GAS/clang display formatter ───────────────────────────────────────

        // All GAS directives and assembler metadata dropped from display.
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
        // These are SIMD/FP literal pools emitted into .rdata — pure data, not instructions.
        private static readonly Regex ConstPoolLabel = new(
            @"^(__ymm@|__xmm@|__real@|__float@|__int@)[\w@]+\s*:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Pure-noise comment lines dropped entirely
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

        // Strip ALL trailing # comments from instruction lines
        private static readonly Regex DisplayTrailingComment = new(
            @"\s+#.*$",
            RegexOptions.Compiled);

        // Windows clang emits quoted mangled labels:  "?add@@YA_K_K0@Z":
        private static readonly Regex QuotedLabel = new(
            @"^""([^""]+)""\s*:",
            RegexOptions.Compiled);

        // Local labels (.LBB0_1:  .loop:  .Ltmp3: etc.) — dropped from display
        // Matches both indented and non-indented forms
        private static readonly Regex LocalLabel = new(
            @"^\s*\.[\w.]+\s*:(\s*$|\s+#)",
            RegexOptions.Compiled);

        private static string FormatGccAsm(string asm)
        {
            var sb = new StringBuilder();
            bool firstLabel = true;
            bool inCalderaBody = false; // true while inside a caldera wrapper function body

            foreach (var rawLine in asm.Split('\n'))
            {
                var line = rawLine.TrimEnd();

                if (DisplayDropLine.IsMatch(line)) continue;
                if (DisplayDropComment.IsMatch(line)) continue;

                var trimmed = line.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Drop @feat.00 and similar MSVC/COFF metadata symbols
                if (trimmed.StartsWith("@feat.") || trimmed.StartsWith("@comp.id"))
                    continue;

                // Drop local labels (.LBB0_1:  .loop:  etc.)
                if (LocalLabel.IsMatch(trimmed)) continue;

                // Drop constant pool labels (__ymm@fff...:  __real@3ff...:)
                if (ConstPoolLabel.IsMatch(trimmed)) continue;

                // Quoted label (Windows clang):  "?add@@YA_K_K0@Z":
                var quotedMatch = QuotedLabel.Match(trimmed);
                if (quotedMatch.Success)
                {
                    var rawLabel = quotedMatch.Groups[1].Value;
                    if (ConstPoolLabel.IsMatch(rawLabel + ":")) continue;
                    // Drop caldera wrapper quoted labels and their bodies
                    if (rawLabel.Contains("caldera", StringComparison.OrdinalIgnoreCase))
                    { inCalderaBody = true; continue; }
                    inCalderaBody = false;
                    var label = TryDemangleMsvcName(rawLabel) ?? TryDemangleItanium(rawLabel) ?? rawLabel;
                    if (!firstLabel) sb.AppendLine();
                    sb.AppendLine(label + ":");
                    firstLabel = false;
                    continue;
                }

                // Strip all trailing # comments from instructions
                line = DisplayTrailingComment.Replace(line, "").TrimEnd();
                trimmed = line.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Function label: non-indented, ends with ':', not a directive or data label
                bool isFuncLabel = !line.StartsWith(' ') && !line.StartsWith('\t')
                                   && trimmed.EndsWith(':')
                                   && !trimmed.StartsWith('.')
                                   && !trimmed.StartsWith('#')
                                   && !ConstPoolLabel.IsMatch(trimmed);
                if (isFuncLabel)
                {
                    var rawLabel = trimmed.TrimEnd(':');
                    // Drop caldera wrapper labels and mark their body for skipping
                    if (rawLabel.Contains("caldera", StringComparison.OrdinalIgnoreCase))
                    { inCalderaBody = true; continue; }
                    inCalderaBody = false;
                    var label = TryDemangleItanium(rawLabel)
                             ?? (rawLabel.StartsWith('?') ? TryDemangleMsvcName(rawLabel) : null)
                             ?? rawLabel;
                    if (!firstLabel) sb.AppendLine();
                    sb.AppendLine(label + ":");
                    firstLabel = false;
                }
                else
                {
                    // Skip instructions that belong to a caldera wrapper body
                    if (inCalderaBody) continue;
                    sb.AppendLine("        " + trimmed);
                }
            }

            return sb.ToString().Trim();
        }

        // ── MSVC display formatter ────────────────────────────────────────────

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

        // Variable offset annotations: p$ = 8  or  _q$ = 16
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

                // PROC label → function header
                var procMatch = MsvcProcLabel.Match(trimmed);
                if (procMatch.Success)
                {
                    var rawLabel = procMatch.Groups[1].Value;
                    if (MsvcSentinelNames.Contains(rawLabel))
                    { inCalderaBody = true; continue; }
                    inCalderaBody = false;
                    var label = TryDemangleMsvcName(rawLabel) ?? rawLabel;
                    if (!firstLabel) sb.AppendLine();
                    sb.AppendLine(label + ":");
                    firstLabel = false;
                    continue;
                }

                // ENDP → end of a function body (clears caldera body flag too)
                if (trimmed.IndexOf("ENDP", StringComparison.OrdinalIgnoreCase) >= 0)
                { inCalderaBody = false; continue; }

                if (MsvcDropDisplay.IsMatch(line)) continue;
                if (MsvcOffsetAnnotation.IsMatch(line)) continue;

                // Skip instructions inside caldera sentinel bodies
                if (inCalderaBody) continue;

                // Strip trailing ; comment
                var cleaned = MsvcTrailingComment.Replace(line, "").TrimEnd();
                if (string.IsNullOrWhiteSpace(cleaned)) continue;

                // Clean up MSVC-isms in instruction operands
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

        // ── MSVC name demangler ───────────────────────────────────────────────
        // Decodes common patterns from /FA output.
        // ?add@@YA_K_K0@Z  →  add(unsigned long long, unsigned long long)
        // Back-reference digit (0,1,2...) = re-use param at that index in the param list.
        private static readonly Dictionary<char, string> _msvcTypeMap = new()
        {
            ['X'] = "void",
            ['D'] = "char",
            ['C'] = "signed char",
            ['E'] = "unsigned char",
            ['F'] = "short",
            ['G'] = "unsigned short",
            ['H'] = "int",
            ['I'] = "unsigned int",
            ['J'] = "long",
            ['K'] = "unsigned long",
            ['M'] = "float",
            ['N'] = "double",
            ['O'] = "long double",
        };

        private static readonly Dictionary<char, string> _msvcUnderscoreMap = new()
        {
            ['J'] = "long long",
            ['K'] = "unsigned long long",
            ['W'] = "wchar_t",
            ['N'] = "__int128",
            ['F'] = "__int16",
            ['G'] = "unsigned __int16",
        };

        private static string? TryDemangleMsvcName(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith('?')) return null;
            try
            {
                var s = name[1..];

                // Template instantiation: ??$dot@M@@... → function name is "dot"
                // ??$ means: ? (start) ?$ (template marker) name @ args @@
                if (s.StartsWith("?$"))
                {
                    s = s[2..];
                    int at = s.IndexOf('@');
                    if (at <= 0) return null;
                    // Just return the bare template name — type args are too complex to decode
                    return s[..at] + "(...)";
                }

                int atIdx = s.IndexOf('@');
                if (atIdx <= 0) return null;
                var funcName = s[..atIdx];
                s = s[(atIdx + 1)..];

                // Class scope: ?foo@Bar@@...
                string? className = null;
                if (s.Length > 0 && s[0] != '@' && s[0] != 'Y' && !char.IsDigit(s[0]))
                {
                    int at2 = s.IndexOf('@');
                    if (at2 > 0) { className = s[..at2]; s = s[(at2 + 1)..]; }
                }

                // Locate 'Y' (function type encoding start)
                int yi = s.IndexOf('Y');
                if (yi < 0) return null;
                s = s[(yi + 1)..];
                if (s.Length > 0) s = s[1..]; // skip calling-convention letter

                // Skip return type
                s = SkipMsvcType(s, new List<string>(), out _);

                // Parse parameter types
                var paramTypes = new List<string>();
                while (s.Length > 0 && s[0] != '@' && s[0] != 'Z')
                {
                    s = SkipMsvcType(s, paramTypes, out var t);
                    if (t == "void" || t == null) break;
                    paramTypes.Add(t);
                }

                var qualName = className != null ? $"{className}::{funcName}" : funcName;
                return qualName + "(" + string.Join(", ", paramTypes) + ")";
            }
            catch { return null; }
        }

        private static string SkipMsvcType(string s, List<string> paramHistory, out string? typeName)
        {
            typeName = null;
            if (s.Length == 0) return s;
            char c = s[0];

            // Pointer / reference
            if (c == 'P' || c == 'Q') // pointer
            {
                var s2 = s[1..];
                if (s2.Length > 0 && (s2[0] == 'A' || s2[0] == 'B')) s2 = s2[1..]; // cv
                s2 = SkipMsvcType(s2, paramHistory, out var inner);
                typeName = (inner ?? "?") + "*";
                return s2;
            }
            if (c == 'A') // reference
            {
                var s2 = s[1..];
                if (s2.Length > 0 && (s2[0] == 'A' || s2[0] == 'B')) s2 = s2[1..];
                s2 = SkipMsvcType(s2, paramHistory, out var inner);
                typeName = (inner ?? "?") + "&";
                return s2;
            }
            if (c == '_' && s.Length > 1)
            {
                typeName = _msvcUnderscoreMap.TryGetValue(s[1], out var t2) ? t2 : null;
                return s[2..];
            }
            // Back-reference digit: 0 = first param, 1 = second, etc.
            if (char.IsDigit(c))
            {
                int idx = c - '0';
                typeName = idx < paramHistory.Count ? paramHistory[idx] : null;
                return s[1..];
            }
            if (_msvcTypeMap.TryGetValue(c, out var t)) { typeName = t; return s[1..]; }
            return s[1..]; // unknown — skip
        }

        // ── Itanium ABI demangler (clang/g++ Linux or MinGW) ─────────────────
        private static readonly Dictionary<char, string> _itaniumTypeMap = new()
        {
            ['v'] = "void",
            ['b'] = "bool",
            ['c'] = "char",
            ['a'] = "signed char",
            ['h'] = "unsigned char",
            ['s'] = "short",
            ['t'] = "unsigned short",
            ['i'] = "int",
            ['j'] = "unsigned int",
            ['l'] = "long",
            ['m'] = "unsigned long",
            ['x'] = "long long",
            ['y'] = "unsigned long long",
            ['f'] = "float",
            ['d'] = "double",
            ['e'] = "long double",
            ['z'] = "...",
        };

        private static string? TryDemangleItanium(string mangled)
        {
            if (!mangled.StartsWith("_Z") && !mangled.StartsWith("__Z")) return null;
            try
            {
                var s = mangled.StartsWith("__Z") ? mangled[3..] : mangled[2..];
                string funcName;
                string remaining;

                if (s.StartsWith("N")) // nested name: _ZN3Foo3barEv
                {
                    s = s[1..];
                    var parts = new List<string>();
                    while (s.Length > 0 && s[0] != 'E')
                    {
                        // Template instantiation: I...E
                        if (s[0] == 'I') { s = SkipToE(s[1..]); continue; }
                        if (!char.IsDigit(s[0])) break;
                        var len = ParseLength(s, out var after);
                        if (len <= 0 || len > after.Length) break;
                        parts.Add(after[..len]);
                        s = after[len..];
                    }
                    funcName = string.Join("::", parts);
                    remaining = s.StartsWith("E") ? s[1..] : s;
                }
                else if (char.IsDigit(s[0])) // simple name: _Z3add
                {
                    var len = ParseLength(s, out var after);
                    if (len <= 0 || len > after.Length) return null;
                    funcName = after[..len];
                    remaining = after[len..];
                    // Skip template args if present
                    if (remaining.StartsWith("I"))
                        remaining = SkipToE(remaining[1..]);
                }
                else return null;

                if (remaining.Length == 0 || remaining == "v")
                    return funcName + "()";

                // Parse parameter types
                var paramTypes = new List<string>();
                var sub = new List<string>(); // substitution table
                int i = 0;
                while (i < remaining.Length && remaining[i] != 'E')
                {
                    var (typeName, advance) = ParseItaniumType(remaining[i..], sub);
                    if (typeName == null || advance == 0) break;
                    if (typeName != "void") paramTypes.Add(typeName);
                    i += advance;
                }

                return funcName + "(" + string.Join(", ", paramTypes) + ")";
            }
            catch { return null; }
        }

        private static (string? type, int advance) ParseItaniumType(string s, List<string> sub)
        {
            if (s.Length == 0) return (null, 0);
            char c = s[0];

            // Qualifiers — consume and recurse
            if (c == 'K' || c == 'r' || c == 'V') // const, restrict, volatile
            {
                var (inner, adv) = ParseItaniumType(s[1..], sub);
                string qual = c == 'K' ? "const " : c == 'r' ? "__restrict__ " : "volatile ";
                return (inner != null ? qual + inner : null, adv + 1);
            }

            // Pointer / reference
            if (c == 'P')
            {
                var (inner, adv) = ParseItaniumType(s[1..], sub);
                return (inner != null ? inner + "*" : null, adv + 1);
            }
            if (c == 'R')
            {
                var (inner, adv) = ParseItaniumType(s[1..], sub);
                return (inner != null ? inner + "&" : null, adv + 1);
            }
            if (c == 'O')
            {
                var (inner, adv) = ParseItaniumType(s[1..], sub);
                return (inner != null ? inner + "&&" : null, adv + 1);
            }

            // Substitution S_ S0_ S1_ ...
            if (c == 'S')
            {
                if (s.Length > 1 && s[1] == '_')
                {
                    var t = sub.Count > 0 ? sub[0] : "?";
                    return (t, 2);
                }
                if (s.Length > 2 && char.IsDigit(s[1]) && s[2] == '_')
                {
                    int idx = (s[1] - '0') + 1;
                    var t = idx < sub.Count ? sub[idx] : "?";
                    return (t, 3);
                }
                // St = std::
                if (s.Length > 1 && s[1] == 't') return ("std", 2);
                return ("?", 1);
            }

            // Template args — skip entirely
            if (c == 'I')
            {
                var rest = SkipToE(s[1..]);
                return ("...", s.Length - rest.Length + 1);
            }

            // Builtin type
            if (_itaniumTypeMap.TryGetValue(c, out var bt))
            {
                if (bt != "void") sub.Add(bt);
                return (bt, 1);
            }

            // User-defined type (length-prefixed name)
            if (char.IsDigit(c))
            {
                var len = ParseLength(s, out var after);
                if (len > 0 && len <= after.Length)
                {
                    var name = after[..len];
                    sub.Add(name);
                    return (name, s.Length - after.Length + len);
                }
            }

            return (null, 1); // unknown — skip one char
        }

        // Skip past a template argument list, consuming until the matching 'E'
        private static string SkipToE(string s)
        {
            int depth = 0;
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == 'I') depth++;
                else if (s[i] == 'E') { if (depth == 0) return s[(i + 1)..]; depth--; }
                i++;
            }
            return string.Empty;
        }

        private static int ParseLength(string s, out string remainder)
        {
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == 0) { remainder = s; return -1; }
            var len = int.Parse(s[..i]);
            remainder = s[i..];
            return len;
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

        // Strip all trailing # comments from instructions
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

            var mangled = new Regex(@"\?\?|\?[A-Za-z$]|FLAT:|@[A-Za-z0-9_]+@@", RegexOptions.Compiled);
            var offsetFlat = new Regex(@"OFFSET\s+FLAT:\s*", RegexOptions.Compiled);
            var shortKw = new Regex(@"\bSHORT\s+", RegexOptions.Compiled);
            var scopedLabel = new Regex(@"\$([A-Za-z0-9_]+)@([A-Za-z0-9_]+)", RegexOptions.Compiled);
            var trailingComment = new Regex(@"\s*;.*$", RegexOptions.Compiled);
            var hexLit = new Regex(@"\b([0-9A-Fa-f]+)H\b", RegexOptions.Compiled);

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

        // ── vcvars / MSVC discovery ───────────────────────────────────────────

        public static string? FindVcvars64(string clExePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(clExePath);
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var candidate = Path.Combine(dir, "Auxiliary", "Build", "vcvars64.bat");
                    if (File.Exists(candidate)) return candidate;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
            return null;
        }

        public static string? FindVcvars64Anywhere()
        {
            var vswhereLocations = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft Visual Studio", "Installer", "vswhere.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft Visual Studio", "Installer", "vswhere.exe"),
            };

            foreach (var vswhere in vswhereLocations)
            {
                if (!File.Exists(vswhere)) continue;
                try
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = vswhere,
                        Arguments = "-latest -property installationPath",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    })!;
                    var path = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(path))
                    {
                        var c = Path.Combine(path, "VC", "Auxiliary", "Build", "vcvars64.bat");
                        if (File.Exists(c)) return c;
                    }
                }
                catch { }
            }

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] years = { "2022", "2019", "2017" };
            string[] editions = { "Enterprise", "Professional", "Community", "BuildTools", "Preview" };

            foreach (var root in new[] { pf, pfx86 })
                foreach (var year in years)
                    foreach (var ed in editions)
                    {
                        var c = Path.Combine(root, "Microsoft Visual Studio", year, ed,
                            "VC", "Auxiliary", "Build", "vcvars64.bat");
                        if (File.Exists(c)) return c;
                    }

            return null;
        }
    }
}