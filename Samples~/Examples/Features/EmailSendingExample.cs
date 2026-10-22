using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Email Sending (transactional).
    ///
    /// What it does: connects, sends one transactional email to an explicitly
    /// configured recipient using a Dashboard template, then queries its status.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// create a "welcome" email template in the horizOn Dashboard, then attach this
    /// script to an empty GameObject. Set Recipient User Id in the Inspector to an
    /// existing active, email-verified user with a nonempty email in the same project,
    /// then press Play. Anonymous users are not eligible. This sample creates no user.
    /// Expected Debug.Log output: "Email queued: <id>" then "Email status: <status>".
    ///
    /// Reference: docs/wiki/sdks/features/email-sending.md
    /// </summary>
    public class EmailSendingExample : MonoBehaviour
    {
        [SerializeField] private string _recipientUserId = "";
        [SerializeField] private string templateSlug = "welcome";
        [SerializeField] private string language = "en";

        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                if (string.IsNullOrWhiteSpace(_recipientUserId))
                {
                    HorizonApp.Log.Error("[EmailSendingExample] Set Recipient User Id to an active, email-verified user in this project");
                    return;
                }

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[EmailSendingExample] Could not connect to horizOn");
                    return;
                }

                var variables = new Dictionary<string, string> { { "username", "Player1" } };

                var sent = await EmailSendingManager.Instance.SendEmail(_recipientUserId, templateSlug, variables, language);
                if (sent == null)
                {
                    Debug.LogError("[EmailSendingExample] Email send failed");
                    return;
                }

                Debug.Log($"[EmailSendingExample] Email queued: {sent.id}");

                var status = await EmailSendingManager.Instance.GetEmailStatus(sent.id);
                if (status != null)
                {
                    Debug.Log($"[EmailSendingExample] Email status: {status.status}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[EmailSendingExample] Unexpected error: {e.Message}");
            }
        }
    }
}
