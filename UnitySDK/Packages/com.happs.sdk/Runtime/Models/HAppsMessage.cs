using System;

namespace HAppsSDK
{
	[Serializable]
	public class HAppsMessage
	{
		public string type;
		public HAppsErrorData error;

		public InitData initData;
		public UserData userData;
		public SignatureData signatureData;
		public PaymentData paymentData;
		public AuthPopupData authPopupData;
	}

	[Serializable]
	public class HAppsErrorData
	{
		public string code;
		public string message;

		public override string ToString()
		{
			return string.IsNullOrEmpty(code) ? message : $"{code}: {message}";
		}
	}

	public sealed class HAppsException : Exception
	{
		public string Code { get; }

		public HAppsException(HAppsErrorData error)
			: base(error?.message ?? "HApps operation failed")
		{
			Code = error?.code;
		}
	}
}
