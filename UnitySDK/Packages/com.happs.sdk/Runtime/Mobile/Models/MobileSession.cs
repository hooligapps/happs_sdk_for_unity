namespace HAppsSDK
{
	public sealed class MobileSession
	{
		public string DeviceId;
        public string Attribution;
		public string AnalyticProvider;
		public string AnalyticKey;
		public string AccessToken;
		public long AccessTokenExpiresAtUtc;
		public string PublicId;
		public bool Verified;
		public bool IsAuthorized;
		public MobileSession Copy() => (MobileSession)MemberwiseClone();
	}
}
