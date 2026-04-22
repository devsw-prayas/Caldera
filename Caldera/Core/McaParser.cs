using System;
using System.Collections.Generic;

namespace Caldera.Core
{
    public class McaInstructionRow
    {
        public string UOps { get; set; } = string.Empty;
        public string Latency { get; set; } = string.Empty;
        public string RThroughput { get; set; } = string.Empty;
        public string Instruction { get; set; } = string.Empty;
    }

    public class McaResult
    {
        public string Summary { get; set; } = string.Empty;
        public List<McaInstructionRow> Instructions { get; set; } = new();
    }

    public static class McaParser
    {
        public static McaResult Parse(string txt)
        {
            var res = new McaResult();
            if (string.IsNullOrWhiteSpace(txt)) return res;

            var lines = txt.Split('\n');
            bool inInstr = false;
            int instrIdx = -1;

            foreach (var l in lines)
            {
                var line = l.TrimEnd('\r', ' ');

                if (line.Contains("[1]") && line.Contains("Instructions:"))
                {
                    instrIdx = line.IndexOf("Instructions:");
                    inInstr = true;
                    continue;
                }

                if (inInstr)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        inInstr = false;
                        continue;
                    }

                    if (instrIdx > 0 && line.Length > 3)
                    {
                        int splitIdx = Math.Min(instrIdx, line.Length - 1);
                        // Backtrack splitIdx if the instruction text starts earlier due to tabs, etc.
                        var statsPart = line.Substring(0, splitIdx).Trim();
                        var instrPart = line.Substring(splitIdx).Trim();
                        
                        var stats = statsPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (stats.Length >= 3)
                        {
                            res.Instructions.Add(new McaInstructionRow
                            {
                                UOps = stats[0],
                                Latency = stats[1],
                                RThroughput = stats[2],
                                Instruction = instrPart
                            });
                        }
                    }
                }
                else if (res.Instructions.Count == 0 && !line.StartsWith("Instruction Info") && !line.StartsWith("["))
                {
                    if (string.IsNullOrWhiteSpace(line) && string.IsNullOrWhiteSpace(res.Summary)) continue;
                    res.Summary += line + "\n";
                }
            }

            res.Summary = res.Summary.TrimEnd();
            return res;
        }
    }
}
