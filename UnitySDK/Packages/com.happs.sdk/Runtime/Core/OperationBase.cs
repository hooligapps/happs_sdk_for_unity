using System;
using System.Threading;
using System.Threading.Tasks;

namespace HAppsSDK
{
	internal abstract class OperationBase
	{
		public abstract Task UntypedTask { get; }
		public abstract void Fail(Exception e);
	}
	
	internal sealed class Operation<T> : OperationBase
	{
		private readonly TaskCompletionSource<T> _tcs;
		private readonly CancellationTokenSource _timeoutCts;
		private readonly CancellationTokenRegistration _timeoutRegistration;

		public Operation(int? timeoutMs)
		{
			_tcs = new TaskCompletionSource<T>(
				TaskCreationOptions.RunContinuationsAsynchronously);

			if (timeoutMs.HasValue)
			{
				_timeoutCts = new CancellationTokenSource(timeoutMs.Value);
				_timeoutRegistration = _timeoutCts.Token.Register(OnTimeout);
			}
		}

		public Task<T> Task => _tcs.Task;
		public override Task UntypedTask => _tcs.Task;

		private void OnTimeout()
		{
			if (_tcs.Task.IsCompleted)
				return;

			_tcs.TrySetException(new TimeoutException("Operation timeout"));
		}

		public void Complete(T result)
		{
			_timeoutRegistration.Dispose();
			_timeoutCts?.Dispose();

			_tcs.TrySetResult(result);
		}

		public override void Fail(Exception e)
		{
			_timeoutRegistration.Dispose();
			_timeoutCts?.Dispose();

			_tcs.TrySetException(e);
		}
	}
}
