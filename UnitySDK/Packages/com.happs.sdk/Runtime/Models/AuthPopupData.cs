using System;

namespace HAppsSDK
{
	[Serializable]
	public class AuthPopupData
	{
		public string flow;
		public string ticket;

		public AuthPopupFlow Flow
		{
			get
			{
				if (string.IsNullOrEmpty(flow))
					return AuthPopupFlow.Unknown;

				return flow switch
				{
					"cookie" => AuthPopupFlow.Cookie,
					"ticket" => AuthPopupFlow.Ticket,
					"cancelled" => AuthPopupFlow.Cancelled,
					_ => AuthPopupFlow.Unknown
				};
			}
		}
	}

	public enum AuthPopupFlow
	{
		Unknown,
		Cookie,
		Ticket,
		Cancelled,
	}
}
