namespace DreamMovers
{
    public enum DMMonsterState : byte
    {
        Idle,
        Patrol,
        /// <summary>Saw a doll and is going after it.</summary>
        Chase,
        /// <summary>Rushing toward a noise.</summary>
        Charge,
        /// <summary>At the last known spot, looking around.</summary>
        Search,
        /// <summary>Heading back to its patrol or post.</summary>
        Return
    }
}
