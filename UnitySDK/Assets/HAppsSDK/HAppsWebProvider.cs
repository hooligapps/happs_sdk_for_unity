using System;
using System.Threading.Tasks;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsWebProvider : HAppsProvider
    {
        private enum OperationType
        {
            None,
            Init,
            Profile,
            Payment,
            AuthTicket,
        }

        private const int DEFAULT_TIMEOUT_MS = 30000;

        private readonly HAppsJSBridge _bridge;
        private readonly GameObject _bridgeGo;

        private OperationType _currentOperation = OperationType.None;
        private TaskCompletionSource<object> _currentTcs;

        public HAppsWebProvider()
        {
            _bridgeGo = new GameObject("HAppsManager");
            UnityEngine.Object.DontDestroyOnLoad(_bridgeGo);

            _bridge = _bridgeGo.AddComponent<HAppsJSBridge>();

            _bridge.OnInitialized += HandleInitialized;
            _bridge.OnProfile += HandleProfile;
            _bridge.OnPaymentCompleted += HandlePaymentCompleted;
            _bridge.OnAuthTicket += HandleAuthTicket;
            _bridge.OnRegisterComplete += HandleRegisterComplete;
            _bridge.OnVisibility += HandleVisibility;
        }

        #region Public API

        public override async Task<bool> Initialize()
        {
            return await StartOperation<bool>(
                OperationType.Init,
                () => _bridge.SendToJS("init", "{}"),
                DEFAULT_TIMEOUT_MS);
        }

        public override async Task<UserData> RequestProfile()
        {
            return await StartOperation<UserData>(
                OperationType.Profile,
                () => _bridge.SendToJS("getProfile", "{}"),
                DEFAULT_TIMEOUT_MS);
        }

        public override async Task<PaymentData> RequestPayment(PaymentItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var json = JsonUtility.ToJson(new PaymentRequest { item = item });

            return await StartOperation<PaymentData>(
                OperationType.Payment,
                () => _bridge.SendToJS("makePayment", json),
                DEFAULT_TIMEOUT_MS);
        }

        public override async Task<string> OpenAuthPopup(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("[HApps] Auth popup URL must use https://", nameof(url));

            if (_currentOperation == OperationType.AuthTicket)
            {
                _currentTcs?.TrySetResult(null);
                Reset();
            }

            var json = JsonUtility.ToJson(new AuthPopupRequest { url = url });

            return await StartOperation<string>(
                OperationType.AuthTicket,
                () => _bridge.SendToJS("openAuthPopup", json),
                timeoutMs: null);
        }

        public override void Destroy()
        {
            if (_bridge != null)
            {
                _bridge.OnInitialized -= HandleInitialized;
                _bridge.OnProfile -= HandleProfile;
                _bridge.OnPaymentCompleted -= HandlePaymentCompleted;
                _bridge.OnAuthTicket -= HandleAuthTicket;
                _bridge.OnRegisterComplete -= HandleRegisterComplete;
                _bridge.OnVisibility -= HandleVisibility;
            }

            _currentTcs?.TrySetCanceled();
            Reset();

            if (_bridgeGo != null)
                UnityEngine.Object.Destroy(_bridgeGo);
        }

        #endregion

        #region Core

        private async Task<T> StartOperation<T>(
            OperationType type,
            Action startAction,
            int? timeoutMs)
        {
            if (_currentOperation != OperationType.None)
                throw new InvalidOperationException(
                    $"Operation already in progress: {_currentOperation}");

            Debug.Log($"[HApps] Starting {type}");

            _currentOperation = type;
            _currentTcs = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            startAction?.Invoke();

            Task<object> task = _currentTcs.Task;

            if (timeoutMs.HasValue)
                task = WithTimeout(task, timeoutMs.Value);

            var result = await task;
            return (T)result;
        }

        private async Task<object> WithTimeout(Task<object> task, int timeoutMs)
        {
            var timeoutTask = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(task, timeoutTask);

            if (completed == timeoutTask)
            {
                _currentTcs?.TrySetCanceled();
                Reset();
                throw new TimeoutException($"[HApps] Operation timed out after {timeoutMs}ms");
            }

            return await task;
        }

        private void Complete(object result)
        {
            _currentTcs?.TrySetResult(result);
            Reset();
        }

        private void Reset()
        {
            _currentOperation = OperationType.None;
            _currentTcs = null;
        }

        #endregion

        #region JS Callbacks

        private void HandleInitialized(InitData init, UserData user, SignatureData signature)
        {
            if (_currentOperation != OperationType.Init)
                return;

            if (user != null)
            {
                _userData = user;
                _loggedIn = true;
            }

            Signature = signature?.signature;
            _isInitialized = init?.ready == true || user != null;

            Complete(_isInitialized);
        }

        private void HandleProfile(UserData user)
        {
            if (_currentOperation != OperationType.Profile)
                return;

            _userData = user;
            _loggedIn = user != null;

            Complete(user);
        }

        private void HandlePaymentCompleted(PaymentData data)
        {
            if (_currentOperation != OperationType.Payment)
                return;

            Complete(data);
        }

        private void HandleAuthTicket(string ticket)
        {
            if (_currentOperation != OperationType.AuthTicket)
                return;

            Complete(ticket);
        }

        private void HandleRegisterComplete(UserData user, SignatureData signature)
        {
            if (user != null)
            {
                _userData = user;
                _loggedIn = true;
            }

            Signature = signature?.signature;
            NotifyUserChanged(user);
        }

        private void HandleVisibility(bool visible)
        {
            NotifyVisibilityChanged(visible);
        }

        #endregion
    }
}
