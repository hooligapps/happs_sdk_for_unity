using System;
using System.Text;

namespace HAppsSDK
{
	[Serializable]
	public sealed class MobileAttributionData
	{
		public string Provider;
		public string ProviderInstallId;
		public string MediaSource;
		public string Campaign;
		public string CampaignId;
		public string CustomData;
		public string HaffCid;
		public string HaffPid;
		public string UtmCampaign;
		public string Status;
		public long ObservedAt;

		public MobileAttributionData Copy()
			=> (MobileAttributionData)MemberwiseClone();

		public void Validate()
		{
			if (string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(ProviderInstallId))
				throw new ArgumentException("Attribution provider and installation ID are required.");
			if (Status != "pending" && Status != "organic" && Status != "non-organic")
				throw new ArgumentException("Invalid attribution status.");
			if (ObservedAt <= 0)
				throw new ArgumentException("Attribution observation time must be a Unix timestamp in seconds.");
			if (Encoding.UTF8.GetByteCount(CustomData ?? string.Empty) > 16384)
				throw new ArgumentException("Attribution custom data exceeds 16384 UTF-8 bytes.");
			foreach (var value in new[]
			{
				Provider, ProviderInstallId, MediaSource, Campaign, CampaignId,
				HaffCid, HaffPid, UtmCampaign, Status
			})
				if (Encoding.UTF8.GetByteCount(value ?? string.Empty) > 1024)
					throw new ArgumentException("Attribution field exceeds 1024 UTF-8 bytes.");
		}
	}
}
