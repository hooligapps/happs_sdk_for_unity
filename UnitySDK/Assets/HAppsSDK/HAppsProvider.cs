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
		public abstract Task<UserData> RequestProfile();
		public abstract Task<PaymentData> RequestPayment(PaymentItem item);
		public abstract void OpenAuthPopup();
	}
	
	
		[Serializable]
		public class HAppsMessage
		{
			public string type;

			public InitData initData;
			public UserData userData;
			public SignatureData signatureData;
			public PaymentData paymentData;
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
			Pending,
			Succeeded,
			Failed,
			Cancelled,
			Error
		}

		[Serializable]
		public class PaymentData
		{
			public string paymentId;
			public string itemId;

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
						case "pending": return PaymentStatus.Pending;
						case "succeeded": return PaymentStatus.Succeeded;
						case "failed": return PaymentStatus.Failed;
						case "cancelled":
						case "usercancelled": return PaymentStatus.Cancelled;
						case "error": return PaymentStatus.Error;
						default: return PaymentStatus.Unknown;
					}
				}
			}

			public bool IsSuccess => Status == PaymentStatus.Succeeded;

			public bool IsFailed =>
				Status == PaymentStatus.Failed ||
				Status == PaymentStatus.Error ||
				Status == PaymentStatus.Cancelled;

			public override string ToString()
			{
				return $"PaymentId={paymentId}, Status={Status}, Error={error}";
			}
		}
		
		[Serializable]
		public class PaymentItem
		{
			public string itemId;
			public string itemName;
			public string description;
			public string imageUrl;

			public int quantity;
			public float price;

			public PaymentItem() { }

			public PaymentItem(string itemId, int quantity, float price)
			{
				this.itemId = itemId;
				this.quantity = quantity;
				this.price = price;
			}
		}
		
		[Serializable]
		public class PaymentRequest
		{
			public PaymentItem item;
		}
		
		[Serializable]
		public class PaymentConfirmRequest
		{
			public string paymentId;
		}
}