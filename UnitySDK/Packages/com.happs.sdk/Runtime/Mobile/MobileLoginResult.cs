namespace HAppsSDK
{
	public sealed class MobileLoginResult
	{
		public bool IsSuccess => string.IsNullOrEmpty(Error);

		public string AccessToken;
		public string IdToken;
		public string RefreshToken;
		public string Error;
		public UserData User;
	}
}
