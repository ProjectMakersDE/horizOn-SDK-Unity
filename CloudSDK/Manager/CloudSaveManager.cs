using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for cloud save functionality.
    /// Supports two modes:
    /// - JSON mode: Save/Load methods send UTF-8 strings via JSON (Content-Type: application/json)
    /// - Binary mode: SaveBytes/LoadBytes methods send raw bytes (Content-Type: application/octet-stream)
    /// Tier-based size limits apply.
    /// </summary>
    public class CloudSaveManager : BaseManager<CloudSaveManager>
    {
        /// <summary>
        /// Save data to the cloud.
        /// </summary>
        /// <param name="data">Data to save (UTF-8 string)</param>
        /// <returns>True if save succeeded, false otherwise</returns>
        public async Task<bool> Save(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                HorizonApp.Log.Error("Save data is required");
                return false;
            }

            if (!HasSignedInSession())
            {
                HorizonApp.Log.Error("User must be signed in to save data");
                return false;
            }

            var request = new SaveCloudDataRequest
            {
                userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId,
                saveData = data
            };

            var response = await HorizonApp.Network.PostAsync<SaveCloudSaveResponse>(
                "/api/v1/app/cloud-save/save",
                request,
                useSessionToken: true
            );

            if (response.IsSuccess && response.Data != null && response.Data.success)
            {
                HorizonApp.Log.Info($"Cloud data saved: ({response.Data.dataSizeBytes} bytes)");
                HorizonApp.Events.Publish(EventKeys.CloudSaveDataChanged, request.userId);
                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Cloud save failed: {response.Error}");
                return false;
            }
        }

        /// <summary>
        /// Load data from the cloud.
        /// </summary>
        /// <returns>Loaded data (UTF-8 string), or null if failed</returns>
        public async Task<string> Load()
        {
            if (!HasSignedInSession())
            {
                HorizonApp.Log.Error("User must be signed in to load data");
                return null;
            }

            var request = new LoadCloudDataRequest
            {
                userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId
            };

            var response = await HorizonApp.Network.PostAsync<LoadCloudSaveResponse>(
                "/api/v1/app/cloud-save/load",
                request,
                useSessionToken: true
            );

            if (response.IsSuccess && response.Data != null && response.Data.found)
            {
                string loadedData = response.Data.saveData;
                int sizeBytes = Encoding.UTF8.GetByteCount(loadedData);

                HorizonApp.Log.Info($"Cloud data loaded: ({sizeBytes} bytes)");
                HorizonApp.Events.Publish(EventKeys.CloudSaveDataLoaded, new CloudSaveLoadedData
                {
                    Key = request.userId,
                    Data = loadedData,
                    SizeBytes = sizeBytes,
                    LastModified = ""
                });

                return loadedData;
            }
            else
            {
                HorizonApp.Log.Error($"Cloud load failed: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Save raw binary data to the cloud.
        /// Uses application/octet-stream content type.
        /// </summary>
        /// <param name="data">Raw binary data to save</param>
        /// <returns>True if save succeeded, false otherwise</returns>
        public async Task<bool> SaveBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                HorizonApp.Log.Error("Save data is required");
                return false;
            }

            if (!HasSignedInSession())
            {
                HorizonApp.Log.Error("User must be signed in to save data");
                return false;
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            var response = await HorizonApp.Network.PostBinaryAsync<SaveCloudSaveResponse>(
                $"/api/v1/app/cloud-save/save?userId={userId}",
                data,
                useSessionToken: true
            );

            if (response.IsSuccess && response.Data != null && response.Data.success)
            {
                HorizonApp.Log.Info($"Cloud data saved (binary): ({response.Data.dataSizeBytes} bytes)");
                HorizonApp.Events.Publish(EventKeys.CloudSaveDataChanged, userId);
                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Cloud save (binary) failed: {response.Error}");
                return false;
            }
        }

        /// <summary>
        /// Load raw binary data from the cloud.
        /// Uses application/octet-stream accept header.
        /// </summary>
        /// <returns>Raw binary data, or null if not found or failed</returns>
        public async Task<byte[]> LoadBytes()
        {
            if (!HasSignedInSession())
            {
                HorizonApp.Log.Error("User must be signed in to load data");
                return null;
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            BinaryNetworkResponse response = await HorizonApp.Network.PostForBinaryAsync(
                "/api/v1/app/cloud-save/load",
                new LoadCloudDataRequest { userId = userId },
                useSessionToken: true
            );

            if (response.IsSuccess)
            {
                if (!response.Found)
                {
                    HorizonApp.Log.Info("Cloud data not found (binary)");
                    return null;
                }

                byte[] loadedData = response.Data;
                int sizeBytes = loadedData?.Length ?? 0;

                HorizonApp.Log.Info($"Cloud data loaded (binary): ({sizeBytes} bytes)");
                HorizonApp.Events.Publish(EventKeys.CloudSaveBytesLoaded, new CloudSaveBytesLoadedData
                {
                    Key = userId,
                    Data = loadedData,
                    SizeBytes = sizeBytes
                });

                return loadedData;
            }
            else
            {
                HorizonApp.Log.Error($"Cloud load (binary) failed: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Load the string save together with its server revision, for saving from several devices.
        /// Pass <c>Revision</c> as <c>expectedRevision</c> to <see cref="Save(string, long)"/>.
        /// </summary>
        /// <returns>
        /// The snapshot. <c>Found</c> is false and <c>Revision</c> 0 for an empty slot.
        /// <c>Revision</c> is null when the server sent none: the state is unknown, not empty.
        /// </returns>
        public async Task<CloudSaveSnapshot<string>> LoadSnapshot()
        {
            if (!HasSignedInSession())
            {
                HorizonApp.Log.Error("User must be signed in to load data");
                return new CloudSaveSnapshot<string> { Error = "User must be signed in to load data" };
            }

            var request = new LoadCloudDataRequest
            {
                userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId
            };

            var response = await HorizonApp.Network.PostAsync<LoadCloudSaveResponse>(
                "/api/v1/app/cloud-save/load",
                request,
                useSessionToken: true
            );

            if (!response.IsSuccess || response.Data == null)
            {
                HorizonApp.Log.Error($"Cloud load failed: {response.Error}");
                return new CloudSaveSnapshot<string> { Error = response.Error, StatusCode = response.StatusCode };
            }

            long? revision = CloudSaveRevision.FromHeader(response.GetHeader(CloudSaveRevision.ResponseHeader));
            var snapshot = new CloudSaveSnapshot<string>
            {
                IsSuccess = true,
                Found = response.Data.found,
                Data = response.Data.found ? response.Data.saveData : null,
                Revision = revision,
                StatusCode = response.StatusCode
            };

            if (snapshot.Found)
            {
                int sizeBytes = Encoding.UTF8.GetByteCount(snapshot.Data ?? string.Empty);
                HorizonApp.Log.Info($"Cloud data loaded: ({sizeBytes} bytes, revision {FormatRevision(revision)})");
                HorizonApp.Events.Publish(EventKeys.CloudSaveDataLoaded, new CloudSaveLoadedData
                {
                    Key = request.userId,
                    Data = snapshot.Data,
                    SizeBytes = sizeBytes,
                    LastModified = ""
                });
            }
            else
            {
                HorizonApp.Log.Info($"Cloud data not found (revision {FormatRevision(revision)})");
            }

            return snapshot;
        }

        /// <summary>
        /// Load the binary save together with its server revision, for saving from several devices.
        /// Pass <c>Revision</c> as <c>expectedRevision</c> to <see cref="SaveBytes(byte[], long)"/>.
        /// </summary>
        /// <returns>
        /// The snapshot. <c>Found</c> is false and <c>Revision</c> 0 for an empty slot (HTTP 204).
        /// <c>Revision</c> is null when the server sent none: the state is unknown, not empty.
        /// </returns>
        public async Task<CloudSaveSnapshot<byte[]>> LoadBytesSnapshot()
        {
            if (!HasSignedInSession())
            {
                HorizonApp.Log.Error("User must be signed in to load data");
                return new CloudSaveSnapshot<byte[]> { Error = "User must be signed in to load data" };
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            BinaryNetworkResponse response = await HorizonApp.Network.PostForBinaryAsync(
                "/api/v1/app/cloud-save/load",
                new LoadCloudDataRequest { userId = userId },
                useSessionToken: true
            );

            if (!response.IsSuccess)
            {
                HorizonApp.Log.Error($"Cloud load (binary) failed: {response.Error}");
                return new CloudSaveSnapshot<byte[]> { Error = response.Error, StatusCode = response.StatusCode };
            }

            long? revision = CloudSaveRevision.FromHeader(response.GetHeader(CloudSaveRevision.ResponseHeader));
            var snapshot = new CloudSaveSnapshot<byte[]>
            {
                IsSuccess = true,
                Found = response.Found,
                Data = response.Found ? response.Data : null,
                Revision = revision,
                StatusCode = response.StatusCode
            };

            if (snapshot.Found)
            {
                int sizeBytes = snapshot.Data?.Length ?? 0;
                HorizonApp.Log.Info($"Cloud data loaded (binary): ({sizeBytes} bytes, revision {FormatRevision(revision)})");
                HorizonApp.Events.Publish(EventKeys.CloudSaveBytesLoaded, new CloudSaveBytesLoadedData
                {
                    Key = userId,
                    Data = snapshot.Data,
                    SizeBytes = sizeBytes
                });
            }
            else
            {
                HorizonApp.Log.Info($"Cloud data not found (binary, revision {FormatRevision(revision)})");
            }

            return snapshot;
        }

        /// <summary>
        /// Save a string only if the cloud save is still at <paramref name="expectedRevision"/>
        /// (sends <c>If-Match</c>). When another device saved first, nothing is written and the
        /// result is <see cref="CloudSaveWriteStatus.Conflict"/>: load a new snapshot and decide.
        /// </summary>
        /// <param name="data">Data to save (UTF-8 string)</param>
        /// <param name="expectedRevision">Revision from the last snapshot or save, 0 for an empty slot</param>
        /// <returns>The result with the new revision on success</returns>
        public async Task<CloudSaveWriteResult> Save(string data, long expectedRevision)
        {
            string localError = ValidateConditionalSave(string.IsNullOrEmpty(data), expectedRevision);
            if (localError != null)
            {
                HorizonApp.Log.Error(localError);
                return new CloudSaveWriteResult { Status = CloudSaveWriteStatus.Failed, Error = localError };
            }

            var request = new SaveCloudDataRequest
            {
                userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId,
                saveData = data
            };

            var response = await HorizonApp.Network.PostAsync<SaveCloudSaveResponse>(
                "/api/v1/app/cloud-save/save",
                request,
                useSessionToken: true,
                headers: CloudSaveRevision.IfMatch(expectedRevision)
            );

            return ToWriteResult(response, request.userId, "Cloud save");
        }

        /// <summary>
        /// Save raw bytes only if the cloud save is still at <paramref name="expectedRevision"/>
        /// (sends <c>If-Match</c>). When another device saved first, nothing is written and the
        /// result is <see cref="CloudSaveWriteStatus.Conflict"/>: load a new snapshot and decide.
        /// </summary>
        /// <param name="data">Raw binary data to save</param>
        /// <param name="expectedRevision">Revision from the last snapshot or save, 0 for an empty slot</param>
        /// <returns>The result with the new revision on success</returns>
        public async Task<CloudSaveWriteResult> SaveBytes(byte[] data, long expectedRevision)
        {
            string localError = ValidateConditionalSave(data == null || data.Length == 0, expectedRevision);
            if (localError != null)
            {
                HorizonApp.Log.Error(localError);
                return new CloudSaveWriteResult { Status = CloudSaveWriteStatus.Failed, Error = localError };
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            var response = await HorizonApp.Network.PostBinaryAsync<SaveCloudSaveResponse>(
                $"/api/v1/app/cloud-save/save?userId={userId}",
                data,
                useSessionToken: true,
                headers: CloudSaveRevision.IfMatch(expectedRevision)
            );

            return ToWriteResult(response, userId, "Cloud save (binary)");
        }

        private static string ValidateConditionalSave(bool dataMissing, long expectedRevision)
        {
            if (dataMissing)
            {
                return "Save data is required";
            }
            if (expectedRevision < 0)
            {
                return "expectedRevision must be 0 (empty slot) or a revision from a snapshot or save";
            }
            if (!HasSignedInSession())
            {
                return "User must be signed in to save data";
            }
            return null;
        }

        private static CloudSaveWriteResult ToWriteResult(
            NetworkResponse<SaveCloudSaveResponse> response, string userId, string operation)
        {
            if (response.IsSuccess && response.Data != null && response.Data.success)
            {
                long? revision = CloudSaveRevision.FromSaveResponse(response.Data.revision);
                HorizonApp.Log.Info($"{operation} written: ({response.Data.dataSizeBytes} bytes, revision {FormatRevision(revision)})");
                HorizonApp.Events.Publish(EventKeys.CloudSaveDataChanged, userId);
                return new CloudSaveWriteResult
                {
                    Status = CloudSaveWriteStatus.Saved,
                    Revision = revision,
                    DataSizeBytes = response.Data.dataSizeBytes,
                    StatusCode = response.StatusCode
                };
            }

            if (response.StatusCode == 409)
            {
                const string conflict = "Cloud save changed on another device (HTTP 409). Nothing was written. Load a new snapshot before saving again.";
                HorizonApp.Log.Warning($"{operation} conflict: {conflict}");
                return new CloudSaveWriteResult
                {
                    Status = CloudSaveWriteStatus.Conflict,
                    Error = conflict,
                    StatusCode = response.StatusCode
                };
            }

            HorizonApp.Log.Error($"{operation} failed: {response.Error}");
            return new CloudSaveWriteResult
            {
                Status = CloudSaveWriteStatus.Failed,
                Error = response.Error,
                StatusCode = response.StatusCode
            };
        }

        private static string FormatRevision(long? revision)
        {
            return revision.HasValue ? revision.Value.ToString(CultureInfo.InvariantCulture) : "unknown";
        }

        private static bool HasSignedInSession()
        {
            var user = UserManager.Instance.CurrentUser;
            var sessionToken = HorizonApp.Network.GetSessionToken();
            return UserManager.Instance.IsSignedIn &&
                !string.IsNullOrEmpty(sessionToken) &&
                string.Equals(user.AccessToken, sessionToken, StringComparison.Ordinal);
        }

        /// <summary>
        /// Save an object as JSON to the cloud.
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <param name="obj">Object to save</param>
        /// <returns>True if save succeeded, false otherwise</returns>
        public async Task<bool> SaveObject<T>(T obj)
        {
            string json = JsonHelper.ToJson(obj);
            return await Save(json);
        }

        /// <summary>
        /// Load an object from JSON in the cloud.
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Loaded object, or default if failed</returns>
        public async Task<T> LoadObject<T>()
        {
            string json = await Load();
            return string.IsNullOrEmpty(json) ? default : JsonHelper.FromJson<T>(json);
        }
    }

    /// <summary>
    /// Revision headers of the cloud save endpoints. Server contract checked 2026-10-10
    /// (horizOn-Server AppCloudSaveController, CloudSaveService.saveCloudSave): load answers
    /// <c>X-Cloud-Save-Revision</c> on JSON, bytes and 204 (0 = empty slot), save reads
    /// <c>If-Match</c> as a plain number and answers 409 on a mismatch, a successful save returns
    /// <c>revision</c> of at least 1.
    /// </summary>
    internal static class CloudSaveRevision
    {
        internal const string ResponseHeader = "X-Cloud-Save-Revision";
        internal const string RequestHeader = "If-Match";

        internal static IReadOnlyDictionary<string, string> IfMatch(long expectedRevision)
        {
            return new Dictionary<string, string>
            {
                [RequestHeader] = expectedRevision.ToString(CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Revision from the load header, or null when it is missing or not a number (unknown state).
        /// </summary>
        internal static long? FromHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            return long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long revision)
                ? revision
                : (long?)null;
        }

        /// <summary>
        /// Revision from a successful save. A written save is at least revision 1, so 0 means the
        /// server sent no revision (unknown).
        /// </summary>
        internal static long? FromSaveResponse(long revision)
        {
            return revision > 0 ? revision : (long?)null;
        }
    }

    /// <summary>
    /// Event data for cloud save loaded (string data).
    /// </summary>
    public class CloudSaveLoadedData
    {
        public string Key;
        public string Data;
        public int SizeBytes;
        public string LastModified;
    }

    /// <summary>
    /// Event data for cloud save loaded (binary data).
    /// </summary>
    public class CloudSaveBytesLoadedData
    {
        public string Key;
        public byte[] Data;
        public int SizeBytes;
    }
}
