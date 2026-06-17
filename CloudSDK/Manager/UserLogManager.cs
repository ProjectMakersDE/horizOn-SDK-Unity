using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for creating user logs on the server.
    /// Tracks application events and errors for monitoring and debugging.
    /// Note: Not available for FREE tier accounts.
    /// </summary>
    public class UserLogManager : BaseManager<UserLogManager>
    {
        /// <summary>
        /// Create a log entry on the server.
        /// </summary>
        /// <param name="type">Log type (INFO, WARN, ERROR)</param>
        /// <param name="message">Log message (max 1000 characters)</param>
        /// <param name="errorCode">Optional error code (max 50 characters)</param>
        /// <returns>CreateUserLogResponse with id and createdAt if successful, null otherwise</returns>
        public async Task<CreateUserLogResponse> CreateLog(
            LogType type,
            string message,
            string errorCode = null)
        {
            // Validate message
            if (string.IsNullOrEmpty(message))
            {
                HorizonApp.Log.Error("Log message is required");
                return null;
            }

            if (message.Length > 1000)
            {
                HorizonApp.Log.Warning("Log message exceeds 1000 characters, truncating");
                message = message.Substring(0, 1000);
            }

            // Validate error code length
            if (!string.IsNullOrEmpty(errorCode) && errorCode.Length > 50)
            {
                HorizonApp.Log.Warning("Error code exceeds 50 characters, truncating");
                errorCode = errorCode.Substring(0, 50);
            }

            // Ensure user is signed in
            if (UserManager.Instance == null || !UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to create logs");
                return null;
            }

            var request = new CreateUserLogRequest
            {
                message = message,
                type = type.ToString(),
                userId = UserManager.Instance.CurrentUser.UserId,
                errorCode = errorCode
            };

            var response = await HorizonApp.Network.PostAsync<CreateUserLogResponse>(
                "/api/v1/app/user-logs/create",
                request,
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null && !string.IsNullOrEmpty(response.Data.id))
            {
                HorizonApp.Events.Publish(EventKeys.LogCreated, response.Data);
                return response.Data;
            }
            else
            {
                string errorMsg = response.Error ?? "Unknown error";

                // Handle specific error codes
                if (response.StatusCode == 403)
                {
                    errorMsg = "User log feature is not available for FREE accounts";
                }
                else if (response.StatusCode == 429)
                {
                    errorMsg = "Rate limit exceeded. Please try again later.";
                }

                HorizonApp.Log.Warning($"User log creation failed: {errorMsg}");
                return null;
            }
        }

        /// <summary>
        /// Create an INFO log entry.
        /// </summary>
        /// <param name="message">Log message (max 1000 characters)</param>
        /// <param name="errorCode">Optional error code</param>
        /// <returns>CreateUserLogResponse if successful, null otherwise</returns>
        public async Task<CreateUserLogResponse> Info(string message, string errorCode = null)
        {
            return await CreateLog(LogType.INFO, message, errorCode);
        }

        /// <summary>
        /// Create a WARN log entry.
        /// </summary>
        /// <param name="message">Log message (max 1000 characters)</param>
        /// <param name="errorCode">Optional error code</param>
        /// <returns>CreateUserLogResponse if successful, null otherwise</returns>
        public async Task<CreateUserLogResponse> Warn(string message, string errorCode = null)
        {
            return await CreateLog(LogType.WARN, message, errorCode);
        }

        /// <summary>
        /// Create an ERROR log entry.
        /// </summary>
        /// <param name="message">Log message (max 1000 characters)</param>
        /// <param name="errorCode">Optional error code</param>
        /// <returns>CreateUserLogResponse if successful, null otherwise</returns>
        public async Task<CreateUserLogResponse> Error(string message, string errorCode = null)
        {
            return await CreateLog(LogType.ERROR, message, errorCode);
        }
    }
}
