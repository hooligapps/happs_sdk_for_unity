using System.Threading.Tasks;

namespace HAppsSDK
{
    internal sealed class MobileLoginCallback
    {
        private readonly string _redirectUri;
        private readonly string _state;
        private readonly TaskCompletionSource<string> _completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> Task => _completion.Task;

        public MobileLoginCallback(string redirectUri, string state)
        {
            _redirectUri = redirectUri;
            _state = state;
        }

        public bool TryAccept(string url)
        {
            if (!HAppsMobileProvider.TryGetLoginCallback(url, _redirectUri, _state, out var accepted)) return false;
            return _completion.TrySetResult(accepted);
        }
    }
}
