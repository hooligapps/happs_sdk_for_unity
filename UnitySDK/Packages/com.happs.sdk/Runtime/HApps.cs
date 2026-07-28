using System;
using System.Threading.Tasks;

namespace HAppsSDK
{
	public static class HApps
	{
		private static HAppsProvider _provider;

		public static event Action<UserData, SignatureData> AuthCompleted
		{
			add => Provider.AuthCompleted += value;
			remove
			{
				if (_provider != null)
					_provider.AuthCompleted -= value;
			}
		}

		public static HAppsProvider Provider =>
			_provider ??= new HAppsWebProvider();

		public static Task<bool> Connect()
			=> Provider.Connect();

		public static Task<UserData> GetProfile()
			=> Provider.GetProfile();

		public static Task<PaymentData> MakePayment(string orderId)
			=> Provider.MakePayment(orderId);

		public static Task<AuthPopupData> OpenIdpAuthPopup(string url)
			=> Provider.OpenIdpAuthPopup(url);

		public static Task<bool> OpenPortalAuthPopup()
			=> Provider.OpenPortalAuthPopup();

		public static void OpenAgeVerification(bool adultMode = true)
			=> Provider.OpenAgeVerification(adultMode);

		public static void SetTheaterMode(bool enabled)
			=> Provider.SetTheaterMode(enabled);

		public static bool IsPortalSite()
			=> Provider.IsPortalSite();

		public static bool IsReady()
			=> HAppsJSBridge.IsReady();

		public static void SetDebugLogging(bool enabled)
			=> HAppsLog.SetDebugEnabled(enabled);

		public static void Shutdown()
		{
			HAppsLog.Log("Shutdown");

			_provider?.Dispose();
			_provider = null;
		}
	}
}
