using System.Collections.Generic;

namespace Caldera
{
    // ── Data model ────────────────────────────────────────────────────────────

    public class OpcodeLatency
    {
        public string Uarch { get; init; } = string.Empty;
        public string Latency { get; init; } = "-";   // cycles, or "~N"
        public string Throughput { get; init; } = "-";  // reciprocal throughput
        public string Ports { get; init; } = "";    // e.g. "p0156"
    }

    public class OpcodeForm
    {
        public string Operands { get; init; } = string.Empty;  // "r64, r/m64"
        public string Encoding { get; init; } = string.Empty;  // "RM", "MR", "I", …
        public string OpcodeBytes { get; init; } = string.Empty; // "REX.W 0F AF /r"
    }

    public class OpcodeInfo
    {
        // ── Compact ───────────────────────────────────────────────────────────
        public string Mnemonic { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;  // one-line description
        public string Category { get; init; } = string.Empty;  // ALU / SSE / AVX / …
        public string FlagsRead { get; init; } = "";            // "CF OF"  or "-"
        public string FlagsWritten { get; init; } = "";            // "ZF SF CF OF PF AF"
        public string RepLatency { get; init; } = "";            // "3 / 1" (lat/tput, Zen4)
        public string RepUarch { get; init; } = "Zen 4";       // which µarch the hint is for

        // ── Expanded ─────────────────────────────────────────────────────────
        public string Description { get; init; } = string.Empty;  // multi-sentence detail
        public string ExceptionClass { get; init; } = "";           // "#GP, #PF, …" or "None"
        public List<OpcodeForm> Forms { get; init; } = new();
        public List<OpcodeLatency> Latencies { get; init; } = new();
    }

    // ── Database ──────────────────────────────────────────────────────────────

    public static class OpcodeDb
    {
        /// <summary>
        /// Look up an opcode by mnemonic (case-insensitive).
        /// Strips known prefixes (REP/REPE/REPNE/LOCK/XACQUIRE/XRELEASE)
        /// and VEX/EVEX size suffixes are normalised before lookup.
        /// Returns null if the mnemonic is not in the database.
        /// </summary>
        public static OpcodeInfo? Lookup(string mnemonic)
        {
            if (string.IsNullOrWhiteSpace(mnemonic)) return null;
            var key = Normalize(mnemonic);
            return _db.TryGetValue(key, out var info) ? info : null;
        }

        private static string Normalize(string m)
        {
            m = m.Trim().ToUpperInvariant();
            // Strip mandatory prefixes that appear as separate tokens sometimes
            foreach (var pfx in new[] { "REPNE ", "REPE ", "REP ", "LOCK ", "XACQUIRE ", "XRELEASE " })
                if (m.StartsWith(pfx)) m = m[pfx.Length..];
            return m;
        }

        // ── The table ─────────────────────────────────────────────────────────

        private static readonly Dictionary<string, OpcodeInfo> _db =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                // ── Data transfer ─────────────────────────────────────────────────

                ["MOV"] = new()
                {
                    Mnemonic = "MOV",
                    Summary = "Move — copy source to destination",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "1 / 0.25",
                    RepUarch = "Zen 4",
                    Description = "Copies the value of the source operand to the destination operand. " +
                              "Does not affect flags. MOV to/from segment registers or CR/DR registers " +
                              "behave differently and may serialize the pipeline.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r/m64, r64",   Encoding = "MR", OpcodeBytes = "REX.W 89 /r" },
                    new() { Operands = "r64, r/m64",   Encoding = "RM", OpcodeBytes = "REX.W 8B /r" },
                    new() { Operands = "r64, imm64",   Encoding = "OI", OpcodeBytes = "REX.W B8+rd io" },
                    new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W C7 /0 id" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",       Latency = "1",  Throughput = "0.25", Ports = "p0123" },
                    new() { Uarch = "Skylake",     Latency = "1",  Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Alder Lake",  Latency = "1",  Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Sapphire Rapids", Latency = "1", Throughput = "0.25", Ports = "p0156" },
                },
                },

                ["MOVSX"] = new()
                {
                    Mnemonic = "MOVSX",
                    Summary = "Move with sign extension",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "1 / 0.25",
                    RepUarch = "Zen 4",
                    Description = "Copies the source operand into the destination, sign-extending the value " +
                              "to fill the wider destination register.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r64, r/m32", Encoding = "RM", OpcodeBytes = "REX.W 63 /r" },
                    new() { Operands = "r32, r/m16", Encoding = "RM", OpcodeBytes = "0F BF /r" },
                    new() { Operands = "r32, r/m8",  Encoding = "RM", OpcodeBytes = "0F BE /r" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "1", Throughput = "0.25", Ports = "p0123" },
                    new() { Uarch = "Skylake",    Latency = "1", Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Alder Lake", Latency = "1", Throughput = "0.25", Ports = "p0156" },
                },
                },

                ["MOVSXD"] = new()
                {
                    Mnemonic = "MOVSXD",
                    Summary = "Move doubleword to quadword with sign extension",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "1 / 0.25",
                    RepUarch = "Zen 4",
                    Description = "Sign-extends a 32-bit register or memory operand into a 64-bit register. " +
                              "Commonly emitted by compilers to widen int to long.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new() { new() { Operands = "r64, r/m32", Encoding = "RM", OpcodeBytes = "REX.W 63 /r" } },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "1", Throughput = "0.25", Ports = "p0123" },
                    new() { Uarch = "Skylake",    Latency = "1", Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Alder Lake", Latency = "1", Throughput = "0.25", Ports = "p0156" },
                },
                },

                ["MOVZX"] = new()
                {
                    Mnemonic = "MOVZX",
                    Summary = "Move with zero extension",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "1 / 0.25",
                    RepUarch = "Zen 4",
                    Description = "Copies the source operand into the destination, zero-extending to fill " +
                              "the wider destination. On x86-64, a 32-bit write already zero-extends " +
                              "to 64 bits, so MOVZX r32,r/m8 is common.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r32, r/m8",  Encoding = "RM", OpcodeBytes = "0F B6 /r" },
                    new() { Operands = "r32, r/m16", Encoding = "RM", OpcodeBytes = "0F B7 /r" },
                    new() { Operands = "r64, r/m8",  Encoding = "RM", OpcodeBytes = "REX.W 0F B6 /r" },
                    new() { Operands = "r64, r/m16", Encoding = "RM", OpcodeBytes = "REX.W 0F B7 /r" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "1", Throughput = "0.25", Ports = "p0123" },
                    new() { Uarch = "Skylake",    Latency = "1", Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Alder Lake", Latency = "1", Throughput = "0.25", Ports = "p0156" },
                },
                },

                ["XCHG"] = new()
                {
                    Mnemonic = "XCHG",
                    Summary = "Exchange — atomically swap two operands",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "2 / 1",
                    RepUarch = "Zen 4",
                    Description = "Swaps the values of two operands. When one operand is a memory location, " +
                              "XCHG carries an implicit LOCK prefix, making it fully atomic. " +
                              "Commonly used for spinlocks.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 87 /r" },
                    new() { Operands = "rAX, r64",   Encoding = "O",  OpcodeBytes = "REX.W 90+rd" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "2",  Throughput = "1",   Ports = "p0" },
                    new() { Uarch = "Skylake",    Latency = "2",  Throughput = "1",   Ports = "p0" },
                    new() { Uarch = "Alder Lake", Latency = "2",  Throughput = "1",   Ports = "p06" },
                },
                },

                ["LEA"] = new()
                {
                    Mnemonic = "LEA",
                    Summary = "Load effective address — compute address into register",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "1 / 0.25",
                    RepUarch = "Zen 4",
                    Description = "Calculates the effective address of the memory operand and stores it in " +
                              "the destination register without accessing memory. Compilers use LEA " +
                              "heavily as a cheap multiply-and-add (e.g. lea rax,[rax+rax*4] = rax*5).",
                    ExceptionClass = "#UD (if memory operand is not a memory reference)",
                    Forms = new()
                {
                    new() { Operands = "r64, m", Encoding = "RM", OpcodeBytes = "REX.W 8D /r" },
                    new() { Operands = "r32, m", Encoding = "RM", OpcodeBytes = "8D /r" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",         Latency = "1", Throughput = "0.25", Ports = "p0123" },
                    new() { Uarch = "Skylake",        Latency = "1", Throughput = "0.25", Ports = "p15"   },
                    new() { Uarch = "Alder Lake",     Latency = "1", Throughput = "0.25", Ports = "p15"   },
                    new() { Uarch = "Sapphire Rapids",Latency = "1", Throughput = "0.25", Ports = "p15"   },
                },
                },

                ["PUSH"] = new()
                {
                    Mnemonic = "PUSH",
                    Summary = "Push value onto the stack",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "3 / 1",
                    RepUarch = "Zen 4",
                    Description = "Decrements RSP by the operand size (8 bytes in 64-bit mode) then stores " +
                              "the source value at [RSP]. Memory-form PUSH is a micro-fused load+store.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "FF /6" },
                    new() { Operands = "imm32", Encoding = "I", OpcodeBytes = "68 id"  },
                    new() { Operands = "imm8",  Encoding = "I", OpcodeBytes = "6A ib"  },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "3", Throughput = "1", Ports = "p4+p2" },
                    new() { Uarch = "Skylake",    Latency = "3", Throughput = "1", Ports = "p237"  },
                    new() { Uarch = "Alder Lake", Latency = "3", Throughput = "1", Ports = "p237"  },
                },
                },

                ["POP"] = new()
                {
                    Mnemonic = "POP",
                    Summary = "Pop value from the stack",
                    Category = "Data Transfer",
                    FlagsRead = "-",
                    FlagsWritten = "-",
                    RepLatency = "4 / 1",
                    RepUarch = "Zen 4",
                    Description = "Loads the value at [RSP] into the destination then increments RSP by 8. " +
                              "POPs that feed the address of the next load can cause store-forwarding stalls.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r64",   Encoding = "O", OpcodeBytes = "58+rd" },
                    new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "8F /0" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "4", Throughput = "1", Ports = "p0+p3" },
                    new() { Uarch = "Skylake",    Latency = "4", Throughput = "1", Ports = "p06+p23" },
                    new() { Uarch = "Alder Lake", Latency = "4", Throughput = "1", Ports = "p06+p23" },
                },
                },

                ["CMOVZ"] = new() { Mnemonic = "CMOVZ", Summary = "Conditional move if Zero (ZF=1)", Category = "Data Transfer", FlagsRead = "ZF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination only if the Zero Flag is set. All CMOVcc instructions read flags but never write them.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 44 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVNZ"] = new() { Mnemonic = "CMOVNZ", Summary = "Conditional move if Not Zero (ZF=0)", Category = "Data Transfer", FlagsRead = "ZF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination only if the Zero Flag is clear.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 45 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVS"] = new() { Mnemonic = "CMOVS", Summary = "Conditional move if Sign (SF=1)", Category = "Data Transfer", FlagsRead = "SF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination only if the Sign Flag is set.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 48 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVNS"] = new() { Mnemonic = "CMOVNS", Summary = "Conditional move if Not Sign (SF=0)", Category = "Data Transfer", FlagsRead = "SF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination only if the Sign Flag is clear.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 49 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVL"] = new() { Mnemonic = "CMOVL", Summary = "Conditional move if Less (SF≠OF)", Category = "Data Transfer", FlagsRead = "SF OF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination if signed less-than (SF≠OF).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 4C /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVGE"] = new() { Mnemonic = "CMOVGE", Summary = "Conditional move if Greater/Equal (SF=OF)", Category = "Data Transfer", FlagsRead = "SF OF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination if signed greater-than-or-equal (SF=OF).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 4D /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVC"] = new() { Mnemonic = "CMOVC", Summary = "Conditional move if Carry (CF=1)", Category = "Data Transfer", FlagsRead = "CF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination only if the Carry Flag is set. Alias: CMOVB / CMOVNAE.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 42 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVNC"] = new() { Mnemonic = "CMOVNC", Summary = "Conditional move if Not Carry (CF=0)", Category = "Data Transfer", FlagsRead = "CF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination only if the Carry Flag is clear. Alias: CMOVAE / CMOVNB.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 43 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVA"] = new() { Mnemonic = "CMOVA", Summary = "Conditional move if Above (CF=0 and ZF=0)", Category = "Data Transfer", FlagsRead = "CF ZF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination if unsigned above (CF=0 and ZF=0).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 47 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVBE"] = new() { Mnemonic = "CMOVBE", Summary = "Conditional move if Below/Equal (CF=1 or ZF=1)", Category = "Data Transfer", FlagsRead = "CF ZF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination if unsigned below-or-equal (CF=1 or ZF=1).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 46 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVG"] = new() { Mnemonic = "CMOVG", Summary = "Conditional move if Greater (ZF=0 and SF=OF)", Category = "Data Transfer", FlagsRead = "ZF SF OF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination if signed greater-than (ZF=0 and SF=OF).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 4F /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["CMOVLE"] = new() { Mnemonic = "CMOVLE", Summary = "Conditional move if Less/Equal (ZF=1 or SF≠OF)", Category = "Data Transfer", FlagsRead = "ZF SF OF", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Copies source to destination if signed less-than-or-equal (ZF=1 or SF≠OF).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F 4E /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },

                // ── Integer ALU ───────────────────────────────────────────────────

                ["ADD"] = new()
                {
                    Mnemonic = "ADD",
                    Summary = "Integer addition",
                    Category = "Integer ALU",
                    FlagsRead = "-",
                    FlagsWritten = "CF OF SF ZF AF PF",
                    RepLatency = "1 / 0.25",
                    RepUarch = "Zen 4",
                    Description = "Adds source to destination and writes the result to destination. " +
                              "All arithmetic flags are updated. CF and OF indicate unsigned and signed " +
                              "overflow respectively.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r/m64, r64",   Encoding = "MR", OpcodeBytes = "REX.W 01 /r" },
                    new() { Operands = "r64, r/m64",   Encoding = "RM", OpcodeBytes = "REX.W 03 /r" },
                    new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W 81 /0 id" },
                    new() { Operands = "r/m64, imm8",  Encoding = "MI", OpcodeBytes = "REX.W 83 /0 ib" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",         Latency = "1", Throughput = "0.25", Ports = "p0123" },
                    new() { Uarch = "Skylake",        Latency = "1", Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Alder Lake",     Latency = "1", Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Sapphire Rapids",Latency = "1", Throughput = "0.25", Ports = "p0156" },
                },
                },

                ["SUB"] = new()
                {
                    Mnemonic = "SUB",
                    Summary = "Integer subtraction",
                    Category = "Integer ALU",
                    FlagsRead = "-",
                    FlagsWritten = "CF OF SF ZF AF PF",
                    RepLatency = "1 / 0.25",
                    RepUarch = "Zen 4",
                    Description = "Subtracts source from destination. All arithmetic flags are updated. " +
                              "CF is set if an unsigned borrow occurred; OF if signed overflow.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r/m64, r64",   Encoding = "MR", OpcodeBytes = "REX.W 29 /r" },
                    new() { Operands = "r64, r/m64",   Encoding = "RM", OpcodeBytes = "REX.W 2B /r" },
                    new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W 81 /5 id" },
                    new() { Operands = "r/m64, imm8",  Encoding = "MI", OpcodeBytes = "REX.W 83 /5 ib" },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "1", Throughput = "0.25", Ports = "p0123" },
                    new() { Uarch = "Skylake",    Latency = "1", Throughput = "0.25", Ports = "p0156" },
                    new() { Uarch = "Alder Lake", Latency = "1", Throughput = "0.25", Ports = "p0156" },
                },
                },

                ["IMUL"] = new()
                {
                    Mnemonic = "IMUL",
                    Summary = "Signed integer multiply",
                    Category = "Integer ALU",
                    FlagsRead = "-",
                    FlagsWritten = "CF OF (SF ZF AF PF undefined)",
                    RepLatency = "3 / 1",
                    RepUarch = "Zen 4",
                    Description = "Multiplies two signed integers. The two- and three-operand forms truncate " +
                              "to the destination width; CF and OF are set if the result was truncated. " +
                              "The one-operand form produces a double-width result in RDX:RAX.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new()
                {
                    new() { Operands = "r64, r/m64",        Encoding = "RM",  OpcodeBytes = "REX.W 0F AF /r"    },
                    new() { Operands = "r64, r/m64, imm32", Encoding = "RMI", OpcodeBytes = "REX.W 69 /r id"    },
                    new() { Operands = "r64, r/m64, imm8",  Encoding = "RMI", OpcodeBytes = "REX.W 6B /r ib"    },
                    new() { Operands = "r/m64 (→ RDX:RAX)", Encoding = "M",   OpcodeBytes = "REX.W F7 /5"       },
                },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",         Latency = "3", Throughput = "1",   Ports = "p0" },
                    new() { Uarch = "Skylake",        Latency = "3", Throughput = "1",   Ports = "p1" },
                    new() { Uarch = "Alder Lake",     Latency = "3", Throughput = "1",   Ports = "p1" },
                    new() { Uarch = "Sapphire Rapids",Latency = "3", Throughput = "0.5", Ports = "p1" },
                },
                },

                ["MUL"] = new()
                {
                    Mnemonic = "MUL",
                    Summary = "Unsigned integer multiply (RDX:RAX ← RAX × src)",
                    Category = "Integer ALU",
                    FlagsRead = "-",
                    FlagsWritten = "CF OF (SF ZF AF PF undefined)",
                    RepLatency = "3 / 1",
                    RepUarch = "Zen 4",
                    Description = "Multiplies the unsigned source operand by RAX (or the appropriate " +
                              "accumulator size) and stores the double-width result in RDX:RAX. " +
                              "CF and OF are set if the upper half is non-zero.",
                    ExceptionClass = "#GP, #SS, #PF, #AC",
                    Forms = new() { new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "REX.W F7 /4" } },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "3", Throughput = "1", Ports = "p0" },
                    new() { Uarch = "Skylake",    Latency = "3", Throughput = "1", Ports = "p1" },
                    new() { Uarch = "Alder Lake", Latency = "3", Throughput = "1", Ports = "p1" },
                },
                },

                ["IDIV"] = new()
                {
                    Mnemonic = "IDIV",
                    Summary = "Signed integer divide (RDX:RAX ÷ src)",
                    Category = "Integer ALU",
                    FlagsRead = "-",
                    FlagsWritten = "CF OF SF ZF AF PF (all undefined)",
                    RepLatency = "~41 / ~41",
                    RepUarch = "Zen 4",
                    Description = "Divides the signed double-width value in RDX:RAX by the source. " +
                              "Quotient → RAX, remainder → RDX. Raises #DE if the divisor is zero " +
                              "or the quotient overflows. Division is microcoded and very slow.",
                    ExceptionClass = "#DE, #GP, #SS, #PF, #AC",
                    Forms = new() { new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "REX.W F7 /7" } },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "~41", Throughput = "~41", Ports = "p0" },
                    new() { Uarch = "Skylake",    Latency = "~35", Throughput = "~21", Ports = "p0" },
                    new() { Uarch = "Alder Lake", Latency = "~35", Throughput = "~21", Ports = "p0" },
                },
                },

                ["DIV"] = new()
                {
                    Mnemonic = "DIV",
                    Summary = "Unsigned integer divide (RDX:RAX ÷ src)",
                    Category = "Integer ALU",
                    FlagsRead = "-",
                    FlagsWritten = "CF OF SF ZF AF PF (all undefined)",
                    RepLatency = "~41 / ~41",
                    RepUarch = "Zen 4",
                    Description = "Divides the unsigned double-width value in RDX:RAX by the source. " +
                              "Quotient → RAX, remainder → RDX. Raises #DE on divide-by-zero or overflow. " +
                              "Division is microcoded and extremely slow — prefer reciprocal multiply.",
                    ExceptionClass = "#DE, #GP, #SS, #PF, #AC",
                    Forms = new() { new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "REX.W F7 /6" } },
                    Latencies = new()
                {
                    new() { Uarch = "Zen 4",      Latency = "~41", Throughput = "~41", Ports = "p0" },
                    new() { Uarch = "Skylake",    Latency = "~35", Throughput = "~21", Ports = "p0" },
                    new() { Uarch = "Alder Lake", Latency = "~35", Throughput = "~21", Ports = "p0" },
                },
                },

                ["INC"] = new() { Mnemonic = "INC", Summary = "Increment by 1", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "OF SF ZF AF PF", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Adds 1 to the operand. Unlike ADD, INC does not modify CF — useful for loop counters where carry state must be preserved.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "REX.W FF /0" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },
                ["DEC"] = new() { Mnemonic = "DEC", Summary = "Decrement by 1", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "OF SF ZF AF PF", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Subtracts 1 from the operand. Does not modify CF. Common in loop countdown patterns.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "REX.W FF /1" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },
                ["NEG"] = new() { Mnemonic = "NEG", Summary = "Two's complement negation", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "CF OF SF ZF AF PF", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Replaces the operand with its two's complement (0 − operand). CF is set unless the operand was zero.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "REX.W F7 /3" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },
                ["NOT"] = new() { Mnemonic = "NOT", Summary = "Bitwise NOT (one's complement)", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Flips all bits of the operand. Does not affect any flags.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "REX.W F7 /2" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },

                ["AND"] = new() { Mnemonic = "AND", Summary = "Bitwise AND", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "SF ZF PF (CF=OF=0, AF=undef)", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Performs a bitwise AND and writes the result to the destination. CF and OF are cleared; AF is undefined.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, r64", Encoding = "MR", OpcodeBytes = "REX.W 21 /r" }, new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 23 /r" }, new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W 81 /4 id" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },
                ["OR"] = new() { Mnemonic = "OR", Summary = "Bitwise OR", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "SF ZF PF (CF=OF=0, AF=undef)", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Performs a bitwise OR. CF and OF are cleared; AF is undefined.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, r64", Encoding = "MR", OpcodeBytes = "REX.W 09 /r" }, new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0B /r" }, new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W 81 /1 id" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },
                ["XOR"] = new() { Mnemonic = "XOR", Summary = "Bitwise XOR — also idiom to zero a register", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "SF ZF PF (CF=OF=0, AF=undef)", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Performs a bitwise exclusive-OR. XOR r,r is the canonical register-zeroing idiom on x86-64 — it is smaller and faster than MOV r,0 and breaks the dependency on the old value on most µarchs.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, r64", Encoding = "MR", OpcodeBytes = "REX.W 31 /r" }, new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 33 /r" }, new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W 81 /6 id" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },

                ["CMP"] = new() { Mnemonic = "CMP", Summary = "Compare — subtract without storing result", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "CF OF SF ZF AF PF", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "Subtracts the source from the destination and sets flags, but discards the result. Used to set up conditional jumps and CMOVcc.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, r64", Encoding = "MR", OpcodeBytes = "REX.W 39 /r" }, new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 3B /r" }, new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W 81 /7 id" }, new() { Operands = "r/m64, imm8", Encoding = "MI", OpcodeBytes = "REX.W 83 /7 ib" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },
                ["TEST"] = new() { Mnemonic = "TEST", Summary = "Logical compare — AND without storing result", Category = "Integer ALU", FlagsRead = "-", FlagsWritten = "SF ZF PF (CF=OF=0, AF=undef)", RepLatency = "1 / 0.25", RepUarch = "Zen 4", Description = "ANDs the two operands and sets flags without storing the result. TEST rax,rax is the idiomatic way to check if a register is zero.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, r64", Encoding = "MR", OpcodeBytes = "REX.W 85 /r" }, new() { Operands = "r/m64, imm32", Encoding = "MI", OpcodeBytes = "REX.W F7 /0 id" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.25", Ports = "p0123" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.25", Ports = "p0156" } } },

                // ── Shifts & rotates ──────────────────────────────────────────────

                ["SHL"] = new() { Mnemonic = "SHL", Summary = "Shift logical left", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF OF SF ZF PF (AF=undef)", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Shifts bits left by the count operand, filling low bits with 0. Equivalent to unsigned multiply by 2^count. CF receives the last bit shifted out.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, imm8", Encoding = "MI", OpcodeBytes = "REX.W C1 /4 ib" }, new() { Operands = "r/m64, CL", Encoding = "MC", OpcodeBytes = "REX.W D3 /4" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["SHR"] = new() { Mnemonic = "SHR", Summary = "Shift logical right (unsigned)", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF OF SF ZF PF (AF=undef)", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Shifts bits right, filling high bits with 0. Equivalent to unsigned divide by 2^count.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, imm8", Encoding = "MI", OpcodeBytes = "REX.W C1 /5 ib" }, new() { Operands = "r/m64, CL", Encoding = "MC", OpcodeBytes = "REX.W D3 /5" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["SAR"] = new() { Mnemonic = "SAR", Summary = "Shift arithmetic right (signed)", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF OF SF ZF PF (AF=undef)", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Shifts bits right, filling high bits with the sign bit. Equivalent to signed divide by 2^count (rounds toward negative infinity).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, imm8", Encoding = "MI", OpcodeBytes = "REX.W C1 /7 ib" }, new() { Operands = "r/m64, CL", Encoding = "MC", OpcodeBytes = "REX.W D3 /7" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["SAL"] = new() { Mnemonic = "SAL", Summary = "Shift arithmetic left (alias for SHL)", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF OF SF ZF PF (AF=undef)", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Identical to SHL. Assemblers may emit SAL for signed shift-left contexts.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, imm8", Encoding = "MI", OpcodeBytes = "REX.W C1 /4 ib" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["ROL"] = new() { Mnemonic = "ROL", Summary = "Rotate left", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF OF", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Rotates bits left — the high bit wraps into the low bit and into CF.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, imm8", Encoding = "MI", OpcodeBytes = "REX.W C1 /0 ib" }, new() { Operands = "r/m64, CL", Encoding = "MC", OpcodeBytes = "REX.W D3 /0" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["ROR"] = new() { Mnemonic = "ROR", Summary = "Rotate right", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF OF", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Rotates bits right — the low bit wraps into the high bit and into CF.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, imm8", Encoding = "MI", OpcodeBytes = "REX.W C1 /1 ib" }, new() { Operands = "r/m64, CL", Encoding = "MC", OpcodeBytes = "REX.W D3 /1" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["SHLD"] = new() { Mnemonic = "SHLD", Summary = "Double precision shift left", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF SF ZF PF (OF/AF=undef)", RepLatency = "3 / 1", RepUarch = "Zen 4", Description = "Shifts dst left by count, filling low bits from the high bits of src. Used for multi-word shifts.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, r64, imm8", Encoding = "MRI", OpcodeBytes = "REX.W 0F A4 /r ib" }, new() { Operands = "r/m64, r64, CL", Encoding = "MRC", OpcodeBytes = "REX.W 0F A5 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "3", Throughput = "1", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "1", Ports = "p06" } } },
                ["SHRD"] = new() { Mnemonic = "SHRD", Summary = "Double precision shift right", Category = "Shift / Rotate", FlagsRead = "-", FlagsWritten = "CF SF ZF PF (OF/AF=undef)", RepLatency = "3 / 1", RepUarch = "Zen 4", Description = "Shifts dst right by count, filling high bits from the low bits of src.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m64, r64, imm8", Encoding = "MRI", OpcodeBytes = "REX.W 0F AC /r ib" }, new() { Operands = "r/m64, r64, CL", Encoding = "MRC", OpcodeBytes = "REX.W 0F AD /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "3", Throughput = "1", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "1", Ports = "p06" } } },

                // ── Control flow ──────────────────────────────────────────────────

                ["JMP"] = new() { Mnemonic = "JMP", Summary = "Unconditional jump", Category = "Control Flow", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Transfers execution to the target address unconditionally. Near direct jumps are predicted by the BTB; indirect jumps (JMP r/m) use the indirect branch predictor.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "E9 cd" }, new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "FF /4" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "1", Ports = "p06" } } },
                ["JE"] = new() { Mnemonic = "JE", Summary = "Jump if Equal / Zero (ZF=1)", Category = "Control Flow", FlagsRead = "ZF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if the Zero Flag is set. Predicted by the conditional branch predictor.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 84 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JNE"] = new() { Mnemonic = "JNE", Summary = "Jump if Not Equal / Not Zero (ZF=0)", Category = "Control Flow", FlagsRead = "ZF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if the Zero Flag is clear.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 85 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JL"] = new() { Mnemonic = "JL", Summary = "Jump if Less — signed (SF≠OF)", Category = "Control Flow", FlagsRead = "SF OF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if signed less-than (SF≠OF).", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 8C cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JGE"] = new() { Mnemonic = "JGE", Summary = "Jump if Greater/Equal — signed (SF=OF)", Category = "Control Flow", FlagsRead = "SF OF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if signed greater-than-or-equal (SF=OF).", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 8D cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JG"] = new() { Mnemonic = "JG", Summary = "Jump if Greater — signed (ZF=0 and SF=OF)", Category = "Control Flow", FlagsRead = "ZF SF OF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if signed greater-than.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 8F cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JLE"] = new() { Mnemonic = "JLE", Summary = "Jump if Less/Equal — signed (ZF=1 or SF≠OF)", Category = "Control Flow", FlagsRead = "ZF SF OF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if signed less-than-or-equal.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 8E cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JA"] = new() { Mnemonic = "JA", Summary = "Jump if Above — unsigned (CF=0 and ZF=0)", Category = "Control Flow", FlagsRead = "CF ZF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if unsigned above.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 87 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JBE"] = new() { Mnemonic = "JBE", Summary = "Jump if Below/Equal — unsigned (CF=1 or ZF=1)", Category = "Control Flow", FlagsRead = "CF ZF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if unsigned below-or-equal.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 86 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JB"] = new() { Mnemonic = "JB", Summary = "Jump if Below — unsigned (CF=1)", Category = "Control Flow", FlagsRead = "CF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if unsigned below (CF=1). Alias: JC / JNAE.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 82 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JAE"] = new() { Mnemonic = "JAE", Summary = "Jump if Above/Equal — unsigned (CF=0)", Category = "Control Flow", FlagsRead = "CF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if unsigned above-or-equal (CF=0). Alias: JNC / JNB.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 83 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JS"] = new() { Mnemonic = "JS", Summary = "Jump if Sign (SF=1)", Category = "Control Flow", FlagsRead = "SF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if the Sign Flag is set.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 88 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JNS"] = new() { Mnemonic = "JNS", Summary = "Jump if Not Sign (SF=0)", Category = "Control Flow", FlagsRead = "SF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if the Sign Flag is clear.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 89 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JO"] = new() { Mnemonic = "JO", Summary = "Jump if Overflow (OF=1)", Category = "Control Flow", FlagsRead = "OF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if the Overflow Flag is set.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 80 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JNO"] = new() { Mnemonic = "JNO", Summary = "Jump if Not Overflow (OF=0)", Category = "Control Flow", FlagsRead = "OF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if the Overflow Flag is clear.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 81 cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["JP"] = new() { Mnemonic = "JP", Summary = "Jump if Parity (PF=1)", Category = "Control Flow", FlagsRead = "PF", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Branches if the Parity Flag is set (even number of set bits in low byte of result).", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "0F 8A cd" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["LOOP"] = new() { Mnemonic = "LOOP", Summary = "Decrement RCX, jump if RCX≠0", Category = "Control Flow", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 4", RepUarch = "Zen 4", Description = "Decrements RCX (or ECX in 32-bit mode) and branches if not zero. Slow on modern µarchs — prefer DEC+JNZ.", ExceptionClass = "#GP", Forms = new() { new() { Operands = "rel8", Encoding = "D", OpcodeBytes = "E2 cb" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "4", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "7", Throughput = "5", Ports = "p06" } } },

                ["CALL"] = new() { Mnemonic = "CALL", Summary = "Call procedure — push RIP then jump", Category = "Control Flow", FlagsRead = "-", FlagsWritten = "-", RepLatency = "3 / 1", RepUarch = "Zen 4", Description = "Pushes the return address (next instruction) onto the stack then jumps to the target. The Return Stack Buffer (RSB) predicts the matching RET.", ExceptionClass = "#GP, #SS, #PF", Forms = new() { new() { Operands = "rel32", Encoding = "D", OpcodeBytes = "E8 cd" }, new() { Operands = "r/m64", Encoding = "M", OpcodeBytes = "FF /2" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "3", Throughput = "1", Ports = "p0+p4+p2" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p06+p23" } } },
                ["RET"] = new() { Mnemonic = "RET", Summary = "Return from procedure — pop RIP", Category = "Control Flow", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 1", RepUarch = "Zen 4", Description = "Pops the return address from the stack into RIP. Predicted by the Return Stack Buffer matching the corresponding CALL.", ExceptionClass = "#GP, #SS, #PF", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "C3" }, new() { Operands = "imm16", Encoding = "I", OpcodeBytes = "C2 iw" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "1", Ports = "p0+p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "1", Ports = "p06+p23" } } },

                // ── Set byte on condition ─────────────────────────────────────────
                ["SETE"] = new() { Mnemonic = "SETE", Summary = "Set byte if Equal (ZF=1)", Category = "Integer ALU", FlagsRead = "ZF", FlagsWritten = "-", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Sets the destination byte to 1 if ZF=1, else 0. Useful for branchless boolean conversion.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m8", Encoding = "M", OpcodeBytes = "0F 94 /0" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["SETNE"] = new() { Mnemonic = "SETNE", Summary = "Set byte if Not Equal (ZF=0)", Category = "Integer ALU", FlagsRead = "ZF", FlagsWritten = "-", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Sets the destination byte to 1 if ZF=0.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m8", Encoding = "M", OpcodeBytes = "0F 95 /0" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["SETL"] = new() { Mnemonic = "SETL", Summary = "Set byte if Less — signed (SF≠OF)", Category = "Integer ALU", FlagsRead = "SF OF", FlagsWritten = "-", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Sets byte to 1 if signed less-than.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m8", Encoding = "M", OpcodeBytes = "0F 9C /0" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["SETGE"] = new() { Mnemonic = "SETGE", Summary = "Set byte if Greater/Equal — signed", Category = "Integer ALU", FlagsRead = "SF OF", FlagsWritten = "-", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Sets byte to 1 if signed greater-than-or-equal.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r/m8", Encoding = "M", OpcodeBytes = "0F 9D /0" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },

                // ── Memory / load-store ───────────────────────────────────────────

                ["MOVAPS"] = new() { Mnemonic = "MOVAPS", Summary = "Move aligned packed single-precision FP (XMM)", Category = "SSE", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Moves 128 bits of aligned packed floats between XMM registers or between XMM and 16-byte aligned memory. Raises #GP if memory is not 16-byte aligned.", ExceptionClass = "#GP (alignment), #SS, #PF, #UD", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "0F 28 /r" }, new() { Operands = "xmm1/m128, xmm2", Encoding = "MR", OpcodeBytes = "0F 29 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },
                ["MOVUPS"] = new() { Mnemonic = "MOVUPS", Summary = "Move unaligned packed single-precision FP (XMM)", Category = "SSE", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Like MOVAPS but tolerates unaligned memory. On modern µarchs the performance penalty for unaligned loads is negligible unless the access crosses a cache-line boundary.", ExceptionClass = "#GP, #SS, #PF, #UD", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "0F 10 /r" }, new() { Operands = "xmm1/m128, xmm2", Encoding = "MR", OpcodeBytes = "0F 11 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },
                ["MOVDQU"] = new() { Mnemonic = "MOVDQU", Summary = "Move unaligned double quadword (XMM)", Category = "SSE2", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Moves 128 bits of integer data without alignment requirement. Prefer VMOVDQU (VEX) or VMOVDQU32/64 (EVEX) in modern code.", ExceptionClass = "#GP, #SS, #PF, #UD", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "F3 0F 6F /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },
                ["MOVDQA"] = new() { Mnemonic = "MOVDQA", Summary = "Move aligned double quadword (XMM)", Category = "SSE2", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Moves 128 bits of integer data, requiring 16-byte alignment for memory operands.", ExceptionClass = "#GP (alignment), #SS, #PF, #UD", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "66 0F 6F /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },

                // ── SSE / AVX arithmetic ──────────────────────────────────────────

                ["ADDPS"] = new() { Mnemonic = "ADDPS", Summary = "Add packed single-precision FP", Category = "SSE", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 0.5", RepUarch = "Zen 4", Description = "Adds four packed 32-bit floats in an XMM register.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "0F 58 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "0.5", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "0.5", Ports = "p01" } } },
                ["ADDPD"] = new() { Mnemonic = "ADDPD", Summary = "Add packed double-precision FP", Category = "SSE2", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 0.5", RepUarch = "Zen 4", Description = "Adds two packed 64-bit doubles in an XMM register.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "66 0F 58 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "0.5", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "0.5", Ports = "p01" } } },
                ["MULPS"] = new() { Mnemonic = "MULPS", Summary = "Multiply packed single-precision FP", Category = "SSE", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 0.5", RepUarch = "Zen 4", Description = "Multiplies four packed 32-bit floats.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "0F 59 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "0.5", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "0.5", Ports = "p01" } } },
                ["MULPD"] = new() { Mnemonic = "MULPD", Summary = "Multiply packed double-precision FP", Category = "SSE2", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 0.5", RepUarch = "Zen 4", Description = "Multiplies two packed 64-bit doubles.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "66 0F 59 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "0.5", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "0.5", Ports = "p01" } } },
                ["DIVPS"] = new() { Mnemonic = "DIVPS", Summary = "Divide packed single-precision FP", Category = "SSE", FlagsRead = "-", FlagsWritten = "-", RepLatency = "11 / 5", RepUarch = "Zen 4", Description = "Divides four packed 32-bit floats. Division is significantly slower than multiply — consider RCPPS + Newton-Raphson refinement for performance-sensitive code.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "0F 5E /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "11", Throughput = "5", Ports = "p3" }, new() { Uarch = "Skylake", Latency = "11", Throughput = "3", Ports = "p0" } } },
                ["SQRTPS"] = new() { Mnemonic = "SQRTPS", Summary = "Square root of packed single FP", Category = "SSE", FlagsRead = "-", FlagsWritten = "-", RepLatency = "13 / 5", RepUarch = "Zen 4", Description = "Computes the square root of four packed 32-bit floats. Slow — consider RSQRTPS + one Newton-Raphson step for approximate reciprocal square root.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "0F 51 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "13", Throughput = "5", Ports = "p3" }, new() { Uarch = "Skylake", Latency = "13", Throughput = "7", Ports = "p0" } } },

                ["VADDPS"] = new() { Mnemonic = "VADDPS", Summary = "AVX: Add packed single FP (128/256-bit)", Category = "AVX", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 0.5", RepUarch = "Zen 4", Description = "VEX-encoded packed float addition, non-destructive three-operand form. 256-bit version operates on eight floats simultaneously.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "ymm1, ymm2, ymm3/m256", Encoding = "RVM", OpcodeBytes = "VEX.256.0F.W0 58 /r" }, new() { Operands = "xmm1, xmm2, xmm3/m128", Encoding = "RVM", OpcodeBytes = "VEX.128.0F.W0 58 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "0.5", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "0.5", Ports = "p01" } } },
                ["VMULPS"] = new() { Mnemonic = "VMULPS", Summary = "AVX: Multiply packed single FP", Category = "AVX", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 0.5", RepUarch = "Zen 4", Description = "VEX-encoded packed float multiply, three-operand non-destructive form.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "ymm1, ymm2, ymm3/m256", Encoding = "RVM", OpcodeBytes = "VEX.256.0F.W0 59 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "0.5", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "0.5", Ports = "p01" } } },
                ["VFMADD231PS"] = new() { Mnemonic = "VFMADD231PS", Summary = "AVX-512/FMA: Fused multiply-add (dst += src1 * src2)", Category = "AVX / FMA", FlagsRead = "-", FlagsWritten = "-", RepLatency = "4 / 0.5", RepUarch = "Zen 4", Description = "Fused multiply-add: dst = src2*src3 + dst. Single rounding, better precision than separate MUL+ADD. Critical for BLAS/GEMM kernels.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "ymm1, ymm2, ymm3/m256", Encoding = "RVM", OpcodeBytes = "VEX.256.66.0F38.W0 B8 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "4", Throughput = "0.5", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "4", Throughput = "0.5", Ports = "p01" } } },

                // ── Bit manipulation ──────────────────────────────────────────────

                ["BSF"] = new() { Mnemonic = "BSF", Summary = "Bit scan forward — index of lowest set bit", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "ZF (CF OF SF AF PF=undef)", RepLatency = "3 / 3", RepUarch = "Zen 4", Description = "Searches for the least significant set bit. ZF=1 and result undefined if source is zero. Prefer TZCNT (BMI1) which is faster and defines zero-input behavior.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F BC /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "3", Throughput = "3", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p1" } } },
                ["BSR"] = new() { Mnemonic = "BSR", Summary = "Bit scan reverse — index of highest set bit", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "ZF (CF OF SF AF PF=undef)", RepLatency = "3 / 3", RepUarch = "Zen 4", Description = "Searches for the most significant set bit. Result is undefined if source is zero. Prefer LZCNT (LZCNT) for defined zero behavior.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W 0F BD /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "3", Throughput = "3", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p1" } } },
                ["TZCNT"] = new() { Mnemonic = "TZCNT", Summary = "Count trailing zero bits (BMI1)", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "CF ZF", RepLatency = "2 / 1", RepUarch = "Zen 4", Description = "Counts trailing zeros (= index of lowest set bit). Returns operand size if input is zero, and sets CF. Faster than BSF and well-defined on zero input. Requires BMI1.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W F3 0F BC /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "2", Throughput = "1", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p1" } } },
                ["LZCNT"] = new() { Mnemonic = "LZCNT", Summary = "Count leading zero bits (LZCNT)", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "CF ZF", RepLatency = "2 / 1", RepUarch = "Zen 4", Description = "Counts leading zeros. Returns operand size if input is zero, and sets CF. Requires LZCNT CPUID feature.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W F3 0F BD /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "2", Throughput = "1", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p1" } } },
                ["POPCNT"] = new() { Mnemonic = "POPCNT", Summary = "Population count — number of set bits", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "ZF (CF OF SF AF PF=0)", RepLatency = "2 / 1", RepUarch = "Zen 4", Description = "Counts the number of bits set to 1 in the source operand. Requires POPCNT CPUID feature bit. On older Intel µarchs has a false dependency on the destination register.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64", Encoding = "RM", OpcodeBytes = "REX.W F3 0F B8 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "2", Throughput = "1", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p1" } } },
                ["ANDN"] = new() { Mnemonic = "ANDN", Summary = "Bitwise AND-NOT dst = ~src1 & src2 (BMI1)", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "SF ZF (CF=OF=0)", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Three-operand: dst = NOT(src1) AND src2. Useful for clearing specific bits. Requires BMI1.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r64, r/m64", Encoding = "RVM", OpcodeBytes = "VEX.LZ.0F38.W1 F2 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["BEXTR"] = new() { Mnemonic = "BEXTR", Summary = "Bit-field extract (BMI1)", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "ZF (CF OF SF AF PF=undef)", RepLatency = "2 / 1", RepUarch = "Zen 4", Description = "Extracts a contiguous bit field. The control register encodes start:length in the low 16 bits. Requires BMI1.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64, r64", Encoding = "RMV", OpcodeBytes = "VEX.LZ.0F38.W1 F7 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "2", Throughput = "1", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "2", Throughput = "1", Ports = "p06" } } },
                ["BZHI"] = new() { Mnemonic = "BZHI", Summary = "Zero high bits from index (BMI2)", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "SF ZF CF (OF=0)", RepLatency = "1 / 0.5", RepUarch = "Zen 4", Description = "Clears all bits from bit-index N to the MSB. Useful for masking. Requires BMI2.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r/m64, r64", Encoding = "RMV", OpcodeBytes = "VEX.LZ.0F38.W1 F5 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.5", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.5", Ports = "p06" } } },
                ["PEXT"] = new() { Mnemonic = "PEXT", Summary = "Parallel bits extract (BMI2)", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "-", RepLatency = "3 / 1", RepUarch = "Zen 4", Description = "Extracts bits from src at positions marked by the mask and packs them into the LSBs of dst. Extremely slow on AMD (microcoded ~100 cycles). Fast on Intel. Requires BMI2.", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r64, r/m64", Encoding = "RMV", OpcodeBytes = "VEX.LZ.F3.0F38.W1 F5 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~100", Throughput = "~100", Ports = "p0 (slow)" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p1" } } },
                ["PDEP"] = new() { Mnemonic = "PDEP", Summary = "Parallel bits deposit (BMI2)", Category = "Bit Manipulation", FlagsRead = "-", FlagsWritten = "-", RepLatency = "3 / 1", RepUarch = "Zen 4", Description = "Deposits bits from src at positions marked by the mask into dst. Same caveat as PEXT: very slow on AMD Zen µarchs (microcoded).", ExceptionClass = "#GP, #SS, #PF, #AC", Forms = new() { new() { Operands = "r64, r64, r/m64", Encoding = "RMV", OpcodeBytes = "VEX.LZ.F2.0F38.W1 F5 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~100", Throughput = "~100", Ports = "p0 (slow)" }, new() { Uarch = "Skylake", Latency = "3", Throughput = "1", Ports = "p1" } } },

                // ── Misc / system ─────────────────────────────────────────────────

                ["NOP"] = new() { Mnemonic = "NOP", Summary = "No operation", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "0.25 / 0.25", RepUarch = "Zen 4", Description = "Does nothing. Multi-byte NOPs (0F 1F /0) are used for alignment padding and are handled by the decoder without consuming execution ports.", ExceptionClass = "None", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "90" }, new() { Operands = "r/m16", Encoding = "M", OpcodeBytes = "0F 1F /0" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "0.25", Throughput = "0.25", Ports = "decode-only" }, new() { Uarch = "Skylake", Latency = "0.25", Throughput = "0.25", Ports = "decode-only" } } },
                ["ENDBR64"] = new() { Mnemonic = "ENDBR64", Summary = "End branch — CET indirect branch target marker", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Marks a valid indirect branch target for Intel CET (Control-flow Enforcement Technology). Without CET enabled, treated as a NOP. Commonly emitted by compilers with -fcf-protection.", ExceptionClass = "None (with CET: #CP on violation)", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "F3 0F 1E FA" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "decode-only" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "1", Ports = "decode-only" } } },
                ["CPUID"] = new() { Mnemonic = "CPUID", Summary = "CPU identification — serializing instruction", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "~100 / ~100", RepUarch = "Zen 4", Description = "Returns processor identification and feature information in EAX/EBX/ECX/EDX based on the leaf value in EAX (and sub-leaf in ECX). CPUID is a serializing instruction — it flushes the pipeline. Never use inside benchmarks.", ExceptionClass = "None (may #GP in VMX non-root)", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "0F A2" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~100", Throughput = "~100", Ports = "serializing" }, new() { Uarch = "Skylake", Latency = "~100", Throughput = "~100", Ports = "serializing" } } },
                ["RDTSC"] = new() { Mnemonic = "RDTSC", Summary = "Read time-stamp counter into EDX:EAX", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "~25 / ~25", RepUarch = "Zen 4", Description = "Reads the 64-bit TSC into EDX:EAX. Not serializing — use LFENCE before RDTSC for accurate microbenchmarks. RDTSCP (0F 01 F9) also reads IA32_TSC_AUX into ECX.", ExceptionClass = "None (may #GP at CPL>0 depending on CR4.TSD)", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "0F 31" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~25", Throughput = "~25", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "~25", Throughput = "~25", Ports = "p0" } } },
                ["MFENCE"] = new() { Mnemonic = "MFENCE", Summary = "Memory fence — serialize all memory ops", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "~40 / ~40", RepUarch = "Zen 4", Description = "Full memory barrier: all prior loads and stores complete before any subsequent memory operation begins. Use for TSO fences. SFENCE (stores) and LFENCE (loads+serializing) are lighter alternatives.", ExceptionClass = "None", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "0F AE /6" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~40", Throughput = "~40", Ports = "serializing" }, new() { Uarch = "Skylake", Latency = "~33", Throughput = "~33", Ports = "serializing" } } },
                ["LFENCE"] = new() { Mnemonic = "LFENCE", Summary = "Load fence — serialize instruction stream", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "~4 / ~4", RepUarch = "Zen 4", Description = "Serializes load operations and the instruction stream. Cheaper than MFENCE. Use before RDTSC and after speculative-execution mitigations (Spectre). Does NOT prevent store reordering.", ExceptionClass = "None", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "0F AE /5" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~4", Throughput = "~4", Ports = "serializing" }, new() { Uarch = "Skylake", Latency = "~4", Throughput = "~4", Ports = "serializing" } } },
                ["SFENCE"] = new() { Mnemonic = "SFENCE", Summary = "Store fence — serialize store operations", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "~5 / ~5", RepUarch = "Zen 4", Description = "Ensures all prior stores complete before subsequent stores. Lighter than MFENCE. Required after non-temporal stores (MOVNTPS etc.).", ExceptionClass = "None", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "0F AE /7" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~5", Throughput = "~5", Ports = "p4" }, new() { Uarch = "Skylake", Latency = "~5", Throughput = "~5", Ports = "p4" } } },
                ["PAUSE"] = new() { Mnemonic = "PAUSE", Summary = "Spin-loop hint — reduce power in spinlock", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "~140 / ~140", RepUarch = "Zen 4", Description = "Hints to the processor that a spin-wait loop is in progress, reducing power and improving performance when the lock is released. Mandatory in any spin-lock to avoid SMT slowdown.", ExceptionClass = "None", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "F3 90" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "~140", Throughput = "~140", Ports = "p0" }, new() { Uarch = "Skylake", Latency = "~140", Throughput = "~140", Ports = "p0" } } },
                ["UD2"] = new() { Mnemonic = "UD2", Summary = "Undefined instruction — guaranteed #UD", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "-", RepUarch = "", Description = "Generates an invalid opcode exception (#UD) unconditionally. Used as an unreachable sentinel, for testing exception handlers, and by compilers after __builtin_unreachable().", ExceptionClass = "#UD always", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "0F 0B" } }, Latencies = new() },
                ["INT3"] = new() { Mnemonic = "INT3", Summary = "Breakpoint — software debug trap", Category = "Misc", FlagsRead = "-", FlagsWritten = "-", RepLatency = "-", RepUarch = "", Description = "Generates a breakpoint exception (#BP / INT 3). Debuggers replace the first byte of an instruction with CC to set a software breakpoint.", ExceptionClass = "#BP", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "CC" } }, Latencies = new() },

                ["CDQ"] = new() { Mnemonic = "CDQ", Summary = "Sign-extend EAX into EDX:EAX", Category = "Data Transfer", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Sign-extends EAX into EDX:EAX (32→64-bit). Used to prepare for IDIV.", ExceptionClass = "None", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "99" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["CQO"] = new() { Mnemonic = "CQO", Summary = "Sign-extend RAX into RDX:RAX", Category = "Data Transfer", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 1", RepUarch = "Zen 4", Description = "Sign-extends RAX into RDX:RAX (64→128-bit). Required before 64-bit IDIV/DIV.", ExceptionClass = "None", Forms = new() { new() { Operands = "", Encoding = "ZO", OpcodeBytes = "REX.W 99" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "1", Ports = "p0" } } },
                ["XORPS"] = new() { Mnemonic = "XORPS", Summary = "XOR packed single FP — idiom to zero XMM register", Category = "SSE", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Performs bitwise XOR on two XMM registers. XORPS xmm,xmm is the canonical XMM zero-idiom; it breaks the dependency chain and is handled by the renamer on modern µarchs.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "0F 57 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },
                ["XORPD"] = new() { Mnemonic = "XORPD", Summary = "XOR packed double FP — idiom to zero XMM register", Category = "SSE2", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Bitwise XOR on XMM registers. Often emitted by compilers to zero double-precision XMM registers.", ExceptionClass = "#UD, #XM", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "66 0F 57 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },

                ["PXOR"] = new() { Mnemonic = "PXOR", Summary = "XOR packed integers (XMM) — zero idiom", Category = "SSE2", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Bitwise XOR on packed integer XMM registers. PXOR xmm,xmm is an integer zero idiom.", ExceptionClass = "#UD", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "66 0F EF /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },
                ["PCMPEQD"] = new() { Mnemonic = "PCMPEQD", Summary = "Compare packed 32-bit integers for equality", Category = "SSE2", FlagsRead = "-", FlagsWritten = "-", RepLatency = "1 / 0.33", RepUarch = "Zen 4", Description = "Compares each 32-bit element: sets lane to 0xFFFFFFFF if equal, 0 otherwise. Useful for SIMD branchless select patterns.", ExceptionClass = "#UD", Forms = new() { new() { Operands = "xmm1, xmm2/m128", Encoding = "RM", OpcodeBytes = "66 0F 76 /r" } }, Latencies = new() { new() { Uarch = "Zen 4", Latency = "1", Throughput = "0.33", Ports = "p2/p3" }, new() { Uarch = "Skylake", Latency = "1", Throughput = "0.33", Ports = "p015" } } },
            };
    }
}