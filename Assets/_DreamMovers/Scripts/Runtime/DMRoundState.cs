namespace DreamMovers
{
    public enum DMRoundState : byte
    {
        /// <summary>Waiting for the map (and on a generated map its navmesh); no timer, no monsters.</summary>
        Preparing,
        Playing,
        Success,
        Failed,
        /// <summary>The map could not be made ready (generation failed or navigation timed out); the round never starts.</summary>
        Aborted
    }
}
