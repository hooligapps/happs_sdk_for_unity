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

		public virtual Task<bool> Connect()
			=> throw new NotSupportedException("Connect is not supported by this provider.");

		public abstract Task<UserData> GetProfile();
		public abstract Task<PaymentData> MakePayment(string orderId);
		public virtual Task<AuthPopupData> OpenIdpAuthPopup(string url)
			=> throw new NotSupportedException("OpenIdpAuthPopup is not supported by this provider.");

		public virtual Task<bool> OpenPortalAuthPopup()
			=> throw new NotSupportedException("OpenPortalAuthPopup is not supported by this provider.");

		public virtual void OpenAgeVerification(bool adultMode = true) { }
		public virtual void SetTheaterMode(bool enabled) { }
		public virtual bool IsPortalSite() => false;

		public virtual void Dispose() { }
	}
}
