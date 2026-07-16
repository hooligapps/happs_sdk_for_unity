namespace HAppsSDK
{
	public sealed class HAppsMobileAuthOptions
	{
		public string Authority;
		public string ClientId;
		public string RedirectUri;
		public string PostLogoutRedirectUri;
		public string Scope;
		public string InitSessionUrl;
		public string RefreshSessionUrl;
		public string CreatePaymentUrl;
		public int LoginTimeoutMs = 180000;
	}
}
