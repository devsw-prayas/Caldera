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
        public bool   Success        { get; init; }
        public string CompilerOutput { get; init; } = string.Empty;
        public string AsmOutput      { get; init; } = string.Empty;
        public string RawAsmOutput   { get; init; } = string.Empty;
        public AsmMapper.CompilerKind CompilerKind { get; init; }
        public Dictionary<int, List<int>> AsmMap { get; init; } = new();
    }

    public sealed class McaResult
    {
        public bool   Success { get; init; }
        public string Output  { get; init; } = string.Empty;
    }

    // ── CompilerService ───────────────────────────────────────────────────────

    public static class CompilerService
    {
        // ── Marker strings ────────────────────────────────────────────────────
        //
        // clang / g++  — volatile asm comment markers.
        //   __asm__ volatile("# CALDERA_BEGIN") survives ALL optimisation levels
        //   because volatile asm is never removed by the optimiser.
        //
        // MSVC  — extern "C" noinline sentinel functions.
        //   extern "C" suppresses name-mangling so the PROC/ENDP labels in the
        //   /FA listing are exactly  CalderaBegin PROC  /  CalderaEnd PROC,
        //   making extraction trivial without regex-matching mangled names.

        private const string GccBeginMarker = "CALDERA_BEGIN";
        private const string GccEndMarker   = "CALDERA_END";
        private const string MsvcBeginFn    = "CalderaBegin";
        private const string MsvcEndFn      = "CalderaEnd";

        // ── Source wrapper ────────────────────────────────────────────────────

        private static string WrapSource(string source, bool isMsvc)
        {
            if (isMsvc)
            {
                return
                    $"extern \"C\" __declspec(noinline) void {MsvcBeginFn}(){{}}\n" +
                    source + "\n" +
                    $"extern \"C\" __declspec(noinline) void {MsvcEndFn}(){{}}\n";
            }
            else
            {
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

            int beginIdx = -1, endIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (beginIdx < 0 && lines[i].Contains(GccBeginMarker, StringComparison.Ordinal))
                    beginIdx = i;
                else if (beginIdx >= 0 && lines[i].Contains(GccEndMarker, StringComparison.Ordinal))
                { endIdx = i; break; }
            }

            // Strategy B: noinline extern "C" sentinel labels (Windows clang)
            if (beginIdx < 0 || endIdx <= beginIdx)
            {
                beginIdx = -1; endIdx = -1;
                var beginToken = GccBeginMarker + "_fn";
                var endToken   = GccEndMarker   + "_fn";
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
                return asm.Trim();

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

            int regionEnd = endIdx - 1;
            while (regionEnd > regionStart)
            {
                var t = lines[regionEnd].TrimStart();
                bool isCalderaLabel = t.EndsWith(':') &&
                    (t.Contains("caldera", StringComparison.OrdinalIgnoreCase) ||
                     t.Contains(GccEndMarker, StringComparison.OrdinalIgnoreCase));
                if (isCalderaLabel) { regionEnd--; break; }
                regionEnd--;
            }
            while (regionEnd > regionStart)
            {
                var t = lines[regionEnd].TrimStart();
                if (string.IsNullOrWhiteSpace(t)) { regionEnd--; continue; }
                if (t.EndsWith(':') && t.Contains("caldera", StringComparison.OrdinalIgnoreCase))
                { regionEnd--; continue; }
                break;
            }

            var sb = new StringBuilder();
            for (int i = regionStart; i <= regionEnd; i++)
                sb.AppendLine(lines[i].TrimEnd());

            var result = sb.ToString().Trim();

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
                if (beginIdx < 0 &&
                    t.StartsWith(MsvcBeginFn, StringComparison.OrdinalIgnoreCase) &&
                    t.IndexOf("PROC", StringComparison.OrdinalIgnoreCase) >= 0)
                    beginIdx = i;
                if (beginIdx >= 0 &&
                    t.StartsWith(MsvcEndFn, StringComparison.OrdinalIgnoreCase) &&
                    t.IndexOf("PROC", StringComparison.OrdinalIgnoreCase) >= 0)
                { endIdx = i; break; }
            }

            if (beginIdx < 0 || endIdx <= beginIdx)
                return asm.Trim();

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
            string exe, string args, string? stdinText = null, string? workingDir = null, System.Threading.CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                RedirectStandardInput  = stdinText != null,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                WorkingDirectory       = workingDir ?? Environment.CurrentDirectory,
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            if (stdinText != null)
            {
                await proc.StandardInput.WriteAsync(stdinText);
                proc.StandardInput.Close();
            }

            using var ctr = ct.Register(() => { try { proc.Kill(true); } catch { } });

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            return (stdout, stderr, proc.ExitCode);
        }

        // ── Compile ───────────────────────────────────────────────────────────

        public static async Task<CompileResult> CompileAsync(
            string compiler, string std, string flags, string sourceText, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sourceText))
                return new CompileResult { CompilerOutput = "No source to compile." };

            var id      = Guid.NewGuid().ToString("N")[..8];
            var tmpDir  = Path.GetTempPath();
            var srcFile = Path.Combine(tmpDir, $"caldera_{id}.cpp");
            var asmFile = Path.Combine(tmpDir, $"caldera_{id}.asm");

            var isWsl = compiler.StartsWith("WSL ");
            var compilerName = isWsl ? compiler.Substring(4) : compiler;
            var isMsvc = compilerName == "cl.exe";
            var kind   = isMsvc ? AsmMapper.CompilerKind.Msvc : AsmMapper.CompilerKind.ClangOrGcc;

            await File.WriteAllTextAsync(srcFile, WrapSource(sourceText, isMsvc), ct);

            string rawAsm         = string.Empty;
            string compilerOutput = string.Empty;
            int    exitCode       = 0;

            // ── MSVC ─────────────────────────────────────────────────────────

            if (isMsvc)
            {
                var clExe    = CompilerPaths.Resolve("cl.exe");
                var clInvoke = File.Exists(clExe) ? $"\"{clExe}\"" : "cl";
                var cleanFlags = Regex.Replace(flags, @"/std:\S+\s*", "").Trim();
                var args = $"/std:{std} {cleanFlags} /c /FA /Fa\"{asmFile}\" \"{srcFile}\"";

                var batFile = Path.Combine(tmpDir, $"caldera_{id}.bat");
                var vcvars  = FindVcvars64Anywhere() ?? FindVcvars64(clExe);
                var bat     = new StringBuilder();
                bat.AppendLine("@echo off");
                if (vcvars != null) bat.AppendLine($"call \"{vcvars}\" >nul 2>&1");
                bat.AppendLine($"{clInvoke} {args}");
                await File.WriteAllTextAsync(batFile, bat.ToString(), ct);

                (string stdout, string stderr, int code) =
                    await RunProcessAsync("cmd.exe", $"/c \"{batFile}\"", null, tmpDir, ct);

                exitCode = code;
                compilerOutput = $"cl.exe {args}\n{stdout}{stderr}";

                if (exitCode == 0)
                {
                    var asmPath = File.Exists(asmFile)
                        ? asmFile
                        : Path.ChangeExtension(srcFile, ".asm");
                    if (File.Exists(asmPath))
                        rawAsm = ExtractSentinelRegion(await File.ReadAllTextAsync(asmPath, ct), isMsvc: true);
                }
            }

            // ── clang++ / g++ ─────────────────────────────────────────────────

            else
            {
                var exe        = isWsl ? "wsl" : CompilerPaths.Resolve(compilerName);
                var cleanFlags = Regex.Replace(flags, @"-std=\S+\s*", "").Trim();
                
                string args;
                if (isWsl)
                {
                    var drive = char.ToLowerInvariant(srcFile[0]);
                    var wslSrc = $"/mnt/{drive}/{srcFile.Substring(3).Replace('\\', '/')}";
                    args = $"-e {compilerName} -std={std} {cleanFlags} -S -fverbose-asm -masm=intel " +
                           $"-fno-asynchronous-unwind-tables -fno-dwarf2-cfi-asm " +
                           $"-fno-stack-protector -o - \"{wslSrc}\"";
                }
                else
                {
                    args = $"-std={std} {cleanFlags} -S -fverbose-asm -masm=intel " +
                           $"-fno-asynchronous-unwind-tables -fno-dwarf2-cfi-asm " +
                           $"-fno-stack-protector -o - \"{srcFile}\"";
                }

                (string stdout, string stderr, int code) =
                    await RunProcessAsync(exe, args, null, tmpDir, ct);

                exitCode = code;
                compilerOutput = $"{compiler} {args}\n{stderr}";

                if (exitCode == 0)
                {
                    bool isWindowsClang = stdout.Contains("\"CALDERA_BEGIN_fn\"") ||
                                         (!stdout.Contains(GccBeginMarker) &&
                                          stdout.Contains("\"\t.text\"") ||
                                          Regex.IsMatch(stdout, @"^""[^""]+"":", RegexOptions.Multiline));

                    rawAsm = isWindowsClang
                        ? stdout
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

            var displayAsm = string.IsNullOrWhiteSpace(rawAsm)
                ? rawAsm
                : AsmFormatter.FormatAsmForDisplay(rawAsm, kind);

            bool hasSourceTags = !string.IsNullOrWhiteSpace(rawAsm) &&
                Regex.IsMatch(rawAsm, isMsvc ? @";\s+Line\s+\d+" : @"#\s+\S+:\d+:");

            Dictionary<int, List<int>> asmMap;
            if (string.IsNullOrWhiteSpace(rawAsm))
            {
                asmMap = new Dictionary<int, List<int>>();
            }
            else if (!hasSourceTags && !isMsvc)
            {
                asmMap = AsmMapper.Parse(rawAsm, kind, sourceText, displayAsm);
            }
            else
            {
                int srcLineOffset = isMsvc ? 1 : 3;
                asmMap = RemapAsmLines(
                    AsmMapper.Parse(rawAsm, kind, sourceText),
                    rawAsm, displayAsm, srcLineOffset);
            }

            if (!string.IsNullOrWhiteSpace(displayAsm))
            {
                var header = "; ────── source file begin ──────\n";
                var footer = "\n; ────── source file end ────────";

                var shiftedMap = new Dictionary<int, List<int>>();
                foreach (var (srcLine, dispLines) in asmMap)
                    shiftedMap[srcLine] = dispLines.ConvertAll(l => l + 1);
                asmMap = shiftedMap;

                displayAsm = header + displayAsm + footer;
            }

            return new CompileResult
            {
                Success        = exitCode == 0,
                CompilerOutput = compilerOutput,
                AsmOutput      = displayAsm,
                RawAsmOutput   = rawAsm,
                CompilerKind   = kind,
                AsmMap         = asmMap,
            };
        }

        // ── Raw-line → display-line remapping ────────────────────────────────

        private static Dictionary<int, List<int>> RemapAsmLines(
            Dictionary<int, List<int>> rawMap,
            string rawAsm,
            string displayAsm,
            int srcLineOffset = 0)
        {
            if (rawMap.Count == 0) return rawMap;

            var displayLines = displayAsm.Split('\n');
            var displayIndex = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            for (int i = 0; i < displayLines.Length; i++)
            {
                var key = NormalizeAsmLine(displayLines[i]);
                if (string.IsNullOrEmpty(key)) continue;
                if (!displayIndex.TryGetValue(key, out var q))
                    displayIndex[key] = q = new Queue<int>();
                q.Enqueue(i + 1);
            }

            var rawLines    = rawAsm.Split('\n');
            var rawToDisplay = new Dictionary<int, int>();
            for (int i = 0; i < rawLines.Length; i++)
            {
                var key = NormalizeAsmLine(rawLines[i]);
                if (string.IsNullOrEmpty(key)) continue;
                if (displayIndex.TryGetValue(key, out var q) && q.Count > 0)
                    rawToDisplay[i + 1] = q.Dequeue();
            }

            var result = new Dictionary<int, List<int>>();
            foreach (var (srcLine, rawAsmLines) in rawMap)
            {
                int editorLine = srcLine - srcLineOffset;
                if (editorLine < 1) continue;
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

        private static readonly Regex NormTrailingComment =
            new(@"\s+#.*$", RegexOptions.Compiled);
        private static readonly Regex NormWhitespace =
            new(@"\s+", RegexOptions.Compiled);

        private static string NormalizeAsmLine(string line)
        {
            var t = line.TrimStart();
            if (string.IsNullOrWhiteSpace(t)) return string.Empty;
            if (t.StartsWith('.') || t.StartsWith('#') || t.StartsWith(';')) return string.Empty;
            if (t.EndsWith(':') && !t.Contains(' ') && !t.Contains('\t')) return string.Empty;
            t = NormTrailingComment.Replace(t, "");
            t = NormWhitespace.Replace(t.Trim(), " ");
            return t;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
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
                        FileName               = vswhere,
                        Arguments              = "-latest -property installationPath",
                        RedirectStandardOutput = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
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

            var pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] years    = { "2022", "2019", "2017" };
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