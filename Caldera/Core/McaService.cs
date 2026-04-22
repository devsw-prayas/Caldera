using System;
using System.Threading.Tasks;

namespace Caldera
{
    // ── llvm-mca runner ───────────────────────────────────────────────────────

    public static class McaService
    {
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
                asmForMca = ".intel_syntax noprefix\n" + AsmFormatter.SanitizeMsvcAsmForMca(asmText);
                mcaFlags = (mcaFlags + " --skip-unsupported-instructions=parse-failure").Trim();
            }
            else
            {
                asmForMca = ".intel_syntax noprefix\n" + AsmFormatter.StripGasDirectives(asmText);
            }

            try
            {
                (string stdout, string stderr, int code) =
                    await CompilerService.RunProcessAsync(exe, mcaFlags.Trim(), asmForMca);

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
    }
}
