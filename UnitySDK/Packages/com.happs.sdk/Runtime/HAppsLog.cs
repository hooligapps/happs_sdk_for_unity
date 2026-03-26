using UnityEngine;

namespace HAppsSDK
{
	internal static class HAppsLog
	{
		public static bool EnableDebug = true;

		public static void Log(string msg)
		{
			if (!EnableDebug) return;
			Debug.Log("[HApps] " + msg);
		}

		public static void Warn(string msg)
		{
			if (!EnableDebug) return;
			Debug.LogWarning("[HApps] " + msg);
		}

		public static void Error(string msg)
		{
			Debug.LogError("[HApps] " + msg);
		}
	}
}
