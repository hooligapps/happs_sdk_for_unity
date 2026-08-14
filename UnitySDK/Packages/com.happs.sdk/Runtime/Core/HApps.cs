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

		public static void SetDebugLogging(bool enabled)
			=> HAppsLog.SetDebugEnabled(enabled);

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
	}
}
