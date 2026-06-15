using System;
using System.Collections.Generic;
using System.Linq;

namespace HR_Codex_v0.Helpers
{
    public static class PeakDetector
    {
        public static (int[] indices, double[] values) FindPeaks(double[] data, double minPeakHeight = double.MinValue, int minPeakDistance = 1, int maxPeaks = int.MaxValue)
        {
            var peakIndices = new List<int>();
            for (int i = 1; i < data.Length - 1; i++)
            {
                if (data[i] > minPeakHeight && data[i] > data[i - 1] && data[i] > data[i + 1])
                    peakIndices.Add(i);
            }
            var filtered = new List<int>();
            foreach (int p in peakIndices.OrderByDescending(i => data[i]))
            {
                bool ok = true;
                foreach (int f in filtered)
                    if (Math.Abs(p - f) < minPeakDistance) { ok = false; break; }
                if (ok) filtered.Add(p);
                if (filtered.Count >= maxPeaks) break;
            }
            filtered.Sort();
            return (filtered.ToArray(), filtered.Select(i => data[i]).ToArray());
        }
    }
}

