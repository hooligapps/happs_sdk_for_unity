using System;
using System.Threading.Tasks;

namespace HAppsSDK
{
    public abstract class HAppsProvider
    {
        protected UserData _userData;
        protected bool _loggedIn;
        protected bool _isInitialized;

        public string Signature { get; protected set; }

        public bool IsInitialized => _isInitialized;
        public bool IsLoggedIn => _loggedIn;
        public UserData CurrentUser => _userData;

        public event Action<UserData> OnUserChanged;
        public event Action<bool> OnVisibilityChanged;

        protected void NotifyUserChanged(UserData user) => OnUserChanged?.Invoke(user);
        protected void NotifyVisibilityChanged(bool visible) => OnVisibilityChanged?.Invoke(visible);

        public abstract Task<bool> Initialize();
        public abstract Task<UserData> RequestProfile();
        public abstract Task<PaymentData> RequestPayment(PaymentItem item);
        public abstract Task<string> OpenAuthPopup(string url);
        public abstract void Destroy();
    }
}
