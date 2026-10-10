using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for saving cloud data.
    /// </summary>
    [Serializable]
    public class SaveCloudSaveResponse
    {
        public bool success;
        public int dataSizeBytes;

        /// <summary>
        /// New server revision after the write (1 = first save). 0 when the server sent none.
        /// </summary>
        public long revision;
    }

    /// <summary>
    /// Response object for loading cloud data.
    /// </summary>
    [Serializable]
    public class LoadCloudSaveResponse
    {
        public bool found;
        public string saveData; // UTF-8 string
    }

    /// <summary>
    /// A cloud save together with the revision it was read at, from
    /// <c>CloudSaveManager.LoadSnapshot()</c> (string) or <c>LoadBytesSnapshot()</c> (bytes).
    /// Pass <see cref="Revision"/> as <c>expectedRevision</c> to the next save.
    /// </summary>
    /// <typeparam name="T"><c>string</c> or <c>byte[]</c></typeparam>
    public class CloudSaveSnapshot<T> where T : class
    {
        /// <summary>True when the load reached the server and succeeded.</summary>
        public bool IsSuccess { get; internal set; }

        /// <summary>True when the player has a save. False for an empty slot or a failed load.</summary>
        public bool Found { get; internal set; }

        /// <summary>The save, or null when not found or failed.</summary>
        public T Data { get; internal set; }

        /// <summary>
        /// Server revision of the save: 0 for an empty slot, 1 after the first save.
        /// Null when the server sent no readable <c>X-Cloud-Save-Revision</c> header (for example
        /// a self-hosted simpleServer). Null means unknown: do not treat it as an empty slot.
        /// </summary>
        public long? Revision { get; internal set; }

        /// <summary>True when <see cref="Revision"/> is known.</summary>
        public bool HasRevision => Revision.HasValue;

        /// <summary>Error message when <see cref="IsSuccess"/> is false.</summary>
        public string Error { get; internal set; }

        /// <summary>HTTP status code, 0 when the request was not sent.</summary>
        public long StatusCode { get; internal set; }
    }

    /// <summary>
    /// Outcome of a revision-checked cloud save.
    /// </summary>
    public enum CloudSaveWriteStatus
    {
        /// <summary>The save was written.</summary>
        Saved,

        /// <summary>
        /// HTTP 409: the cloud save changed on another device since the given revision.
        /// Nothing was written. Load the current save before you save again.
        /// </summary>
        Conflict,

        /// <summary>Any other failure (session, size limit, network). Nothing was written.</summary>
        Failed
    }

    /// <summary>
    /// Result of <c>CloudSaveManager.Save(data, expectedRevision)</c> and
    /// <c>SaveBytes(data, expectedRevision)</c>.
    /// </summary>
    public class CloudSaveWriteResult
    {
        /// <summary>Saved, Conflict or Failed.</summary>
        public CloudSaveWriteStatus Status { get; internal set; }

        /// <summary>True when the save was written.</summary>
        public bool IsSuccess => Status == CloudSaveWriteStatus.Saved;

        /// <summary>True when another device saved first (HTTP 409).</summary>
        public bool IsConflict => Status == CloudSaveWriteStatus.Conflict;

        /// <summary>
        /// New server revision after a successful save, to pass as <c>expectedRevision</c> to the
        /// next save. Null after a failure or when the server sent none (then load a snapshot first).
        /// </summary>
        public long? Revision { get; internal set; }

        /// <summary>Stored size in bytes after a successful save.</summary>
        public int DataSizeBytes { get; internal set; }

        /// <summary>Error message when the save was not written.</summary>
        public string Error { get; internal set; }

        /// <summary>HTTP status code, 0 when the request was not sent.</summary>
        public long StatusCode { get; internal set; }
    }
}
