namespace HAppsSDK
{
	public sealed class MobileLoginResult
	{
		public bool IsSuccess => string.IsNullOrEmpty(Error);

		public string AccessToken;
		public string IdToken;
		public string OidcAccessToken;
		public string TokenType;
		public int ExpiresIn;
		public string Scope;
		public string PublicId;
		public string SocialId;
		public bool Verified;
		public string DeviceId;

		public string RedirectUri;
		public string Error;
	}
}
