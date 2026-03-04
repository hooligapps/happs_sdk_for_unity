using System;
using System.Threading.Tasks;

namespace HAppsSDK
{
    public static class HApps
    {
        private static HAppsProvider _provider;

        public static HAppsProvider Provider =>
            _provider ??= new HAppsWebProvider();

        public static bool IsInitialized => _provider?.IsInitialized == true;
        public static bool IsLoggedIn => _provider?.IsLoggedIn == true;
        public static UserData CurrentUser => _provider?.CurrentUser;
        public static string Signature => _provider?.Signature;

        /// <summary>Fires when user registers or re-authenticates during gameplay.</summary>
        public static event Action<UserData> OnUserChanged
        {
            add => Provider.OnUserChanged += value;
            remove => Provider.OnUserChanged -= value;
        }

        /// <summary>Fires when browser tab visibility changes.</summary>
        public static event Action<bool> OnVisibilityChanged
        {
            add => Provider.OnVisibilityChanged += value;
            remove => Provider.OnVisibilityChanged -= value;
        }

        public static Task<bool> Initialize()
            => Provider.Initialize();

        public static Task<UserData> RequestProfile()
            => Provider.RequestProfile();

        public static Task<PaymentData> RequestPayment(PaymentItem item)
            => Provider.RequestPayment(item);

        public static Task<string> OpenAuthPopup(string url)
            => Provider.OpenAuthPopup(url);

        public static void Destroy()
        {
            _provider?.Destroy();
            _provider = null;
        }
    }
}
