using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Body of PUT /api/v1/app/player-profile. Replaces the whole visible profile:
    /// a missing, null or empty avatarId / frameId clears the slot, a missing or empty
    /// badges list clears all badges.
    /// </summary>
    [Serializable]
    public class SetPlayerProfileRequest
    {
        public string userId;
        public string avatarId;
        public string frameId;
        public string[] badges;
    }
}
