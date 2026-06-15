namespace HR_Codex_v0.Models
{
    public class DetectionPoint : Helpers.ObservableObject
    {
        private int _session;
        private double _range;
        private double _velocity;
        private double _amplitude;

        public int Session { get => _session; set => SetProperty(ref _session, value); }
        public double Range { get => _range; set => SetProperty(ref _range, value); }
        public double Velocity { get => _velocity; set => SetProperty(ref _velocity, value); }
        public double Amplitude { get => _amplitude; set => SetProperty(ref _amplitude, value); }
    }
}

