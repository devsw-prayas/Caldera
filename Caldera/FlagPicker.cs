using System.Collections.Generic;

namespace Caldera
{
    // ── Flag picker data models ───────────────────────────────────────────────

    public class FlagItem
    {
        public string Flag { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class FlagGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public List<FlagItem> Flags { get; set; } = new();
    }

    // ── Flag definitions per compiler ─────────────────────────────────────────

    public static class FlagPickerData
    {
        public static readonly List<FlagGroup> McaFlags = new()
        {
            new FlagGroup { GroupName = "TARGET", Flags = new()
            {
                new() { Flag = "--mcpu=native",         Description = "Current host CPU" },
                new() { Flag = "--mcpu=znver4",         Description = "AMD Zen 4" },
                new() { Flag = "--mcpu=znver3",         Description = "AMD Zen 3" },
                new() { Flag = "--mcpu=znver2",         Description = "AMD Zen 2" },
                new() { Flag = "--mcpu=skylake",        Description = "Intel Skylake" },
                new() { Flag = "--mcpu=skylake-avx512", Description = "Intel Skylake + AVX-512" },
                new() { Flag = "--mcpu=alderlake",      Description = "Intel Alder Lake" },
                new() { Flag = "--mcpu=sapphirerapids", Description = "Intel Sapphire Rapids" },
                new() { Flag = "--mcpu=icelake-server", Description = "Intel Ice Lake (server)" },
            }},
            new FlagGroup { GroupName = "ANALYSIS", Flags = new()
            {
                new() { Flag = "--iterations=100",      Description = "Simulation iterations" },
                new() { Flag = "--iterations=1000",     Description = "More accurate simulation" },
                new() { Flag = "--dispatch=4",          Description = "4-wide dispatch" },
                new() { Flag = "--dispatch=6",          Description = "6-wide dispatch" },
                new() { Flag = "--noalias",             Description = "Assume no memory aliasing" },
                new() { Flag = "--bottleneck-analysis", Description = "Show bottleneck report" },
            }},
            new FlagGroup { GroupName = "OUTPUT", Flags = new()
            {
                new() { Flag = "--timeline",            Description = "Show instruction timeline" },
                new() { Flag = "--timeline-max-iterations=5", Description = "Limit timeline rows" },
                new() { Flag = "--resource-pressure",   Description = "Show resource pressure" },
                new() { Flag = "--instruction-tables",  Description = "Per-instruction latency table" },
                new() { Flag = "--all-stats",           Description = "Print all statistics" },
                new() { Flag = "--all-views",           Description = "Print all views" },
            }},
        };

        public static readonly Dictionary<string, List<FlagGroup>> CompilerFlags = new()
        {
            ["clang++"] = new()
            {
                new FlagGroup { GroupName = "OPTIMIZATION", Flags = new()
                {
                    new() { Flag = "-O0",                 Description = "No optimization" },
                    new() { Flag = "-O1",                 Description = "Basic optimization" },
                    new() { Flag = "-O2",                 Description = "Moderate optimization" },
                    new() { Flag = "-O3",                 Description = "Aggressive optimization" },
                    new() { Flag = "-Os",                 Description = "Optimize for size" },
                    new() { Flag = "-Oz",                 Description = "Minimize size (clang)" },
                    new() { Flag = "-Ofast",              Description = "O3 + unsafe math" },
                }},
                new FlagGroup { GroupName = "ARCHITECTURE", Flags = new()
                {
                    new() { Flag = "-march=native",       Description = "Tune for current CPU" },
                    new() { Flag = "-march=x86-64",       Description = "Baseline x86-64" },
                    new() { Flag = "-march=x86-64-v3",    Description = "AVX2 baseline" },
                    new() { Flag = "-mavx2",              Description = "Enable AVX2" },
                    new() { Flag = "-mavx512f",           Description = "Enable AVX-512F" },
                    new() { Flag = "-madx",               Description = "Enable ADCX/ADOX" },
                    new() { Flag = "-mbmi2",              Description = "Enable BMI2" },
                    new() { Flag = "-mtune=native",       Description = "Schedule for host CPU" },
                }},
                new FlagGroup { GroupName = "DEBUG / DIAGNOSTICS", Flags = new()
                {
                    new() { Flag = "-g",                  Description = "Debug info (DWARF)" },
                    new() { Flag = "-g3",                 Description = "Full debug info + macros" },
                    new() { Flag = "-Wall",               Description = "Common warnings" },
                    new() { Flag = "-Wextra",             Description = "Extra warnings" },
                    new() { Flag = "-Wpedantic",          Description = "Strict standard conformance" },
                    new() { Flag = "-Werror",             Description = "Warnings as errors" },
                    new() { Flag = "-fsanitize=address",  Description = "AddressSanitizer" },
                    new() { Flag = "-fsanitize=undefined",Description = "UBSan" },
                }},
                new FlagGroup { GroupName = "CODE GENERATION", Flags = new()
                {
                    new() { Flag = "-ffast-math",         Description = "Unsafe FP optimizations" },
                    new() { Flag = "-fno-exceptions",     Description = "Disable exceptions" },
                    new() { Flag = "-fno-rtti",           Description = "Disable RTTI" },
                    new() { Flag = "-fomit-frame-pointer",Description = "Free up RBP" },
                    new() { Flag = "-fvectorize",         Description = "Enable auto-vectorize" },
                    new() { Flag = "-fno-unroll-loops",   Description = "Disable loop unrolling" },
                    new() { Flag = "-flto",               Description = "Link-time optimization" },
                    new() { Flag = "-fprofile-generate",  Description = "Instrument for PGO" },
                }},
            },

            ["g++"] = new()
            {
                new FlagGroup { GroupName = "OPTIMIZATION", Flags = new()
                {
                    new() { Flag = "-O0",                 Description = "No optimization" },
                    new() { Flag = "-O1",                 Description = "Basic optimization" },
                    new() { Flag = "-O2",                 Description = "Moderate optimization" },
                    new() { Flag = "-O3",                 Description = "Aggressive optimization" },
                    new() { Flag = "-Os",                 Description = "Optimize for size" },
                    new() { Flag = "-Ofast",              Description = "O3 + unsafe math" },
                }},
                new FlagGroup { GroupName = "ARCHITECTURE", Flags = new()
                {
                    new() { Flag = "-march=native",       Description = "Tune for current CPU" },
                    new() { Flag = "-march=x86-64",       Description = "Baseline x86-64" },
                    new() { Flag = "-march=x86-64-v3",    Description = "AVX2 baseline" },
                    new() { Flag = "-mavx2",              Description = "Enable AVX2" },
                    new() { Flag = "-mavx512f",           Description = "Enable AVX-512F" },
                    new() { Flag = "-madx",               Description = "Enable ADCX/ADOX" },
                    new() { Flag = "-mbmi2",              Description = "Enable BMI2" },
                    new() { Flag = "-mtune=native",       Description = "Schedule for host CPU" },
                }},
                new FlagGroup { GroupName = "DEBUG / DIAGNOSTICS", Flags = new()
                {
                    new() { Flag = "-g",                  Description = "Debug info (DWARF)" },
                    new() { Flag = "-g3",                 Description = "Full debug info + macros" },
                    new() { Flag = "-Wall",               Description = "Common warnings" },
                    new() { Flag = "-Wextra",             Description = "Extra warnings" },
                    new() { Flag = "-Wpedantic",          Description = "Strict standard conformance" },
                    new() { Flag = "-Werror",             Description = "Warnings as errors" },
                    new() { Flag = "-fsanitize=address",  Description = "AddressSanitizer" },
                    new() { Flag = "-fsanitize=undefined",Description = "UBSan" },
                }},
                new FlagGroup { GroupName = "CODE GENERATION", Flags = new()
                {
                    new() { Flag = "-ffast-math",         Description = "Unsafe FP optimizations" },
                    new() { Flag = "-fno-exceptions",     Description = "Disable exceptions" },
                    new() { Flag = "-fno-rtti",           Description = "Disable RTTI" },
                    new() { Flag = "-fomit-frame-pointer",Description = "Free up RBP" },
                    new() { Flag = "-ftree-vectorize",    Description = "Auto-vectorize loops" },
                    new() { Flag = "-fno-unroll-loops",   Description = "Disable loop unrolling" },
                    new() { Flag = "-flto",               Description = "Link-time optimization" },
                    new() { Flag = "-fprofile-generate",  Description = "Instrument for PGO" },
                }},
            },

            ["cl.exe"] = new()
            {
                new FlagGroup { GroupName = "OPTIMIZATION", Flags = new()
                {
                    new() { Flag = "/Od",                 Description = "No optimization" },
                    new() { Flag = "/O1",                 Description = "Minimize size" },
                    new() { Flag = "/O2",                 Description = "Maximize speed" },
                    new() { Flag = "/Ox",                 Description = "Full optimization" },
                    new() { Flag = "/Os",                 Description = "Favor size" },
                    new() { Flag = "/Ot",                 Description = "Favor speed" },
                    new() { Flag = "/GL",                 Description = "Whole program optimization" },
                }},
                new FlagGroup { GroupName = "ARCHITECTURE", Flags = new()
                {
                    new() { Flag = "/arch:AVX",           Description = "Enable AVX" },
                    new() { Flag = "/arch:AVX2",          Description = "Enable AVX2" },
                    new() { Flag = "/arch:AVX512",        Description = "Enable AVX-512" },
                    new() { Flag = "/favor:INTEL64",      Description = "Tune for Intel 64-bit" },
                    new() { Flag = "/favor:AMD64",        Description = "Tune for AMD 64-bit" },
                }},
                new FlagGroup { GroupName = "DEBUG / DIAGNOSTICS", Flags = new()
                {
                    new() { Flag = "/Zi",                 Description = "Debug info (PDB)" },
                    new() { Flag = "/Z7",                 Description = "Debug info (embedded)" },
                    new() { Flag = "/W3",                 Description = "Warning level 3" },
                    new() { Flag = "/W4",                 Description = "Warning level 4" },
                    new() { Flag = "/Wall",               Description = "All warnings" },
                    new() { Flag = "/WX",                 Description = "Warnings as errors" },
                    new() { Flag = "/fsanitize=address",  Description = "AddressSanitizer" },
                    new() { Flag = "/RTC1",               Description = "Runtime checks" },
                }},
                new FlagGroup { GroupName = "CODE GENERATION", Flags = new()
                {
                    new() { Flag = "/fp:fast",            Description = "Fast floating-point" },
                    new() { Flag = "/fp:strict",          Description = "Strict floating-point" },
                    new() { Flag = "/GS-",                Description = "Disable buffer security check" },
                    new() { Flag = "/GS",                 Description = "Buffer security check" },
                    new() { Flag = "/EHsc",               Description = "C++ exception handling" },
                    new() { Flag = "/EHa",                Description = "SEH + C++ exceptions" },
                    new() { Flag = "/GR-",                Description = "Disable RTTI" },
                    new() { Flag = "/Gy",                 Description = "Function-level linking" },
                    new() { Flag = "/Oi",                 Description = "Intrinsic functions" },
                }},
            },
        };
    }
}