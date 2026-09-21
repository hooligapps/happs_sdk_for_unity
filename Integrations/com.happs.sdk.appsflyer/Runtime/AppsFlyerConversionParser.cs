using System;
using System.Collections.Generic;
using System.Globalization;
using AppsFlyerSDK;

namespace HAppsSDK.Attribution
{
	internal static class AppsFlyerConversionParser
	{
		internal static MobileAttributionData Parse(string json, string installId, long observedAt)
		{
			if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
				throw new ArgumentException("Invalid conversion payload size.");
			var fields = AppsFlyer.CallbackStringToDictionary(json);
			if (fields == null)
				throw new ArgumentException("Invalid conversion payload.");
			var status = Read(fields, "af_status").ToLowerInvariant();
			if (status != "organic" && status != "non-organic")
				throw new ArgumentException("Conversion payload has no recognized attribution status.");
			var data = new MobileAttributionData
			{
				Provider = "appsflyer", ProviderInstallId = installId, Status = status,
				MediaSource = Read(fields, "media_source"), Campaign = Read(fields, "campaign"),
				CampaignId = Read(fields, "campaign_id"), ObservedAt = observedAt
			};
			data.Validate();
			return data;
		}

		private static string Read(Dictionary<string, object> fields, string key)
		{
			if (!fields.TryGetValue(key, out var value) || value == null)
				return string.Empty;
			if (value is string text) return text;
			if (value is IFormattable number) return number.ToString(null, CultureInfo.InvariantCulture);
			throw new ArgumentException("Unexpected conversion field type.");
		}
	}
}
