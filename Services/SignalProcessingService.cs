using HR_Codex_v0.Helpers;
using HR_Codex_v0.Models;
using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace HR_Codex_v0.Services
{
    public class SignalProcessingService
    {
        public RadarGlobalState State { get; } = new RadarGlobalState();

        public void Initialize(RadarConfig config)
        {
            ValidateConfig(config);
            State.Clear();
            State.Nr = config.Nr;
            State.Ncut = config.Ncut;
            State.Np = config.Np;
            State.FftMeanInit = config.FftMean;

            double c = 3e8;
            double fc = 11e9;
            double Vmax = 60.0;
            double V_az = config.Vptz / 64.0 * Vmax;
            double t_dwell = config.Pri * config.Np;
            State.ThetaDwell = V_az * t_dwell;

            State.RAxis = ArrayHelper.Linspace(0, (config.Ncut - 1) * c / 2.0 / config.Bandwidth, config.Ncut);
            int beams = (int)Math.Round(360.0 / State.ThetaDwell);
            State.Beams = beams;
            State.Theta = ArrayHelper.Linspace(0, 360, beams);

            var dopplerAxis = ArrayHelper.Linspace(-config.Np / 2 + 1, config.Np / 2, config.Np);
            for (int i = 0; i < dopplerAxis.Length; i++)
                dopplerAxis[i] = dopplerAxis[i] / config.Np / config.Pri;
            State.VelAxis = dopplerAxis.Select(d => d * c / 2.0 / fc).ToArray();

            State.Phase = new double[256];

            int masksize = 9;
            int lengthGuard = 3;
            int start = (masksize - lengthGuard) / 2;
            State.Mask = new double[masksize, masksize];
            for (int i = 0; i < masksize; i++)
                for (int j = 0; j < masksize; j++)
                    State.Mask[i, j] = 1.0;
            for (int i = start; i < start + lengthGuard; i++)
                for (int j = start; j < start + lengthGuard; j++)
                    State.Mask[i, j] = 0.0;
            double maskSum = 0;
            for (int i = 0; i < masksize; i++)
                for (int j = 0; j < masksize; j++)
                    maskSum += State.Mask[i, j];
            for (int i = 0; i < masksize; i++)
                for (int j = 0; j < masksize; j++)
                    State.Mask[i, j] /= maskSum;

            State.X = new double[config.Ncut, beams];
            State.Y = new double[config.Ncut, beams];
            for (int i = 0; i < config.Ncut; i++)
                for (int j = 0; j < beams; j++)
                {
                    double thetaRad = State.Theta[j] * Math.PI / 180.0;
                    State.X[i, j] = Math.Cos(thetaRad) * State.RAxis[i];
                    State.Y[i, j] = Math.Sin(thetaRad) * State.RAxis[i];
                }

            var hannNr = MathNet.Numerics.Window.Hann(config.Nr);
            var hannNp = MathNet.Numerics.Window.Hann(config.Np);
            State.Window2D = new double[config.Nr, config.Np];
            for (int i = 0; i < config.Nr; i++)
                for (int j = 0; j < config.Np; j++)
                    State.Window2D[i, j] = hannNr[i] * hannNp[j];

            State.Amp = new double[config.Ncut, beams];
            for (int i = 0; i < config.Ncut; i++)
                for (int j = 0; j < beams; j++)
                    State.Amp[i, j] = config.FftMean;
        }

        private void ValidateConfig(RadarConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (config.Nr <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Nr), "Nr must be greater than 0");
            if (config.Np <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Np), "Np must be greater than 0");
            if (config.Ncut <= 0 || config.Ncut > config.Nr)
                throw new ArgumentOutOfRangeException(nameof(config.Ncut), "Ncut must be between 1 and Nr");
            if (config.Pri <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Pri), "PRI must be greater than 0");
            if (config.Bandwidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Bandwidth), "Bandwidth must be greater than 0");
            if (config.Fc <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Fc), "Fc must be greater than 0");
            if (config.Vptz <= 0 || config.Vptz > 64)
                throw new ArgumentOutOfRangeException(nameof(config.Vptz), "Vptz must be between 1 and 64");
            if (config.Offset < 0)
                throw new ArgumentOutOfRangeException(nameof(config.Offset), "Offset must be 0 or greater");
            if (config.Rr < 0 || config.Rr >= config.Ncut)
                throw new ArgumentOutOfRangeException(nameof(config.Rr), "Rr must be within Ncut");
            if (config.Xlim <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Xlim), "Xlim must be greater than 0");
            if (config.Threshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Threshold), "Threshold must be greater than 0");
            if (config.PointsMax < 0)
                throw new ArgumentOutOfRangeException(nameof(config.PointsMax), "PointsMax must be 0 or greater");

            double thetaDwell = (config.Vptz / 64.0 * 60.0) * config.Pri * config.Np;
            int beams = thetaDwell > 0 ? (int)Math.Round(360.0 / thetaDwell) : 0;
            if (beams <= 0 || beams > 100000)
                throw new ArgumentOutOfRangeException(nameof(config.Vptz), "Parameter combination creates an invalid beam count");
        }

        public void Clear()
        {
            State.Clear();
        }

        // ================ FFT Profile ================
        public double[] ProcessFFT(short[] real, short[] imag, int offset)
        {
            int n = State.Nr;
            EnsureSamples(real, imag, offset, n);
            var complex = new Complex[n];
            for (int i = 0; i < n; i++)
                complex[i] = new Complex(real[offset + i], imag[offset + i]);

            // 去均值（复数去均值）
            double avgReal = 0, avgImag = 0;
            for (int i = 0; i < n; i++) { avgReal += complex[i].Real; avgImag += complex[i].Imaginary; }
            avgReal /= n; avgImag /= n;
            for (int i = 0; i < n; i++) complex[i] -= new Complex(avgReal, avgImag);

            Fourier.Forward(complex, FourierOptions.Matlab);
            var shifted = FftHelper.FftShift(complex);
            return FftHelper.Db(shifted);
        }

        // ================ PPI ================
        public double[,] ProcessPPI(short[] real, short[] imag, int offset, double azimuth)
        {
            EnsureSamples(real, imag, offset, State.Nr * State.Np);
            var matrix = BuildComplexMatrix(real, imag, offset, State.Nr, State.Np);
            matrix = ApplyWindow2D(matrix);
            var fftCol = FftColumns(matrix);
            var cut = CropRows(fftCol, State.Ncut);
            var fftRow = FftRows(cut);
            var shifted = FftShiftRows(fftRow);
            var rdDb = FftHelper.Db(shifted);
            ApplyFloor(rdDb, State.FftMeanInit);

            var rangeProfile = new double[State.Ncut];
            for (int i = 0; i < State.Ncut; i++)
            {
                double max = double.MinValue;
                for (int j = 0; j < State.Np; j++)
                    if (rdDb[i, j] > max) max = rdDb[i, j];
                rangeProfile[i] = max;
            }

            int azIndex = (int)Math.Ceiling(azimuth / State.ThetaDwell);
            int i1 = ((azIndex - 1) % State.Beams + State.Beams) % State.Beams;
            int i2 = (azIndex % State.Beams + State.Beams) % State.Beams;
            int i3 = ((azIndex + 1) % State.Beams + State.Beams) % State.Beams;
            for (int i = 0; i < State.Ncut; i++)
            {
                State.Amp[i, i1] = rangeProfile[i];
                State.Amp[i, i2] = rangeProfile[i];
                State.Amp[i, i3] = rangeProfile[i];
            }
            return State.Amp;
        }

        // ================ RDM + CFAR ================
        public (double[,] rdMap, DetectionPoint[] points) ProcessRDM(short[] real, short[] imag, int offset, double xlim, double threshold)
        {
            EnsureSamples(real, imag, offset, State.Nr * State.Np);
            // 合并 BuildComplexMatrix + ApplyWindow2D，减少一次数组分配和遍历
            var matrix = BuildAndWindowMatrix(real, imag, offset, State.Nr, State.Np);
            var fftCol = FftColumns(matrix);
            var cut = CropRows(fftCol, State.Ncut);
            var fftRow = FftRows(cut);
            var shifted = FftShiftRows(fftRow);
            var rdDb = ToDbAndFloor(shifted, State.FftMeanInit);
            var rdPower = ToPower(shifted);

            var points = DetectOsCfar(
                rdPower,
                rdDb,
                trainRange: 6,
                trainDoppler: 17,
                guardRange: 2,
                guardDoppler: 3,
                rankRatio: 0.75,
                pfa: 1e-4,
                thresholdScale: Math.Max(0.1, threshold),
                minAmplitudeDb: State.FftMeanInit);

            return (rdDb, points.OrderByDescending(p => p.Amplitude).ToArray());
        }

        // ================ RDM Ref ================
        public double[,] ProcessRDM_ref(short[] real, short[] imag, int offset)
        {
            EnsureSamples(real, imag, offset, State.Nr * State.Np);
            var matrix = BuildComplexMatrix(real, imag, offset, State.Nr, State.Np);
            matrix = ApplyWindow2D(matrix);
            var fftCol = FftColumns(matrix);
            var cut = CropRows(fftCol, State.Ncut);
            var fftRow = FftRows(cut);
            var shifted = FftShiftRows(fftRow);
            State.RdmRef = new Complex[shifted.GetLength(0), shifted.GetLength(1)];
            for (int i = 0; i < shifted.GetLength(0); i++)
                for (int j = 0; j < shifted.GetLength(1); j++)
                    State.RdmRef[i, j] = shifted[i, j];
            var rdDb = FftHelper.Db(shifted);
            // removed FlipLeftRight: keep natural velocity order [-vmax .. 0 .. +vmax]
            ApplyFloor(rdDb, State.FftMeanInit);
            return rdDb;
        }

        // ================ RDM In (Interferometry) ================
        public (double[,] rdMap, double[] displacement) ProcessRDM_in(short[] real, short[] imag, int offset, int rr)
        {
            EnsureSamples(real, imag, offset, State.Nr * State.Np);
            if (rr < 0 || rr >= State.Ncut)
                throw new ArgumentOutOfRangeException(nameof(rr), $"形变观测单元必须在 0 到 {State.Ncut - 1} 之间");
            var matrix = BuildComplexMatrix(real, imag, offset, State.Nr, State.Np);
            matrix = ApplyWindow2D(matrix);
            var fftCol = FftColumns(matrix);
            var cut = CropRows(fftCol, State.Ncut);
            var fftRow = FftRows(cut);
            var shifted = FftShiftRows(fftRow);
            var rdDb = FftHelper.Db(shifted);
            // removed FlipLeftRight: keep natural velocity order [-vmax .. 0 .. +vmax]
            ApplyFloor(rdDb, State.FftMeanInit);

            if (State.RdmRef != null)
            {
                int centerDoppler = State.Np / 2;
                var Din = new Complex[State.Ncut, State.Np];
                for (int i = 0; i < State.Ncut; i++)
                    for (int j = 0; j < State.Np; j++)
                        Din[i, j] = State.RdmRef[i, j] * Complex.Conjugate(shifted[i, j]);

                var phaseSlice = new double[256];
                for (int i = 0; i < 255; i++) phaseSlice[i] = State.Phase[i + 1];
                phaseSlice[255] = Din[rr, centerDoppler].Phase;
                State.Phase = phaseSlice;
                var unwrapped = ArrayHelper.Unwrap(State.Phase);
                var displacement = unwrapped.Select(p => p * 3e8 / (8 * Math.PI * 5.5e9)).ToArray();
                return (rdDb, displacement);
            }
            return (rdDb, null);
        }

        // ================ Time Domain / Range Profile ================
        public (double[] rangeProfile, double[] peakRanges, double[] peakValues) ProcessTD(short[] real, short[] imag, int offset)
        {
            EnsureSamples(real, imag, offset, State.Nr * State.Np);
            var matrix = BuildAndWindowMatrix(real, imag, offset, State.Nr, State.Np);
            var fftCol = FftColumns(matrix);
            var cut = CropRows(fftCol, State.Ncut);
            var fftRow = FftRows(cut);
            var rdDb = FftShiftDbAndFloor(fftRow, State.FftMeanInit);

            int centerCol = State.Np / 2;
            var rangeProfile = new double[State.Ncut];
            for (int i = 0; i < State.Ncut; i++)
                rangeProfile[i] = rdDb[i, centerCol];

            var (indices, values) = PeakDetector.FindPeaks(rangeProfile, 105, 1, 3);
            var peakRanges = indices.Select(idx => State.RAxis[(int)idx]).ToArray();
            return (rangeProfile, peakRanges, values);
        }

        // ================ Helpers ================
        private void EnsureSamples(short[] real, short[] imag, int offset, int requiredSamples)
        {
            if (real == null || imag == null)
                throw new InvalidOperationException("I/Q 数据为空");
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "偏移量不能为负");
            if (real.Length < offset + requiredSamples || imag.Length < offset + requiredSamples)
                throw new InvalidOperationException($"I/Q 数据长度不足：需要 {offset + requiredSamples} 点，实际 Real={real.Length}, Imag={imag.Length}");
        }

        private Complex[,] BuildComplexMatrix(short[] real, short[] imag, int offset, int nr, int np)
        {
            var matrix = new Complex[nr, np];
            for (int j = 0; j < np; j++)
                for (int i = 0; i < nr; i++)
                {
                    int idx = offset + i + j * nr;
                    matrix[i, j] = new Complex(real[idx], imag[idx]);
                }
            return matrix;
        }

        private Complex[,] BuildAndWindowMatrix(short[] real, short[] imag, int offset, int nr, int np)
        {
            var matrix = new Complex[nr, np];
            for (int j = 0; j < np; j++)
                for (int i = 0; i < nr; i++)
                {
                    int idx = offset + i + j * nr;
                    matrix[i, j] = new Complex(real[idx], imag[idx]) * State.Window2D[i, j];
                }
            return matrix;
        }

        private Complex[,] ApplyWindow2D(Complex[,] matrix)
        {
            int nr = matrix.GetLength(0);
            int np = matrix.GetLength(1);
            var result = new Complex[nr, np];
            for (int i = 0; i < nr; i++)
                for (int j = 0; j < np; j++)
                    result[i, j] = matrix[i, j] * State.Window2D[i, j];
            return result;
        }

        private Complex[,] FftColumns(Complex[,] matrix)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new Complex[rows, cols];
            Parallel.For(0, cols, j =>
            {
                var col = new Complex[rows];
                for (int i = 0; i < rows; i++) col[i] = matrix[i, j];
                Fourier.Forward(col, FourierOptions.Matlab);
                for (int i = 0; i < rows; i++) result[i, j] = col[i];
            });
            return result;
        }

        private Complex[,] FftRows(Complex[,] matrix)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new Complex[rows, cols];
            Parallel.For(0, rows, i =>
            {
                var row = new Complex[cols];
                for (int j = 0; j < cols; j++) row[j] = matrix[i, j];
                Fourier.Forward(row, FourierOptions.Matlab);
                for (int j = 0; j < cols; j++) result[i, j] = row[j];
            });
            return result;
        }

        private Complex[,] FftShiftRows(Complex[,] matrix)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new Complex[rows, cols];
            int half = cols / 2;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < half; j++)
                {
                    result[i, j] = matrix[i, j + half];
                    result[i, j + half] = matrix[i, j];
                }
                if (cols % 2 == 1)
                    result[i, half] = matrix[i, cols - 1];
            }
            return result;
        }

        private double[,] FftShiftDbAndFloor(Complex[,] matrix, double floor)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new double[rows, cols];
            int half = cols / 2;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < half; j++)
                {
                    double val = 20.0 * Math.Log10(matrix[i, j + half].Magnitude + 1e-12);
                    result[i, j] = val < floor ? floor : val;
                }
                for (int j = 0; j < half; j++)
                {
                    double val = 20.0 * Math.Log10(matrix[i, j].Magnitude + 1e-12);
                    result[i, j + half] = val < floor ? floor : val;
                }
                if (cols % 2 == 1)
                {
                    double val = 20.0 * Math.Log10(matrix[i, cols - 1].Magnitude + 1e-12);
                    result[i, half] = val < floor ? floor : val;
                }
            }
            return result;
        }

        private double[,] ToDbAndFloor(Complex[,] matrix, double floor)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    double val = 20.0 * Math.Log10(matrix[i, j].Magnitude + 1e-12);
                    result[i, j] = val < floor ? floor : val;
                }
            return result;
        }

        private double[,] ToPower(Complex[,] matrix)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = matrix[i, j].Magnitude * matrix[i, j].Magnitude;
            return result;
        }

        private List<DetectionPoint> DetectOsCfar(
            double[,] powerMap,
            double[,] dbMap,
            int trainRange,
            int trainDoppler,
            int guardRange,
            int guardDoppler,
            double rankRatio,
            double pfa,
            double thresholdScale,
            double minAmplitudeDb)
        {
            int rows = powerMap.GetLength(0);
            int cols = powerMap.GetLength(1);
            int trainingCount = CountTrainingCells(trainRange, trainDoppler, guardRange, guardDoppler);
            int rankIndex = Clamp((int)Math.Floor((trainingCount - 1) * rankRatio), 0, trainingCount - 1);
            double alpha = EstimateOsCfarAlpha(trainingCount, rankIndex + 1, pfa) * thresholdScale;
            var training = new double[trainingCount];
            var points = new List<DetectionPoint>();

            for (int r = trainRange; r < rows - trainRange; r++)
            {
                for (int d = trainDoppler; d < cols - trainDoppler; d++)
                {
                    int count = 0;
                    for (int rr = r - trainRange; rr <= r + trainRange; rr++)
                    {
                        for (int dd = d - trainDoppler; dd <= d + trainDoppler; dd++)
                        {
                            bool inGuard = Math.Abs(rr - r) <= guardRange && Math.Abs(dd - d) <= guardDoppler;
                            if (!inGuard)
                                training[count++] = powerMap[rr, dd];
                        }
                    }

                    Array.Sort(training);
                    double noise = training[rankIndex];
                    if (noise <= 0)
                        continue;

                    if (powerMap[r, d] > alpha * noise &&
                        dbMap[r, d] > minAmplitudeDb &&
                        IsLocalMaximum(powerMap, r, d, 1, 1))
                    {
                        points.Add(new DetectionPoint
                        {
                            Range = State.RAxis[r],
                            Velocity = State.VelAxis[d],
                            Amplitude = dbMap[r, d]
                        });
                    }
                }
            }

            return points;
        }

        private int CountTrainingCells(int trainRange, int trainDoppler, int guardRange, int guardDoppler)
        {
            int windowCells = (2 * trainRange + 1) * (2 * trainDoppler + 1);
            int guardCells = (2 * guardRange + 1) * (2 * guardDoppler + 1);
            return windowCells - guardCells;
        }

        private double EstimateOsCfarAlpha(int trainingCount, int rankOneBased, double pfa)
        {
            double target = Math.Log(Math.Max(1e-12, Math.Min(0.5, pfa)));
            double low = 0;
            double high = 1;
            while (OsCfarLogPfa(trainingCount, rankOneBased, high) > target)
                high *= 2;

            for (int i = 0; i < 64; i++)
            {
                double mid = (low + high) * 0.5;
                if (OsCfarLogPfa(trainingCount, rankOneBased, mid) > target)
                    low = mid;
                else
                    high = mid;
            }
            return high;
        }

        private double OsCfarLogPfa(int trainingCount, int rankOneBased, double alpha)
        {
            double logPfa = 0;
            int start = trainingCount - rankOneBased + 1;
            for (int i = start; i <= trainingCount; i++)
                logPfa += Math.Log((double)i / (i + alpha));
            return logPfa;
        }

        private bool IsLocalMaximum(double[,] map, int row, int col, int rowRadius, int colRadius)
        {
            double value = map[row, col];
            for (int r = row - rowRadius; r <= row + rowRadius; r++)
                for (int c = col - colRadius; c <= col + colRadius; c++)
                    if ((r != row || c != col) && map[r, c] >= value)
                        return false;
            return true;
        }

        private int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private Complex[,] CropRows(Complex[,] matrix, int newRows)
        {
            int cols = matrix.GetLength(1);
            var result = new Complex[newRows, cols];
            for (int i = 0; i < newRows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = matrix[i, j];
            return result;
        }

        private void ApplyFloor(double[,] matrix, double floor)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    if (matrix[i, j] < floor) matrix[i, j] = floor;
        }
    }
}

