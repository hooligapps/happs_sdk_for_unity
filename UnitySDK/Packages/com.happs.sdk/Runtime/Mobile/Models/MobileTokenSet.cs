namespace HAppsSDK
{
	[System.Serializable]
	public sealed class MobileTokenSet
	{
		public string DeviceId;
		public string DeviceKeyAlias;
		public string DevicePrivateKey;
		public string DevicePublicKey;
		public string AccessToken;
		public long AccessTokenExpiresAtUtc;
		public string IdToken;
		public string OidcAccessToken;
		public string PublicId;
		public string SocialId;
		public bool Verified;
	}
}
