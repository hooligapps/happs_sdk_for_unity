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

        private OperationType _currentOperation = OperationType.None;
        private TaskCompletionSource<object> _currentTcs;

        public HAppsWebProvider()
        {
            var go = new GameObject("HAppsJSBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);

            _bridge = go.AddComponent<HAppsJSBridge>();

            _bridge.OnInitialized += HandleInitialized;
            _bridge.OnProfile += HandleProfile;
            _bridge.OnPaymentCompleted += HandlePaymentCompleted;
            _bridge.OnAuthTicket += HandleAuthTicket;
        }

        #region Public API

        public override async Task<bool> Initialize()
        {
            return await StartOperation<bool>(
                OperationType.Init,
                () => _bridge.SendMessage("init", "{}"),
                DEFAULT_TIMEOUT_MS);
        }

        public override async Task<UserData> RequestProfile()
        {
            return await StartOperation<UserData>(
                OperationType.Profile,
                () => _bridge.SendMessage("getProfile", "{}"),
                DEFAULT_TIMEOUT_MS);
        }

        public override async Task<PaymentData> RequestPayment(PaymentItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var json = JsonUtility.ToJson(new PaymentRequest { item = item });

            return await StartOperation<PaymentData>(
                OperationType.Payment,
                () => _bridge.SendMessage("makePayment", json),
                DEFAULT_TIMEOUT_MS);
        }

        public override async Task<string> OpenAuthPopup(string url)
        {
            // перезапуск auth = отмена старого
            if (_currentOperation == OperationType.AuthTicket)
            {
                _currentTcs?.TrySetResult(null);
                Reset();
            }

            return await StartOperation<string>(
                OperationType.AuthTicket,
                () => _bridge.OpenAuthPopup(url),
                timeoutMs: null);
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
            {
                task = WithTimeout(task, timeoutMs.Value);
            }

            var result = await task;

            return (T)result;
        }

        private async Task<object> WithTimeout(Task<object> task, int timeoutMs)
        {
            var timeoutTask = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(task, timeoutTask);

            if (completed == timeoutTask)
            {
                Reset();
                throw new TimeoutException("HApps operation timeout");
            }

            return await task;
        }

        private void Complete(object result)
        {
            Debug.Log($"[HApps] Completed {_currentOperation}: {result}");

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

        private void HandleInitialized(
            InitData init,
            UserData user,
            SignatureData signature)
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

        #endregion
    }
}