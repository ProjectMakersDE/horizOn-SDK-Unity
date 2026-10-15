using System;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Objects.Network.Responses;
namespace PM.horizOn.Cloud.Manager
{
    /// <summary>One active read and one coalesced follow-up. No background polling.</summary>
    internal sealed class InboxRefreshQueue
    {
        private sealed class Read
        {
            public Func<Task<InboxResult>> Action;
            public readonly TaskCompletionSource<InboxResult> Result = new TaskCompletionSource<InboxResult>();
        }
        private Read _next;
        private bool _running;
        public Task<InboxResult> Enqueue(Func<Task<InboxResult>> action)
        {
            var next = _next ?? (_next = new Read());
            next.Action = action;
            if (!_running) { _running = true; _ = Drain(); }
            return next.Result.Task;
        }
        private async Task Drain()
        {
            while (_next != null)
            {
                var read = _next; _next = null;
                try { read.Result.TrySetResult(await read.Action()); }
                catch (Exception) { read.Result.TrySetResult(new InboxResult { ErrorCode = "NETWORK_ERROR", Error = "Inbox request failed" }); }
            }
            _running = false;
        }
    }
}
