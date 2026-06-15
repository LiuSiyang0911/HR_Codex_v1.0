namespace HR_Codex_v0.Models
{
    public class RadarConfig : Helpers.ObservableObject
    {
        private int _nr = 5000;
        private int _ncut = 40;
        private int _np = 256;
        private int _nframe = 1;
        private int _vptz = 20;
        private double _fs = 10e6;
        private double _pri = 500e-6;
        private double _fc = 11.05e9;
        private double _bandwidth = 100e6;
        private int _offset = 0;
        private int _fftMean = 130;
        private double _xlim = 4;
        private double _rr = 9;
        private double _threshold = 1.2;
        private int _pointsMax = 50;

        public int Nr { get => _nr; set => SetProperty(ref _nr, Clamp(value, _ncut, 200000)); }
        public int Ncut
        {
            get => _ncut;
            set
            {
                if (SetProperty(ref _ncut, Clamp(value, 1, Nr)) && _rr >= _ncut)
                    Rr = _ncut - 1;
            }
        }
        public int Np { get => _np; set => SetProperty(ref _np, Clamp(value, 1, 16384)); }
        public int Nframe { get => _nframe; set => SetProperty(ref _nframe, Clamp(value, 1, 100000)); }
        public int Vptz { get => _vptz; set => SetProperty(ref _vptz, Clamp(value, 1, 64)); }
        public double Fs { get => _fs; set => SetProperty(ref _fs, Min(value, 1e-9)); }
        public double Pri { get => _pri; set => SetProperty(ref _pri, Min(value, 1e-9)); }
        public double Fc { get => _fc; set => SetProperty(ref _fc, Min(value, 1e-9)); }
        public double Bandwidth { get => _bandwidth; set => SetProperty(ref _bandwidth, Min(value, 1e-9)); }
        public int Offset { get => _offset; set => SetProperty(ref _offset, value < 0 ? 0 : value); }
        public int FftMean { get => _fftMean; set => SetProperty(ref _fftMean, value); }
        public double Xlim { get => _xlim; set => SetProperty(ref _xlim, Min(value, 1e-9)); }
        public double Rr { get => _rr; set => SetProperty(ref _rr, Clamp(value, 0, Ncut - 1)); }
        public double Threshold { get => _threshold; set => SetProperty(ref _threshold, Min(value, 1e-9)); }
        public int PointsMax { get => _pointsMax; set => SetProperty(ref _pointsMax, value < 0 ? 0 : value); }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double Min(double value, double min)
        {
            return value < min ? min : value;
        }
    }
}

