using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Gift Codes.
    ///
    /// What it does: connects, signs in anonymously, validates a gift code, then
    /// redeems it. Redeeming consumes the code, so a code can only be used once.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// create a gift code in the horizOn Dashboard, set it on this component, then
    /// attach the script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Code valid: True" then "Code redeemed" with gift data.
    ///
    /// Reference: docs/wiki/sdks/features/gift-codes.md
    /// </summary>
    public class GiftCodesExample : MonoBehaviour
    {
        [SerializeField] private string giftCode = "PROMO2026";

        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[GiftCodesExample] Could not connect to horizOn");
                    return;
                }

                // Gift code calls require a signed in user.
                if (!await UserManager.Instance.SignUpAnonymous("Player1"))
                {
                    Debug.LogError("[GiftCodesExample] Anonymous sign up failed");
                    return;
                }

                // Validate returns null on a request error, true or false otherwise.
                bool? valid = await GiftCodeManager.Instance.Validate(giftCode);
                if (valid == null)
                {
                    Debug.LogError("[GiftCodesExample] Gift code validation request failed");
                    return;
                }

                Debug.Log($"[GiftCodesExample] Code valid: {valid.Value}");
                if (!valid.Value)
                {
                    return;
                }

                var result = await GiftCodeManager.Instance.Redeem(giftCode);
                if (result != null && result.success)
                {
                    Debug.Log($"[GiftCodesExample] Code redeemed, gift data: {result.giftData}");
                }
                else
                {
                    Debug.LogWarning("[GiftCodesExample] Gift code redemption failed");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[GiftCodesExample] Unexpected error: {e.Message}");
            }
        }
    }
}
