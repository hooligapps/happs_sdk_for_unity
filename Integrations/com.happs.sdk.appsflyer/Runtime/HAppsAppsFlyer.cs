using System;
using AppsFlyerSDK;
using UnityEngine;
using UnityEngine.Scripting;

namespace HAppsSDK.Attribution
{
	[Preserve]
	public sealed class HAppsAppsFlyer : MonoBehaviour, IAppsFlyerConversionData
	{
		private static HAppsAppsFlyer _instance;
		private HAppsMobileProvider _mobile;
		private string _scope;
		private string _cacheKey;
		private bool _external;
		private bool _manageCustomerId;
		private bool _collectAdvertisingIdentifiers;
		private bool _debugLogging;
		private bool _started;
		private bool _startRequested;
		private bool _flushing;
		private float _nextPoll;
		private float _nextFlush;
		private float _retryDelay = 2;
		private string _installId;
		private string _customerId;
		private string _deviceId;
		private string _pendingConversion;
		private MobileAttributionData _data;

		public static void Initialize(HAppsAppsFlyerOptions options)
		{
			if (options == null) throw new ArgumentNullException(nameof(options));
			if (_instance != null) throw new InvalidOperationException("HApps AppsFlyer is already initialized.");
			var mobile = HApps.Mobile;
			var scope = mobile.AttributionStorageScope;
#if UNITY_ANDROID && !UNITY_EDITOR
			var go = new GameObject("HAppsAppsFlyerCallbacks");
			DontDestroyOnLoad(go);
			var adapter = go.AddComponent<HAppsAppsFlyer>();
			adapter._mobile = mobile;
			adapter._scope = scope;
			adapter._cacheKey = scope + ".appsflyer.attribution.v1";
			adapter._external = options.UseExistingSdk;
			adapter._manageCustomerId = options.ManageCustomerUserId;
			adapter._collectAdvertisingIdentifiers = options.CollectAdvertisingIdentifiers;
			adapter._debugLogging = options.DebugLogging;
			_instance = adapter;
#else
			throw new PlatformNotSupportedException("HApps AppsFlyer tracking requires an Android player.");
#endif
		}

		public static void StartTracking()
		{
			var adapter = RequireInstance();
			adapter._startRequested = true;
			adapter.Poll();
		}

		public static void RecordConversionData(string json)
		{
			var adapter = RequireInstance();
			adapter.ReceiveConversion(json);
		}

		private static HAppsAppsFlyer RequireInstance()
			=> _instance != null ? _instance : throw new InvalidOperationException("Initialize HApps AppsFlyer first.");

		private void Update()
		{
			if (!_startRequested || Time.realtimeSinceStartup < _nextPoll) return;
			_nextPoll = Time.realtimeSinceStartup + 1;
			try { Poll(); }
			catch (Exception ex) { Warn(ex); }
		}

		private void Poll()
		{
			if (_mobile.IsDisposed || _mobile.AttributionStorageScope != _scope)
			{
				if (_manageCustomerId && !string.IsNullOrEmpty(_installId))
					AppsFlyer.setCustomerUserId(_installId);
				enabled = false;
				return;
			}
			if (!_started)
			{
				var initialSession = _mobile.CurrentSession;
				if (initialSession == null) return;
				if (!_external)
				{
					if (string.IsNullOrWhiteSpace(initialSession.AppsFlyerKey)) return;
					AppsFlyer.setIsDebug(_debugLogging);
					AppsFlyer.setDisableAdvertisingIdentifiers(!_collectAdvertisingIdentifiers);
					AppsFlyer.initSDK(initialSession.AppsFlyerKey, null, this);
				}
				if (_manageCustomerId)
				{
					AppsFlyer.setCustomerUserId(initialSession.PublicId);
					_customerId = initialSession.PublicId;
				}
				if (!_external) AppsFlyer.startSDK();
				_started = true;
			}
			var installId = AppsFlyer.getAppsFlyerId();
			if (string.IsNullOrWhiteSpace(installId)) return;
			if (_installId != installId)
			{
				_installId = installId;
				LoadCache();
			}
			if (_pendingConversion != null)
			{
				var json = _pendingConversion;
				_pendingConversion = null;
				ApplyConversion(json);
			}
			var session = _mobile.CurrentSession;
			var customerId = session?.PublicId ?? _installId;
			if (_manageCustomerId && _customerId != customerId)
			{
				AppsFlyer.setCustomerUserId(customerId);
				_customerId = customerId;
			}
			if (_deviceId != session?.DeviceId)
			{
				_deviceId = session?.DeviceId;
				_nextFlush = 0;
				_retryDelay = 2;
			}
			if (session != null && !_flushing && Time.realtimeSinceStartup >= _nextFlush)
				Flush();
		}

		private void LoadCache()
		{
			_data = null;
			try
			{
				var json = PlayerPrefs.GetString(_cacheKey, string.Empty);
				if (!string.IsNullOrEmpty(json))
				{
					var cached = JsonUtility.FromJson<MobileAttributionData>(json);
					cached?.Validate();
					if (cached?.Provider == "appsflyer" && cached.ProviderInstallId == _installId)
						_data = cached;
				}
			}
			catch (Exception ex) { Warn(ex); }
			if (_data == null)
				_data = new MobileAttributionData
				{
					Provider = "appsflyer", ProviderInstallId = _installId, Status = "pending",
					ObservedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
				};
			Publish();
		}

		private void ReceiveConversion(string json)
		{
			if (_mobile == null || _mobile.IsDisposed || _mobile.AttributionStorageScope != _scope) return;
			if (string.IsNullOrEmpty(json) || json.Length > 65536) return;
			if (string.IsNullOrEmpty(_installId)) { _pendingConversion = json; return; }
			ApplyConversion(json);
		}

		private void ApplyConversion(string json)
		{
			try
			{
				var next = AppsFlyerConversionParser.Parse(json, _installId,
					_data?.ObservedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds());
				if (_data != null && JsonUtility.ToJson(next) == JsonUtility.ToJson(_data)) return;
				next.ObservedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				_data = next;
				Publish();
			}
			catch (Exception ex) { Warn(ex); }
		}

		private void Publish()
		{
			_mobile.SetAttribution(_data);
			_nextFlush = 0;
			PlayerPrefs.SetString(_cacheKey, JsonUtility.ToJson(_data));
			PlayerPrefs.Save();
		}

		private async void Flush()
		{
			_flushing = true;
			try
			{
				await _mobile.FlushAttributionAsync();
				_retryDelay = 2;
				_nextFlush = Time.realtimeSinceStartup + 30;
			}
			catch (Exception ex)
			{
				Warn(ex);
				_nextFlush = Time.realtimeSinceStartup + _retryDelay;
				_retryDelay = Math.Min(60, _retryDelay * 2);
			}
			finally { _flushing = false; }
		}

		private static void Warn(Exception ex)
			=> Debug.LogWarning("[HAppsAppsFlyer] Attribution operation failed: " + ex.GetType().Name);

		[Preserve] public void onConversionDataSuccess(string data) => ReceiveConversion(data);
		[Preserve] public void onConversionDataFail(string error)
			=> Debug.LogWarning("[HAppsAppsFlyer] Conversion data unavailable; attribution is not marked organic.");
		[Preserve] public void onAppOpenAttribution(string data) { }
		[Preserve] public void onAppOpenAttributionFailure(string error) { }

		private void OnDestroy()
		{
			if (_instance == this) _instance = null;
		}
	}
}
