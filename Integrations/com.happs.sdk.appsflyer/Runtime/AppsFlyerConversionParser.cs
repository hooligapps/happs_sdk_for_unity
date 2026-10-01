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
			var customData = Read(fields, "custom_data");
			var parsedCustomData = SplitCustomData(customData);
			var data = new MobileAttributionData
			{
				Provider = "appsflyer", ProviderInstallId = installId, Status = status,
				MediaSource = Read(fields, "media_source"), Campaign = Read(fields, "campaign"),
				CampaignId = Read(fields, "campaign_id"),
				CustomData = customData,
				QueryParams = parsedCustomData.QueryParams,
				Referer = parsedCustomData.Referer,
				HaffCid = Read(fields, "af_sub1"),
				HaffPid = Read(fields, "media_source"),
				UtmCampaign = Read(fields, "campaign"),
				ObservedAt = observedAt
			};
			data.Validate();
			return data;
		}

		private static CustomDataFields SplitCustomData(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return new CustomDataFields();

			var parsed = AFMiniJSON.Json.Deserialize(value) as Dictionary<string, object>;
			if (parsed == null)
				return new CustomDataFields { QueryParams = value };

			var queryParams = new Dictionary<string, object>(parsed);
			var referer = RemoveString(queryParams, "referrer");
			var alternateReferer = RemoveString(queryParams, "referer");
			if (string.IsNullOrEmpty(referer)) referer = alternateReferer;

			return new CustomDataFields
			{
				QueryParams = queryParams.Count == 0 ? string.Empty : AFMiniJSON.Json.Serialize(queryParams),
				Referer = referer
			};
		}

		private static string RemoveString(Dictionary<string, object> values, string key)
		{
			if (!values.TryGetValue(key, out var value))
				return string.Empty;

			values.Remove(key);
			return value as string ?? string.Empty;
		}

		private static string Read(Dictionary<string, object> fields, string key)
		{
			if (!fields.TryGetValue(key, out var value) || value == null)
				return string.Empty;
			if (value is string text) return text;
			if (value is IFormattable number) return number.ToString(null, CultureInfo.InvariantCulture);
			throw new ArgumentException("Unexpected conversion field type.");
		}

		private sealed class CustomDataFields
		{
			public string QueryParams = string.Empty;
			public string Referer = string.Empty;
		}
	}
}
