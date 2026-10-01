using System;
using UnityEngine;

namespace HAppsSDK
{
	internal static class HAppsAndroidBrowser
	{
		private const int PaymentAuthTabRequestCode = 47620;

		private static readonly string[] PreferredCustomTabsPackages =
		{
			"com.android.chrome",
			"com.chrome.beta",
			"com.chrome.dev",
			"com.chrome.canary"
		};

		public static void Open(string url)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			try
			{
				using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
				using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using var uriClass = new AndroidJavaClass("android.net.Uri");
				using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url);
				using var builder = new AndroidJavaObject("androidx.browser.customtabs.CustomTabsIntent$Builder");
				builder.Call<AndroidJavaObject>("setSendToExternalDefaultHandlerEnabled", true);
				using var customTab = builder.Call<AndroidJavaObject>("build");
				var customTabsPackage = FindPreferredCustomTabsPackage(activity);
				if (!string.IsNullOrEmpty(customTabsPackage))
				{
					using var intent = customTab.Get<AndroidJavaObject>("intent");
					using var configuredIntent = intent.Call<AndroidJavaObject>("setPackage", customTabsPackage);
				}

				HAppsLog.Log("Opening Android Custom Tab: provider=" + (customTabsPackage ?? "default"));
				customTab.Call("launchUrl", activity, uri);
				return;
			}
			catch (Exception ex)
			{
				HAppsLog.Warn("Unable to open Android Custom Tab; using the system browser. errorType=" + ex.GetType().Name);
			}
#endif
			Application.OpenURL(url);
		}

		public static void OpenAndCloseOnRedirect(string url, string redirectUrl)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			try
			{
				if (!Uri.TryCreate(redirectUrl, UriKind.Absolute, out var redirectUri) ||
					!string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
				{
					throw new ArgumentException("Auth Tab redirect must be an absolute HTTPS URL.", nameof(redirectUrl));
				}

				using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
				using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using var uriClass = new AndroidJavaClass("android.net.Uri");
				using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url);
				using var builder = new AndroidJavaObject("androidx.browser.auth.AuthTabIntent$Builder");
				using var authTab = builder.Call<AndroidJavaObject>("build");
				using var intent = authTab.Get<AndroidJavaObject>("intent");
				using var configuredData = intent.Call<AndroidJavaObject>("setData", uri);
				using var configuredHost = intent.Call<AndroidJavaObject>(
					"putExtra", "androidx.browser.auth.extra.HTTPS_REDIRECT_HOST", redirectUri.Host);
				using var configuredPath = intent.Call<AndroidJavaObject>(
					"putExtra", "androidx.browser.auth.extra.HTTPS_REDIRECT_PATH", redirectUri.AbsolutePath);
				using var configuredFallback = intent.Call<AndroidJavaObject>(
					"putExtra", "android.support.customtabs.extra.SEND_TO_EXTERNAL_HANDLER", true);

				var customTabsPackage = FindPreferredCustomTabsPackage(activity);
				if (!string.IsNullOrEmpty(customTabsPackage))
				{
					using var configuredPackage = intent.Call<AndroidJavaObject>("setPackage", customTabsPackage);
				}

				HAppsLog.Log("Opening Android Auth Tab: provider=" + (customTabsPackage ?? "default"));
				activity.Call("startActivityForResult", intent, PaymentAuthTabRequestCode);
				return;
			}
			catch (Exception ex)
			{
				HAppsLog.Warn("Unable to open Android Auth Tab; using a Custom Tab. errorType=" + ex.GetType().Name);
			}
#endif
			Open(url);
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private static string FindPreferredCustomTabsPackage(AndroidJavaObject activity)
		{
			using var packageManager = activity.Call<AndroidJavaObject>("getPackageManager");

			foreach (var packageName in PreferredCustomTabsPackages)
			{
				try
				{
					using var serviceIntent = new AndroidJavaObject(
						"android.content.Intent",
						"android.support.customtabs.action.CustomTabsService");
					using var configuredIntent = serviceIntent.Call<AndroidJavaObject>("setPackage", packageName);
					using var resolvedService = packageManager.Call<AndroidJavaObject>("resolveService", serviceIntent, 0);
					if (resolvedService != null)
					{
						return packageName;
					}
				}
				catch (Exception)
				{
					// Try the next known provider.
				}
			}

			return null;
		}
#endif
	}
}
