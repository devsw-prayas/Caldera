namespace Caldera
{
    // ── Compiler path store ───────────────────────────────────────────────────
    //
    // Holds the user-configured executable paths for each supported compiler.
    // Falls back to bare command names (resolved via PATH) when unset.

    public static class CompilerPaths
    {
        public static string Clang { get; set; } = string.Empty;
        public static string Gpp   { get; set; } = string.Empty;
        public static string Cl    { get; set; } = string.Empty;
        public static string Mca   { get; set; } = string.Empty;

        public static string Resolve(string compilerName) => compilerName switch
        {
            "clang++" => string.IsNullOrWhiteSpace(Clang) ? "clang++" : Clang,
            "g++"     => string.IsNullOrWhiteSpace(Gpp)   ? "g++"     : Gpp,
            "cl.exe"  => string.IsNullOrWhiteSpace(Cl)    ? "cl"      : Cl,
            "llvm-mca"=> string.IsNullOrWhiteSpace(Mca)   ? "llvm-mca": Mca,
            _         => compilerName
        };
    }
}
