using System.Threading.Tasks;
using UnityEngine;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for submitting user feedback.
    /// </summary>
    public class FeedbackManager : BaseManager<FeedbackManager>
    {
        /// <summary>
        /// Submit user feedback.
        /// </summary>
        /// <param name="title">Feedback title (required, 1-100 characters)</param>
        /// <param name="category">Feedback category (BUG, FEATURE, GENERAL)</param>
        /// <param name="message">Feedback message</param>
        /// <param name="email">Optional contact email</param>
        /// <param name="includeDeviceInfo">Whether to include device information</param>
        /// <returns>True if submission succeeded, false otherwise</returns>
        public async Task<bool> Submit(
            string title,
            string category,
            string message,
            string email = null,
            bool includeDeviceInfo = true)
        {
            if (string.IsNullOrEmpty(title))
            {
                HorizonApp.Log.Error("Feedback title is required");
                return false;
            }

            if (string.IsNullOrEmpty(message))
            {
                HorizonApp.Log.Error("Feedback message is required");
                return false;
            }

            string deviceInfo = null;
            if (includeDeviceInfo)
            {
                deviceInfo = $"Unity {Application.unityVersion} | " +
                            $"{SystemInfo.operatingSystem} | " +
                            $"{SystemInfo.deviceModel} | " +
                            $"{SystemInfo.graphicsDeviceName}";
            }

            // Get the current user ID if available
            string userId = UserManager.Instance?.CurrentUser?.UserId ?? "";

            var request = new SubmitFeedbackRequest
            {
                userId = userId,
                title = title,
                category = category ?? "GENERAL",
                message = message,
                email = email,
                deviceInfo = deviceInfo
            };

            var response = await HorizonApp.Network.PostAsync<MessageResponse>(
                "/api/v1/app/user-feedback/submit",
                request,
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data.success)
            {
                HorizonApp.Log.Info("Feedback submitted successfully");
                HorizonApp.Events.Publish(EventKeys.FeedbackSubmitted, request);
                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Feedback submission failed: {response.Error ?? response.Data?.message}");
                return false;
            }
        }

        /// <summary>
        /// Submit bug report feedback.
        /// </summary>
        public async Task<bool> ReportBug(string title, string message, string email = null)
        {
            return await Submit(title, "BUG", message, email, includeDeviceInfo: true);
        }

        /// <summary>
        /// Submit feature request feedback.
        /// </summary>
        public async Task<bool> RequestFeature(string title, string message, string email = null)
        {
            return await Submit(title, "FEATURE", message, email, includeDeviceInfo: false);
        }

        /// <summary>
        /// Submit general feedback.
        /// </summary>
        public async Task<bool> SendGeneral(string title, string message, string email = null)
        {
            return await Submit(title, "GENERAL", message, email, includeDeviceInfo: false);
        }
    }
}
