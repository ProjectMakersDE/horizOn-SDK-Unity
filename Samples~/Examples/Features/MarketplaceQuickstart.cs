using System.Threading.Tasks;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Use an existing authenticated MarketplaceExample with its stable project UUID configured.</summary>
    public static class MarketplaceQuickstart
    {
        public static Task<MarketplaceResult<MarketplaceListingView>> ReadOneListing(MarketplaceExample screen, string listingId)
            => screen.ReadListing(listingId);
        /// <summary>Call from the explicit Buy confirmation after displaying the successfully read listing.</summary>
        public static async Task<bool> BuyReviewedListing(MarketplaceExample screen, string listingId)
            => screen.ReviewBuy(listingId) && await screen.ConfirmReviewedTrade();
        // After uncertainty, call screen.RetryPending() or screen.RecoverOriginalOperation(); never prepare another trade.
    }
}
