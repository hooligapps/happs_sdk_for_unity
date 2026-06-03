using System;
using UnityEngine;

namespace HAppsSDK
{
	internal sealed class HAppsMobileDeepLinkListener : MonoBehaviour
	{
		public event Action<string> DeepLinkReceived;

		private void Awake()
		{
			DontDestroyOnLoad(gameObject);
			Application.deepLinkActivated += HandleDeepLinkActivated;

			if (!string.IsNullOrEmpty(Application.absoluteURL))
				HandleDeepLinkActivated(Application.absoluteURL);
		}

		private void OnDestroy()
		{
			Application.deepLinkActivated -= HandleDeepLinkActivated;
		}

		private void HandleDeepLinkActivated(string url)
		{
			HAppsLog.Log($"Mobile deep link: {url}");
			DeepLinkReceived?.Invoke(url);
		}
	}
}
