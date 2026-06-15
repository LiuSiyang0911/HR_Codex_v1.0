using System;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace HR_Codex_v0.Helpers
{
    public static class FftHelper
    {
        public static Complex[] FftShift(Complex[] input)
        {
            int n = input.Length;
            var output = new Complex[n];
            int half = n / 2;
            for (int i = 0; i < half; i++)
            {
                output[i] = input[i + half];
                output[i + half] = input[i];
            }
            if (n % 2 == 1)
                output[half] = input[n - 1];
            return output;
        }

        public static double[] Db(double[] magnitude)
        {
            var result = new double[magnitude.Length];
            for (int i = 0; i < magnitude.Length; i++)
                result[i] = 20.0 * Math.Log10(magnitude[i] + 1e-12);
            return result;
        }

        public static double[] Db(Complex[] complex)
        {
            var result = new double[complex.Length];
            for (int i = 0; i < complex.Length; i++)
                result[i] = 20.0 * Math.Log10(complex[i].Magnitude + 1e-12);
            return result;
        }

        public static double[,] Db(Complex[,] complex)
        {
            int rows = complex.GetLength(0);
            int cols = complex.GetLength(1);
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = 20.0 * Math.Log10(complex[i, j].Magnitude + 1e-12);
            return result;
        }

        public static double[] Abs(Complex[] complex)
        {
            var result = new double[complex.Length];
            for (int i = 0; i < complex.Length; i++)
                result[i] = complex[i].Magnitude;
            return result;
        }

        public static Complex[] ForwardFft(double[] realData)
        {
            var complex = new Complex[realData.Length];
            for (int i = 0; i < realData.Length; i++)
                complex[i] = new Complex(realData[i], 0);
            Fourier.Forward(complex, FourierOptions.Matlab);
            return complex;
        }

        public static Complex[] ForwardFft(Complex[] data)
        {
            var copy = new Complex[data.Length];
            data.CopyTo(copy, 0);
            Fourier.Forward(copy, FourierOptions.Matlab);
            return copy;
        }

        public static double Mean(double[] data)
        {
            double sum = 0;
            for (int i = 0; i < data.Length; i++) sum += data[i];
            return sum / data.Length;
        }
    }
}

