using System;
using System.Threading.Tasks;

namespace HAppsSDK
{
	public abstract class HAppsProvider
	{
		protected UserData _userData;
		protected bool _loggedIn;

		public bool IsLoggedIn => _loggedIn;
		public UserData CurrentUser => _userData;

		public abstract Task<UserData> GetProfile();
		public abstract Task<PaymentData> MakePayment(string orderId);
		public virtual void Dispose() { }
	}
}
