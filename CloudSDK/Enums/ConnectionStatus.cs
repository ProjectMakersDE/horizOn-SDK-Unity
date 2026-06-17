namespace PM.horizOn.Cloud.Enums
{
    /// <summary>
    /// Connection status for horizOn server.
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        /// Not connected, no connection attempt made
        /// </summary>
        Disconnected,

        /// <summary>
        /// Currently attempting to connect (pinging hosts)
        /// </summary>
        Connecting,

        /// <summary>
        /// Successfully connected to a host
        /// </summary>
        Connected,

        /// <summary>
        /// Connection failed
        /// </summary>
        Failed,

        /// <summary>
        /// Connection was lost after being established
        /// </summary>
        Lost,

        /// <summary>
        /// Attempting to reconnect after connection loss
        /// </summary>
        Reconnecting
    }
}
