using System;

namespace HAppsSDK
{
	[Serializable]
	public class UserData
	{
		public string userId;
		public string userName;
		public bool verified;

		public override string ToString()
		{
			return $"{userName} ({userId})";
		}
	}
}
