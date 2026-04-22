namespace Caldera
{
    public partial class MainWindow
    {
        // ── ASM stats (instruction count, function count) ──────────────────────

        private void UpdateAsmStats(string asmText)
        {
            if (AsmStatsLabel == null) return;
            if (string.IsNullOrWhiteSpace(asmText))
            {
                AsmStatsLabel.Text = string.Empty;
                return;
            }

            var lines = asmText.Split('\n');
            int instrCount = 0, funcCount = 0;

            foreach (var line in lines)
            {
                var t = line.TrimStart();
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith(';') || t.StartsWith('#') || t.StartsWith('.'))
                    continue;
                if (!line.StartsWith(' ') && !line.StartsWith('\t') && t.EndsWith(':'))
                    funcCount++;
                else
                    instrCount++;
            }

            AsmStatsLabel.Text = $"{instrCount} instr  ·  {funcCount} fn";
        }
    }
}
