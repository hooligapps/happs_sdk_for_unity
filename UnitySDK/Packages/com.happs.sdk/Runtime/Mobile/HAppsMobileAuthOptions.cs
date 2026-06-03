namespace HAppsSDK
{
	public sealed class HAppsMobileAuthOptions
	{
		public string Authority;
		public string ClientId;
		public string RedirectUri;
		public string PostLogoutRedirectUri;
		public string Scope;
		public int LoginTimeoutMs = 180000;
	}
}
