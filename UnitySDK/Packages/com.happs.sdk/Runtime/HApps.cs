using System;
using System.Threading.Tasks;

namespace HAppsSDK
{
	public static class HApps
	{
		private static HAppsWebProvider _web;
		private static HAppsMobileProvider _mobile;

		public static HAppsWebProvider Web =>
			_web ??= new HAppsWebProvider();

		public static HAppsMobileProvider Mobile =>
			_mobile ??= new HAppsMobileProvider();

		// Backward-compatible alias for existing Web integrations.
		public static HAppsWebProvider Provider => Web;

		public static event Action<UserData, SignatureData> AuthCompleted
		{
			add => Web.AuthCompleted += value;
			remove
			{
				if (_web != null)
					_web.AuthCompleted -= value;
			}
		}

		public static Task<bool> Connect()
			=> Web.Connect();

		public static Task<UserData> GetProfile()
			=> Current.GetProfile();

		public static Task<PaymentData> MakePayment(string orderId)
			=> Current.MakePayment(orderId);

		public static Task<AuthPopupData> OpenIdpAuthPopup(string url)
			=> Web.OpenIdpAuthPopup(url);

		public static Task<bool> OpenPortalAuthPopup()
			=> Web.OpenPortalAuthPopup();

		public static bool IsPortalSite()
			=> Web.IsPortalSite();

		public static bool IsReady()
			=> Web.IsReady();

		public static void ConfigureMobile(HAppsMobileAuthOptions options, IMobileTokenStore tokenStore = null)
			=> Mobile.Configure(options, tokenStore);

		public static void Shutdown()
		{
			HAppsLog.Log("Shutdown");

			_web?.Dispose();
			_web = null;

			_mobile?.Dispose();
			_mobile = null;
		}

		private static HAppsProvider Current
		{
			get
			{
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
				return Mobile;
#else
				return Web;
#endif
			}
		}
	}
}
