using System;
using System.Collections.Generic;
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
        // ── Asm filter ────────────────────────────────────────────────────────

        private static readonly Regex FuncLabel =
            new(@"^[A-Za-z_?@][^:]*:\s*(#.*)?$", RegexOptions.Compiled);

        private static readonly Regex LocalLabel =
            new(@"^\.[A-Za-z0-9_]+:", RegexOptions.Compiled);

        private static readonly string[] StripPrefixes =
        {
            ".def ", ".scl ", ".type ", ".endef", ".globl", ".weak",
            ".section", ".addrsig", ".ident", ".file ", ".intel_syntax",
            ".att_syntax", ".p2align", ".align", ".long ", ".quad ",
            ".byte ", ".asciz", ".string ", ".comm ", ".lcomm",
            ".extern", ".set ", ".size ", ".loc ", ".cfi_",
        };

        /// <summary>
        /// Filters MSVC /FA output, keeping only functions whose source file
        /// comment matches caldera_src.cpp (i.e. user code, not STL internals).
        /// MSVC emits ; File ... inside the PROC block, so we buffer each block
        /// and only flush it if the file comment matches.
        /// </summary>
        public static string FilterMsvcAsmOutput(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            var result = new StringBuilder();
            var buffer = new StringBuilder();
            bool inProc = false;
            bool isUserFunc = false;

            foreach (var rawLine in raw.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.TrimStart();

                // PROC starts a new function block — begin buffering
                if (!inProc && trimmed.EndsWith(" PROC"))
                {
                    inProc = true;
                    isUserFunc = false;
                    buffer.Clear();
                    buffer.AppendLine(line);
                    continue;
                }

                if (!inProc) continue;

                // Check if this function comes from the user's source file
                if (trimmed.StartsWith("; File ", StringComparison.OrdinalIgnoreCase))
                {
                    isUserFunc = trimmed.Contains("caldera_src.cpp",
                        StringComparison.OrdinalIgnoreCase);
                    buffer.AppendLine(line);
                    continue;
                }

                // ENDP closes the block — flush if it's user code
                if (trimmed.EndsWith(" ENDP") || trimmed == "ENDP")
                {
                    buffer.AppendLine(line);
                    if (isUserFunc)
                    {
                        result.Append(buffer);
                        result.AppendLine();
                    }
                    inProc = false;
                    buffer.Clear();
                    continue;
                }

                buffer.AppendLine(line);
            }

            return result.ToString().Trim();
        }

        /// <summary>
        /// Strips Windows/COFF debug noise from clang -S output,
        /// keeping function bodies and their verbose-asm source comments.
        /// </summary>
        public static string FilterAsmOutput(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            var kept = new StringBuilder();
            bool inFunction = false;

            foreach (var rawLine in raw.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.TrimStart();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    if (inFunction) kept.AppendLine();
                    continue;
                }

                if (!inFunction && FuncLabel.IsMatch(trimmed) && !LocalLabel.IsMatch(trimmed))
                {
                    inFunction = true;
                    kept.AppendLine(line);
                    continue;
                }

                if (!inFunction) continue;

                if (trimmed.StartsWith(".section") || trimmed.StartsWith(".addrsig") ||
                    trimmed.StartsWith(".ident") || trimmed.StartsWith(".file "))
                {
                    inFunction = false;
                    continue;
                }

                bool isDirective = false;
                foreach (var prefix in StripPrefixes)
                    if (trimmed.StartsWith(prefix)) { isDirective = true; break; }

                if (isDirective && !LocalLabel.IsMatch(trimmed)) continue;

                kept.AppendLine(line);
            }

            return kept.ToString().Trim();
        }

        // ── MSVC helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Walks up from a known cl.exe path to find its matching vcvars64.bat.
        /// </summary>
        public static string? FindVcvars64(string clExePath)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(clExePath);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    var candidate = System.IO.Path.Combine(dir, "Auxiliary", "Build", "vcvars64.bat");
                    if (System.IO.File.Exists(candidate)) return candidate;
                    dir = System.IO.Path.GetDirectoryName(dir);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Searches for vcvars64.bat across all installed Visual Studio versions
        /// without needing a known cl.exe path.
        /// Strategy:
        ///   1. vswhere.exe  (present in every VS 2017+ install)
        ///   2. Fixed paths for VS 2022 / 2019 / 2017 under Program Files
        /// </summary>
        public static string? FindVcvars64Anywhere()
        {
            // 1 ── vswhere
            var vswhereCandidates = new[]
            {
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft Visual Studio", "Installer", "vswhere.exe"),
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft Visual Studio", "Installer", "vswhere.exe"),
            };

            foreach (var vswhere in vswhereCandidates)
            {
                if (!System.IO.File.Exists(vswhere)) continue;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = vswhere,
                        Arguments = "-latest -property installationPath",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    var installPath = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();

                    if (!string.IsNullOrEmpty(installPath))
                    {
                        var candidate = System.IO.Path.Combine(
                            installPath, "VC", "Auxiliary", "Build", "vcvars64.bat");
                        if (System.IO.File.Exists(candidate)) return candidate;
                    }
                }
                catch { }
            }

            // 2 ── fixed paths
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var editions = new[] { "Enterprise", "Professional", "Community", "BuildTools", "Preview" };
            var years = new[] { "2022", "2019", "2017" };
            var roots = new[] { pf, pfx86 };

            foreach (var root in roots)
                foreach (var year in years)
                    foreach (var edition in editions)
                    {
                        var candidate = System.IO.Path.Combine(
                            root, "Microsoft Visual Studio", year, edition,
                            "VC", "Auxiliary", "Build", "vcvars64.bat");
                        if (System.IO.File.Exists(candidate)) return candidate;
                    }

            return null;
        }

        // ── Process runner ────────────────────────────────────────────────────

        public static async Task<(string stdout, string stderr, int exitCode)>
            RunProcessAsync(string exe, string args, string? stdinText = null)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinText != null,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
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

            var tmpDir = System.IO.Path.GetTempPath();
            var srcFile = System.IO.Path.Combine(tmpDir, "caldera_src.cpp");
            var outFile = System.IO.Path.Combine(tmpDir, "caldera_out.exe");
            var asmFile = System.IO.Path.Combine(tmpDir, "caldera_src.asm");

            await System.IO.File.WriteAllTextAsync(srcFile, sourceText);

            string rawAsm = string.Empty;
            string compilerOutput = string.Empty;
            int exitCode = 0;
            var kind = AsmMapper.CompilerKind.ClangOrGcc;

            if (compiler == "cl.exe")
            {
                kind = AsmMapper.CompilerKind.Msvc;

                var clExe = CompilerPaths.Resolve("cl.exe");
                var cleanFlags = Regex.Replace(flags, @"/std:\S+\s*", "").Trim();
                // /c = compile only, no link — we only need the .asm file
                var clArgs = $"/std:{std} {cleanFlags} /c /FA /Fa\"{asmFile}\" \"{srcFile}\"";
                var hasPath = System.IO.File.Exists(clExe);

                // Write a .bat file to avoid cmd /c quoting issues entirely
                var batFile = System.IO.Path.Combine(tmpDir, "caldera_build.bat");
                var vcvars = hasPath ? FindVcvars64(clExe) : FindVcvars64Anywhere();
                var clInvoke = hasPath ? $"\"{clExe}\"" : "cl";

                var batContent = "@echo off\r\n";
                if (vcvars != null)
                    batContent += $"call \"{vcvars}\" >nul 2>&1\r\n";
                batContent += $"{clInvoke} {clArgs}\r\n";

                await System.IO.File.WriteAllTextAsync(batFile, batContent);

                (string clStdout, string clStderr, int code) =
                    await RunProcessAsync("cmd.exe", $"/c \"{batFile}\"");
                exitCode = code;
                compilerOutput = $"cl.exe {clArgs}\n{clStdout}{clStderr}".Trim();

                if (exitCode == 0)
                {
                    // MSVC sometimes writes the .asm next to the source — check both locations
                    var asmAlt = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(srcFile)!,
                        System.IO.Path.GetFileNameWithoutExtension(srcFile) + ".asm");

                    var foundAsm = System.IO.File.Exists(asmFile) ? asmFile
                                 : System.IO.File.Exists(asmAlt) ? asmAlt
                                 : null;

                    if (foundAsm != null)
                    {
                        var raw = await System.IO.File.ReadAllTextAsync(foundAsm);
                        var filtered = FilterMsvcAsmOutput(raw);
                        rawAsm = string.IsNullOrWhiteSpace(filtered) ? raw : filtered;
                    }
                    else
                    {
                        compilerOutput += $"\n[Debug] ASM file not found at:\n  {asmFile}\n  {asmAlt}";
                    }
                }
            }
            else
            {
                kind = AsmMapper.CompilerKind.ClangOrGcc;
                var exe = CompilerPaths.Resolve(compiler);
                var cleanFlags = Regex.Replace(flags, @"-std=\S+\s*", "").Trim();
                var args = $"-std={std} {cleanFlags} -S -fverbose-asm " +
                           $"-fno-asynchronous-unwind-tables " +
                           $"-masm=intel -o - \"{srcFile}\"";

                (string stdout, string stderr, int code) = await RunProcessAsync(exe, args);
                exitCode = code;
                compilerOutput = $"{compiler} {args}\n{stderr}";
                // Temporarily bypassing filter — swap comments to re-enable
                // rawAsm = string.IsNullOrWhiteSpace(stdout) ? stdout : FilterAsmOutput(stdout);
                rawAsm = stdout;
            }

            compilerOutput = compilerOutput.Trim();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(compilerOutput))
                compilerOutput += "\nCompilation successful.";

            var asmMap = string.IsNullOrWhiteSpace(rawAsm)
                ? new Dictionary<int, List<int>>()
                : AsmMapper.Parse(rawAsm, kind);

            return new CompileResult
            {
                Success = exitCode == 0,
                CompilerOutput = compilerOutput,
                AsmOutput = rawAsm,
                CompilerKind = kind,
                AsmMap = asmMap,
            };
        }

        // ── llvm-mca ──────────────────────────────────────────────────────────

        /// <summary>
        /// Converts filtered MSVC MASM output into plain Intel-syntax that
        /// llvm-mca can parse.
        /// </summary>
        public static string SanitizeMsvcAsmForMca(string masm)
        {
            if (string.IsNullOrEmpty(masm)) return masm;

            // Drop entire lines matching these patterns
            var dropLine = new Regex(
                @"(;\s+Line\s+\d+" +
                @"|;\s+Function\s+compile" +
                @"|;\s+COMDAT" +
                @"|_TEXT\s+(SEGMENT|ENDS)" +
                @"|\$Size\$" +
                @"|\$Where\$" +
                @"|\bPROC\b" +
                @"|\bENDP\b" +
                @"|^\s*END\s*$" +
                @"|^\s*include\b" +          // include listing.inc
                @"|^\s*INCLUDELIB\b" +       // INCLUDELIB LIBCMT
                @"|^\s*EXTRN\b" +            // EXTRN _fltused:DWORD
                @"|^\s*PUBLIC\b" +           // PUBLIC symbols
                @"|^\s*CONST\b" +            // CONST SEGMENT/ENDS
                @"|^\s*npad\b" +             // npad alignment pseudo-op
                @")",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Drop lines with mangled MSVC symbols
            var mangledSymbol = new Regex(
                @"\?\?" +
                @"|\?[A-Za-z$]" +
                @"|FLAT:" +
                @"|@[A-Za-z0-9_]+@@",
                RegexOptions.Compiled);

            // Remove OFFSET FLAT:
            var offsetFlat = new Regex(@"OFFSET\s+FLAT:", RegexOptions.Compiled);

            // Remove SHORT qualifier from branches: jl SHORT $label -> jl $label
            var shortQualifier = new Regex(@"\bSHORT\s+", RegexOptions.Compiled);

            // Rename MASM scoped labels ($LC13@dot -> .LC13_dot) so llvm-mc accepts them
            var scopedLabel = new Regex(@"\$([A-Za-z0-9_]+)@([A-Za-z0-9_]+)",
                RegexOptions.Compiled);

            // Strip trailing ; comments
            var trailingComment = new Regex(@"\s*;.*$", RegexOptions.Compiled);

            // Convert MASM hex literals: 40H -> 64
            var hexLiteral = new Regex(@"\b([0-9A-Fa-f]+)H\b", RegexOptions.Compiled);

            int blockIndex = 0;
            var sb = new StringBuilder();
            foreach (var rawLine in masm.Split('\n'))
            {
                var line = rawLine.TrimEnd();

                if (string.IsNullOrWhiteSpace(line)) continue;
                if (dropLine.IsMatch(line)) { blockIndex++; continue; }

                line = trailingComment.Replace(line, "");
                line = offsetFlat.Replace(line, "");

                if (mangledSymbol.IsMatch(line)) continue;

                line = shortQualifier.Replace(line, "");
                // Make scoped labels unique per block so duplicate template
                // instantiations don't redefine the same label name
                line = scopedLabel.Replace(line, m =>
                    $".{m.Groups[1].Value}_{m.Groups[2].Value}_{blockIndex}");
                line = hexLiteral.Replace(line, m =>
                    Convert.ToInt64(m.Groups[1].Value, 16).ToString());

                if (string.IsNullOrWhiteSpace(line)) continue;

                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        public static async Task<McaResult> RunMcaAsync(
            string asmText, string mcaFlags,
            AsmMapper.CompilerKind kind = AsmMapper.CompilerKind.ClangOrGcc)
        {
            if (string.IsNullOrWhiteSpace(asmText))
                return new McaResult { Output = "No ASM output to analyse. Run the compiler first." };

            var exe = CompilerPaths.Resolve("llvm-mca");

            // Normalise to Intel syntax that llvm-mca understands
            string asmForMca;
            if (kind == AsmMapper.CompilerKind.Msvc)
                asmForMca = ".intel_syntax noprefix\n" + SanitizeMsvcAsmForMca(asmText);
            else
                asmForMca = ".intel_syntax noprefix\n" + asmText;

            try
            {
                (string stdout, string stderr, int code) =
                    await RunProcessAsync(exe, mcaFlags, asmForMca);

                return new McaResult
                {
                    Success = code == 0,
                    Output = code == 0
                        ? stdout
                        : $"llvm-mca exited with code {code}:\n{stderr}"
                };
            }
            catch (Exception ex)
            {
                return new McaResult { Output = $"Error launching llvm-mca:\n{ex.Message}" };
            }
        }
    }
}