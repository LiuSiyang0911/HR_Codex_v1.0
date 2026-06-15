namespace HR_Codex_v0.Services.RecorderProtocol
{
    public enum Hr23RecorderState
    {
        Idle,
        Prepared,
        Recording,
        Stopped,
        Error
    }

    internal static class Hr23RecorderStateExtensions
    {
        public static string ToProtocolValue(this Hr23RecorderState state)
        {
            return state.ToString().ToLowerInvariant();
        }
    }
}
