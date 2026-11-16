using System.Threading.Tasks;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Use an existing authenticated ItemGiftsExample with its stable project UUID configured.</summary>
    public static class ItemGiftsQuickstart
    {
        /// <summary>Reads the fee rules and the inventory the review needs. Show both before the player confirms.</summary>
        public static async Task<bool> ReadRulesAndInventory(ItemGiftsExample screen)
            => (await screen.ReadConfig()).IsSuccess && (await screen.ReadInventory()).IsSuccess;
        /// <summary>Call from the explicit Send confirmation after displaying recipient profile, item, fee and the seven-day expiry.</summary>
        public static async Task<bool> SendReviewedGift(ItemGiftsExample screen, string recipientId, string instanceId)
            => screen.ReviewSend(recipientId, instanceId) && await screen.ConfirmReviewedSend();
        // After uncertainty, call screen.RetryPending() or screen.RecoverOriginalOperation(); never prepare another send.
    }
}
