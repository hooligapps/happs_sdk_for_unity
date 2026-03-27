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
		public abstract Task<AuthPopupData> OpenIdpAuthPopup(string url);
		public abstract Task<bool> OpenPortalAuthPopup();
		
		public virtual bool IsPortalSite() => false;

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
		public AuthPopupData authPopupData;
	}

	[Serializable]
	public class UserData
	{
		public string userId;
		public string userName;
		public bool verified;

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

	[Serializable]
	public class AuthPopupData
	{
		public string flow;
		public string ticket;

		public AuthPopupFlow Flow
		{
			get
			{
				if (string.IsNullOrEmpty(flow))
					return AuthPopupFlow.Unknown;

				return flow switch
				{
					"cookie" => AuthPopupFlow.Cookie,
					"ticket" => AuthPopupFlow.Ticket,
					"cancelled" => AuthPopupFlow.Cancelled,
					_ => AuthPopupFlow.Unknown
				};
			}
		}
	}

	public enum AuthPopupFlow
	{
		Unknown,
		Cookie,
		Ticket,
		Cancelled,
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
			return $"OrderId={orderId}, Status={Status}, Error={error}";
		}
	}

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
