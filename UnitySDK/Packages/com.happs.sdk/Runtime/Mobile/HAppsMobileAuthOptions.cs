namespace HAppsSDK
{
	public sealed class HAppsMobileAuthOptions
	{
		public string Authority;
		public string ClientId;
		public string RedirectUri;
		public string PostLogoutRedirectUri;
		public string Scope;
		public string DeviceRegisterUrl;
		public string InitSessionUrl;
		public string OidcStartUrl;
		public string OidcExchangeUrl;
		public string OidcLogoutUrl;
		public string CreatePaymentUrl;
		public int LoginTimeoutMs = 180000;
		public string PlayerPrefsStorageKey = "happs.mobile.state.v1";
	}
}
