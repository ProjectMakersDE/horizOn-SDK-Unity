using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for sending transactional emails to users via pre-defined templates.
    /// </summary>
    public class EmailSendingManager : BaseManager<EmailSendingManager>
    {
        /// <summary>
        /// Send an email to a registered user using a pre-defined template.
        /// If scheduledAt is provided, the email is scheduled for later delivery.
        /// </summary>
        /// <param name="userId">The horizOn user ID of the recipient</param>
        /// <param name="templateSlug">Template slug defined in Dashboard</param>
        /// <param name="variables">Variable values for the template (empty dict if none)</param>
        /// <param name="language">Language code (e.g., "en", "de")</param>
        /// <param name="scheduledAt">Optional ISO 8601 timestamp for scheduled delivery</param>
        /// <returns>SendEmailResponse with email ID and status, or null on failure</returns>
        public async Task<SendEmailResponse> SendEmail(
            string userId,
            string templateSlug,
            Dictionary<string, string> variables,
            string language,
            DateTime? scheduledAt = null)
        {
            if (string.IsNullOrEmpty(userId))
            {
                HorizonApp.Log.Error("User ID is required");
                return null;
            }

            if (string.IsNullOrEmpty(templateSlug))
            {
                HorizonApp.Log.Error("Template slug is required");
                return null;
            }

            if (string.IsNullOrEmpty(language))
            {
                HorizonApp.Log.Error("Language is required");
                return null;
            }

            var request = new SendEmailRequest
            {
                userId = userId,
                templateSlug = templateSlug,
                variables = variables ?? new Dictionary<string, string>(),
                language = language
            };

            if (scheduledAt.HasValue)
            {
                request.scheduledAt = scheduledAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            }

            var response = await HorizonApp.Network.PostAsync<SendEmailResponse>(
                "/api/v1/app/email-sending/send",
                request,
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                HorizonApp.Log.Info($"Email queued: {response.Data.id}");
                HorizonApp.Events.Publish(EventKeys.EmailSent, response.Data);
                return response.Data;
            }
            else
            {
                HorizonApp.Log.Error($"Email send failed: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Cancel a pending or scheduled email before it is sent.
        /// </summary>
        /// <param name="emailId">The email ID returned by SendEmail</param>
        /// <returns>CancelEmailResponse with confirmation message, or null on failure</returns>
        public async Task<CancelEmailResponse> CancelEmail(string emailId)
        {
            if (string.IsNullOrEmpty(emailId))
            {
                HorizonApp.Log.Error("Email ID is required");
                return null;
            }

            var response = await HorizonApp.Network.DeleteAsync<CancelEmailResponse>(
                $"/api/v1/app/email-sending/{emailId}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                HorizonApp.Log.Info($"Email cancelled: {emailId}");
                HorizonApp.Events.Publish(EventKeys.EmailCancelled, emailId);
                return response.Data;
            }
            else
            {
                HorizonApp.Log.Error($"Email cancel failed: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get the current status of a specific email.
        /// </summary>
        /// <param name="emailId">The email ID returned by SendEmail</param>
        /// <returns>EmailStatusResponse with full status details, or null on failure</returns>
        public async Task<EmailStatusResponse> GetEmailStatus(string emailId)
        {
            if (string.IsNullOrEmpty(emailId))
            {
                HorizonApp.Log.Error("Email ID is required");
                return null;
            }

            var response = await HorizonApp.Network.GetAsync<EmailStatusResponse>(
                $"/api/v1/app/email-sending/{emailId}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                HorizonApp.Log.Info($"Email status: {emailId} = {response.Data.status}");
                HorizonApp.Events.Publish(EventKeys.EmailStatusReceived, response.Data);
                return response.Data;
            }
            else
            {
                HorizonApp.Log.Error($"Email status failed: {response.Error}");
                return null;
            }
        }
    }
}
