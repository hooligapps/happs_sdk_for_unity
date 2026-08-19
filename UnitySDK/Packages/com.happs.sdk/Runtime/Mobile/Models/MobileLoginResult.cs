namespace HAppsSDK
{
	public sealed class MobileLoginResult
	{
		public bool IsSuccess => string.IsNullOrEmpty(Error);

		public string AccessToken;
		public string IdToken;
		public string RefreshToken;
		public string TokenType;
		public int ExpiresIn;
		public string Scope;
		public string PublicId;
		public bool Verified;
		public string PortalAccessToken;
		public string PortalRefreshToken;

		public string Code;
		public string CodeVerifier;
		public string RedirectUri;
		public string Error;
	}
}
