using System.Threading.Tasks;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples
{
    /// <summary>Call after login. Subscribe to FeedChanged for the current online window and
    /// LocalHistoryChanged/LocalHistoryFailed for device history. Never automatically retry a send.</summary>
    public class ChatExample : MonoBehaviour
    {
        public async Task<ChatResult<ChatOpened>> OpenProjectChat(string userId)
        {
            var result = await ChatManager.Instance.OpenChannel(userId, "project");
            if (result.IsSuccess) ChatManager.Instance.WatchChannel(userId, result.Data.channel.id);
            return result;
        }
        // Preserve these original inputs if the caller explicitly checks/retries an uncertain operation.
        // issuedAt uses the server clock from limits/open/sync, advanced by monotonic elapsed time.
        public Task<ChatResult<ChatSent>> Send(string userId, string channelId, string operationId, string issuedAt, string text)
            => ChatManager.Instance.SendMessage(userId, channelId, operationId, issuedAt, text);
        public Task<ChatResult<ChatOperation>> CheckSend(string userId, string operationId)
            => ChatManager.Instance.GetOperation(userId, operationId);
        public ChatResult<ChatHistoryPage> History(string channelId, string cursor = null)
            => ChatManager.Instance.ReadLocalHistory(channelId, cursor, 50);
        public bool ClearHistory(string channelId) => ChatManager.Instance.ClearLocalHistory(channelId);
    }
}
