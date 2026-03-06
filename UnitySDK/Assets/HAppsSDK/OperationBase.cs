using System;
using System.Threading;
using System.Threading.Tasks;

namespace HAppsSDK
{
	internal abstract class OperationBase
	{
		public abstract void Fail(Exception e);
	}
	
	internal sealed class Operation<T> : OperationBase
	{
		private readonly TaskCompletionSource<T> _tcs;
		private readonly CancellationTokenSource _timeoutCts;

		public Operation(int? timeoutMs)
		{
			_tcs = new TaskCompletionSource<T>(
				TaskCreationOptions.RunContinuationsAsynchronously);

			if (timeoutMs.HasValue)
			{
				_timeoutCts = new CancellationTokenSource(timeoutMs.Value);
				_timeoutCts.Token.Register(OnTimeout);
			}
		}

		public Task<T> Task => _tcs.Task;

		private void OnTimeout()
		{
			_tcs.TrySetException(new TimeoutException("Operation timeout"));
		}

		public void Complete(T result)
		{
			_timeoutCts?.Cancel();
			_timeoutCts?.Dispose();

			_tcs.TrySetResult(result);
		}

		public override void Fail(Exception e)
		{
			_timeoutCts?.Cancel();
			_timeoutCts?.Dispose();

			_tcs.TrySetException(e);
		}
	}
}