using System;
using System.Collections.Generic;

namespace Caldera.Core
{
    public enum DiffOp
    {
        Equal,
        Insert,
        Delete
    }

    public class DiffLine
    {
        public DiffOp Op { get; set; }
        public string Text { get; set; } = string.Empty;
        public int OriginalIndex { get; set; } = -1; // -1 if not applicable
        public int NewIndex { get; set; } = -1;      // -1 if not applicable
    }

    public static class MyersDiff
    {
        public static List<DiffLine> Diff(string[] oldLines, string[] newLines)
        {
            var result = new List<DiffLine>();
            int N = oldLines.Length;
            int M = newLines.Length;
            int max = N + M;
            
            if (N == 0 && M == 0) return result;

            var v = new int[(2 * max) + 1];
            var trace = new List<int[]>();

            v[max + 1] = 0;

            for (int d = 0; d <= max; d++)
            {
                var vCopy = new int[v.Length];
                Array.Copy(v, vCopy, v.Length);
                trace.Add(vCopy);

                for (int k = -d; k <= d; k += 2)
                {
                    int x, y;
                    if (k == -d || (k != d && v[max + k - 1] < v[max + k + 1]))
                        x = v[max + k + 1];
                    else
                        x = v[max + k - 1] + 1;

                    y = x - k;

                    while (x < N && y < M && oldLines[x] == newLines[y])
                    {
                        x++;
                        y++;
                    }

                    v[max + k] = x;

                    if (x >= N && y >= M)
                    {
                        return Backtrack(oldLines, newLines, trace);
                    }
                }
            }

            return result;
        }

        private static List<DiffLine> Backtrack(string[] oldLines, string[] newLines, List<int[]> trace)
        {
            var diff = new List<DiffLine>();
            int x = oldLines.Length;
            int y = newLines.Length;
            int max = x + y;

            for (int d = trace.Count - 1; d >= 0; d--)
            {
                var v = trace[d];
                int k = x - y;
                
                int prevK;
                if (k == -d || (k != d && v[max + k - 1] < v[max + k + 1]))
                    prevK = k + 1;
                else
                    prevK = k - 1;

                int prevX = v[max + prevK];
                int prevY = prevX - prevK;

                while (x > prevX && y > prevY)
                {
                    x--;
                    y--;
                    diff.Add(new DiffLine { Op = DiffOp.Equal, Text = oldLines[x], OriginalIndex = x, NewIndex = y });
                }

                if (d > 0)
                {
                    if (x == prevX)
                    {
                        y--;
                        diff.Add(new DiffLine { Op = DiffOp.Insert, Text = newLines[y], NewIndex = y });
                    }
                    else
                    {
                        x--;
                        diff.Add(new DiffLine { Op = DiffOp.Delete, Text = oldLines[x], OriginalIndex = x });
                    }
                }
            }

            diff.Reverse();
            return diff;
        }
    }
}
