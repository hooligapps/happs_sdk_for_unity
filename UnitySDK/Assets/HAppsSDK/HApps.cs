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

		public static Task<UserData> RequestProfile()
			=> Provider.RequestProfile();

		public static Task<PaymentData> RequestPayment(PaymentItem item)
			=> Provider.RequestPayment(item);

		public static Task<string> OpenAuthPopup(string url)
			=> Provider.OpenAuthPopup(url);
	}
}