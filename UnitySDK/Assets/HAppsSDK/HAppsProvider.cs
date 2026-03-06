using System;
using System.Threading.Tasks;

namespace HAppsSDK
{
	public abstract class HAppsProvider
	{
		protected UserData _userData;
		protected bool _loggedIn;
		protected bool _isInitialized;

		public string Signature { get; protected set; }

		public bool IsInitialized => _isInitialized;
		public bool IsLoggedIn => _loggedIn;
		public UserData CurrentUser => _userData;

		public abstract Task<bool> Initialize();
		public abstract Task<UserData> GetProfile();
		public abstract Task<PaymentData> MakePayment(string orderId);
		public abstract Task<string> OpenIdpAuthPopup(string url);
		public abstract Task<bool> OpenPortalAuthPopup();

		public virtual void Dispose() { }
    }

	[Serializable]
	public class HAppsMessage
	{
		public string type;

		public InitData initData;
		public UserData userData;
		public SignatureData signatureData;
		public PaymentData paymentData;
		public string authTicket;
	}

	[Serializable]
	public class UserData
	{
		public string userId;
		public string userName;

		public override string ToString()
		{
			return $"{userName} ({userId})";
		}
	}

	[Serializable]
	public class SignatureData
	{
		public string signature;
	}

	[Serializable]
	public class InitData
	{
		public bool ready;
		public bool fromPlatform;
		public bool isMobileWeb;
	}

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

		public string status;
		public string error;

		public PaymentStatus Status
		{
			get
			{
				if (string.IsNullOrEmpty(status))
					return PaymentStatus.Unknown;

				switch (status.ToLowerInvariant())
				{
					case "started": return PaymentStatus.Started;
					case "succeeded": return PaymentStatus.Succeeded;
					case "fail": return PaymentStatus.Fail;
					case "cancelled": return PaymentStatus.Cancelled;
					case "insufficient_funds": return PaymentStatus.InsufficientFunds;
					
					default: return PaymentStatus.Unknown;
				}
			}
		}

		public bool IsSuccess => Status == PaymentStatus.Succeeded;

		public bool IsFailed =>
			Status == PaymentStatus.Fail ||
			Status == PaymentStatus.InsufficientFunds ||
			Status == PaymentStatus.Cancelled;

		public override string ToString()
		{
			return $"OrderId={orderId}, Status={Status}, Error={error}";
		}
	}

	// [Serializable]
	// public class PaymentItem
	// {
	// 	public string itemId;
	// 	public string itemName;
	// 	public string description;
	// 	public string imageUrl;
	//
	// 	public int quantity;
	// 	public float price;
	//
	// 	public PaymentItem()
	// 	{
	// 	}
	//
	// 	public PaymentItem(string itemId, int quantity, float price)
	// 	{
	// 		this.itemId = itemId;
	// 		this.quantity = quantity;
	// 		this.price = price;
	// 	}
	// }

	[Serializable]
	public class PaymentRequest
	{
		public string orderId;
	}

	[Serializable]
	public class PaymentConfirmRequest
	{
		public string orderId;
	}

	[Serializable]
	public class OpenAuthPopupRequest
	{
		public string url;
	}
}