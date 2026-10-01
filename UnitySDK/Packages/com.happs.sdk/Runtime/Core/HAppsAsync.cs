using System.Threading;
using System.Threading.Tasks;

namespace HAppsSDK
{
    internal static class HAppsAsync
    {
        // Cancels this waiter, never a shared operation owned by another caller.
        public static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cancellationToken.CanBeCanceled) return await task;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task)
                    cancellationToken.ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();
                return await task;
            }
        }
    }
}
