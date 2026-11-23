namespace HAppsSDK
{
	public enum AuthAction
	{
		Unknown,
		SignUp,
		Linked,
		Login,
		Cancel,
	}

	internal static class AuthActionParser
	{
		public static AuthAction Parse(string value)
		{
			return value switch
			{
				"sign_up" => AuthAction.SignUp,
				"linked" => AuthAction.Linked,
				"login" => AuthAction.Login,
				"cancel" => AuthAction.Cancel,
				_ => AuthAction.Unknown,
			};
		}
	}
}
