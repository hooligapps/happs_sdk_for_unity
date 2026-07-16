using System;

namespace HAppsSDK
{
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
}
