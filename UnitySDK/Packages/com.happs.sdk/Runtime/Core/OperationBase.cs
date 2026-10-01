using System;
using System.Threading;
using System.Threading.Tasks;

namespace HAppsSDK
{
    internal abstract class OperationBase
    {
        public abstract Task UntypedTask { get; }
        public abstract void Fail(Exception error);
        public abstract void Tick(double now);
    }

    // Driven by the bridge's Update, including on WebGL where managed timers do not run.
    internal sealed class Operation<T> : OperationBase
    {
        private readonly TaskCompletionSource<T> _tcs = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly double _deadline;
        private readonly CancellationToken _cancellationToken;

        public Operation(int? timeoutMs, double now, CancellationToken cancellationToken = default)
        {
            _deadline = timeoutMs.HasValue ? now + timeoutMs.Value / 1000d : double.PositiveInfinity;
            _cancellationToken = cancellationToken;
        }

        public Task<T> Task => _tcs.Task;
        public override Task UntypedTask => _tcs.Task;
        public override void Tick(double now)
        {
            if (_cancellationToken.IsCancellationRequested)
                _tcs.TrySetCanceled(_cancellationToken);
            else if (now >= _deadline)
                _tcs.TrySetException(new TimeoutException("Operation timeout"));
        }
        public void Complete(T result) => _tcs.TrySetResult(result);
        public override void Fail(Exception error) => _tcs.TrySetException(error);
    }
}
