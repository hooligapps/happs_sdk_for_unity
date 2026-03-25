using System.Threading.Tasks;

namespace HAppsSDK
{
	public static class HApps
	{
		private static HAppsProvider _provider;

		public static HAppsProvider Provider =>
			_provider ??= new HAppsWebProvider();

		public static Task<bool> Initialize()
			=> Provider.Initialize();

		public static Task<UserData> GetProfile()
			=> Provider.GetProfile();

		public static Task<PaymentData> MakePayment(string orderId)
			=> Provider.MakePayment(orderId);

		public static Task<string> OpenIdpAuthPopup(string url)
			=> Provider.OpenIdpAuthPopup(url);

		public static Task<bool> OpenPortalAuthPopup()
			=> Provider.OpenPortalAuthPopup();

		public static bool IsPortalSite()
			=> Provider.IsPortalSite();

		public static void Shutdown()
		{
			HAppsLog.Log("Shutdown");

			_provider?.Dispose();
			_provider = null;
		}
	}
}