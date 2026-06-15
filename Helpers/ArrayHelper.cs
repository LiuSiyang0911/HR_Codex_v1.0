using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace HR_Codex_v0.Helpers
{
    public static class ArrayHelper
    {
        public static double[,] Reshape(double[] data, int rows, int cols)
        {
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = data[i + j * rows];
            return result;
        }

        public static Complex[,] ReshapeComplex(double[] data, int rows, int cols)
        {
            var result = new Complex[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    int idx = (i + j * rows) * 2;
                    result[i, j] = new Complex(data[idx], data[idx + 1]);
                }
            return result;
        }

        public static double[,] FlipLeftRight(double[,] matrix)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = matrix[i, cols - 1 - j];
            return result;
        }

        public static double[,] PadArrayReplicate(double[,] matrix, int padRows, int padCols)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new double[rows + 2 * padRows, cols + 2 * padCols];
            for (int i = 0; i < rows + 2 * padRows; i++)
            {
                int srcRow = Math.Min(Math.Max(i - padRows, 0), rows - 1);
                for (int j = 0; j < cols + 2 * padCols; j++)
                {
                    int srcCol = Math.Min(Math.Max(j - padCols, 0), cols - 1);
                    result[i, j] = matrix[srcRow, srcCol];
                }
            }
            return result;
        }

        public static double[,] Conv2Valid(double[,] padded, double[,] kernel)
        {
            int kRows = kernel.GetLength(0);
            int kCols = kernel.GetLength(1);
            int outRows = padded.GetLength(0) - kRows + 1;
            int outCols = padded.GetLength(1) - kCols + 1;
            var result = new double[outRows, outCols];
            Parallel.For(0, outRows, i =>
            {
                for (int j = 0; j < outCols; j++)
                {
                    double sum = 0;
                    for (int ki = 0; ki < kRows; ki++)
                        for (int kj = 0; kj < kCols; kj++)
                            sum += padded[i + ki, j + kj] * kernel[ki, kj];
                    result[i, j] = sum;
                }
            });
            return result;
        }

        public static bool[,] ImRegionalMax(double[,] data)
        {
            int rows = data.GetLength(0);
            int cols = data.GetLength(1);
            var result = new bool[rows, cols];
            Parallel.For(0, rows, i =>
            {
                for (int j = 0; j < cols; j++)
                {
                    bool isMax = true;
                    double val = data[i, j];
                    for (int di = -1; di <= 1 && isMax; di++)
                        for (int dj = -1; dj <= 1 && isMax; dj++)
                        {
                            if (di == 0 && dj == 0) continue;
                            int ni = i + di, nj = j + dj;
                            if (ni >= 0 && ni < rows && nj >= 0 && nj < cols && data[ni, nj] > val)
                                isMax = false;
                        }
                    result[i, j] = isMax;
                }
            });
            return result;
        }

        public static double[] Unwrap(double[] phase)
        {
            var result = new double[phase.Length];
            phase.CopyTo(result, 0);
            for (int i = 1; i < result.Length; i++)
            {
                double diff = result[i] - result[i - 1];
                while (diff > Math.PI) { result[i] -= 2 * Math.PI; diff -= 2 * Math.PI; }
                while (diff < -Math.PI) { result[i] += 2 * Math.PI; diff += 2 * Math.PI; }
            }
            return result;
        }

        public static double[] FindPeaks(double[] data, double minPeakHeight, int minPeakDistance)
        {
            var peaks = new List<int>();
            for (int i = 1; i < data.Length - 1; i++)
            {
                if (data[i] > minPeakHeight && data[i] > data[i - 1] && data[i] > data[i + 1])
                    peaks.Add(i);
            }
            var filtered = new List<int>();
            foreach (int p in peaks)
            {
                bool ok = true;
                foreach (int f in filtered)
                    if (Math.Abs(p - f) < minPeakDistance) { ok = false; break; }
                if (ok) filtered.Add(p);
            }
            return filtered.Select(i => (double)i).ToArray();
        }

        public static double[] Linspace(double start, double end, int count)
        {
            var result = new double[count];
            double step = (end - start) / (count - 1);
            for (int i = 0; i < count; i++) result[i] = start + i * step;
            return result;
        }

        public static double[,] MeshGrid(double[] x, double[] y)
        {
            var result = new double[y.Length, x.Length];
            for (int i = 0; i < y.Length; i++)
                for (int j = 0; j < x.Length; j++)
                    result[i, j] = x[j];
            return result;
        }
    }
}

