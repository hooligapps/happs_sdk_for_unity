using System;

namespace HAppsSDK
{
	public enum PaymentStatus
	{
		Unknown,
		Started,
		Succeeded,
		Fail,
		Cancelled,
		InsufficientFunds,
	}

	[Serializable]
	public class PaymentData
	{
		public string orderId;
		public string transactionId;

		public string status;
		public string error;

		public PaymentStatus Status
		{
			get
			{
				if (string.IsNullOrEmpty(status))
					return PaymentStatus.Unknown;

				return status.ToLowerInvariant() switch
				{
					"started" => PaymentStatus.Started,
					"succeeded" => PaymentStatus.Succeeded,
					"fail" => PaymentStatus.Fail,
					"cancelled" => PaymentStatus.Cancelled,
					"insufficient_funds" => PaymentStatus.InsufficientFunds,
					_ => PaymentStatus.Unknown
				};
			}
		}

		public bool IsSuccess => Status == PaymentStatus.Succeeded;

		public bool IsFailed =>
			Status == PaymentStatus.Fail ||
			Status == PaymentStatus.InsufficientFunds ||
			Status == PaymentStatus.Cancelled;

		public override string ToString()
		{
			return $"OrderId={orderId}, TransactionId={transactionId}, Status={Status}, Error={error}";
		}
	}
}
