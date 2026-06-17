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

            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
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
                useSessionToken: false
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
            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
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
                useSessionToken: false
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

            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to save data");
                return false;
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            var response = await HorizonApp.Network.PostBinaryAsync<SaveCloudSaveResponse>(
                $"/api/v1/app/cloud-save/save?userId={userId}",
                data,
                useSessionToken: false
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
            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to load data");
                return null;
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            BinaryNetworkResponse response = await HorizonApp.Network.GetBinaryAsync(
                $"/api/v1/app/cloud-save/load?userId={userId}",
                useSessionToken: false
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
