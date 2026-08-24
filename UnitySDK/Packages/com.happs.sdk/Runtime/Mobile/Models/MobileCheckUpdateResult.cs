namespace HAppsSDK
{
	public sealed class MobileCheckUpdateResult
	{
		public bool UpdateAvailable;
		public bool Required;
		public int LatestVersionCode;
		public string LatestVersionName;
		public string DownloadUrl;
		public string Sha256;
		public string ReleaseNotes;
	}
}
