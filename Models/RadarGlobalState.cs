using System.Numerics;

namespace HR_Codex_v0.Models
{
    public class RadarGlobalState
    {
        public int Nr;
        public int Ncut;
        public int Np;
        public double[,] Window2D;
        public double[] RAxis;
        public double[] VelAxis;
        public double[] Theta;
        public int Beams;
        public double[,] X;
        public double[,] Y;
        public double[,] Amp;
        public Complex[,] RdmRef;
        public double[] Phase;
        public double[,] Mask;
        public double FftMeanInit;
        public double ThetaDwell;

        public void Clear()
        {
            Nr = 0; Ncut = 0; Np = 0;
            Window2D = null;
            RAxis = null; VelAxis = null; Theta = null;
            Beams = 0;
            X = null; Y = null; Amp = null;
            RdmRef = null;
            Phase = null;
            Mask = null;
            FftMeanInit = 0;
            ThetaDwell = 0;
        }
    }
}

