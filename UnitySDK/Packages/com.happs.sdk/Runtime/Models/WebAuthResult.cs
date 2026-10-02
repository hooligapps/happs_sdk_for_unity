namespace HAppsSDK
{
	public sealed class WebAuthResult
	{
		public bool IsSuccess { get; }
		public UserData User { get; }
		public SignatureData Signature { get; }
		public AuthAction Action { get; }

		internal WebAuthResult(
			bool isSuccess,
			UserData user,
			SignatureData signature,
			AuthAction action)
		{
			IsSuccess = isSuccess;
			User = user;
			Signature = signature;
			Action = action;
		}
	}
}
