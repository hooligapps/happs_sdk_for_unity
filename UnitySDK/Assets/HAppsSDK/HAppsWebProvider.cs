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
            Payment
        }

        private const int DEFAULT_TIMEOUT_MS = 30000;

        private readonly HAppsJSBridge _bridge;

        private OperationType _currentOperation = OperationType.None;
        private TaskCompletionSource<object> _operationTcs;

        public HAppsWebProvider()
        {
            var go = new GameObject("HAppsJSBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);

            _bridge = go.AddComponent<HAppsJSBridge>();

            _bridge.OnInitialized += HandleInitialized;
            _bridge.OnProfile += HandleProfile;
            _bridge.OnPaymentCreated += HandlePaymentCreated;
            _bridge.OnPaymentCompleted += HandlePaymentCompleted;
        }

        #region Public API

        public override async Task<bool> Initialize()
        {
            var task = StartOperation<bool>(OperationType.Init);

            _bridge.SendMessage("init", "{}");

#if UNITY_EDITOR
            HandleInitialized(new InitData { ready = true }, null, null);
#endif

            return await WithTimeout(task, DEFAULT_TIMEOUT_MS);
        }

        public override async Task<UserData> RequestProfile()
        {
            var task = StartOperation<UserData>(OperationType.Profile);

            _bridge.SendMessage("getProfile", "{}");

#if UNITY_EDITOR
            HandleProfile(new UserData
            {
                userId = "editor",
                userName = "EditorUser"
            });
#endif

            return await WithTimeout(task, DEFAULT_TIMEOUT_MS);
        }

        public override async Task<PaymentData> RequestPayment(PaymentItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var task = StartOperation<PaymentData>(OperationType.Payment);

            var json = JsonUtility.ToJson(new PaymentRequest { item = item });

            Debug.Log($"[HApps] Request payment: {item.itemId} x{item.quantity}");

            _bridge.SendMessage("makePayment", json);

            return await WithTimeout(task, DEFAULT_TIMEOUT_MS);
        }

        public override void OpenAuthPopup()
        {
            var apiEndpoint = "";
            var url = $"{apiEndpoint}/auth/idp?token=1";

#if UNITY_WEBGL && !UNITY_EDITOR
            _bridge.OpenAuthPopup(url);
#else
            Application.OpenURL(url);
#endif
        }

        #endregion

        #region Operation Control

        private Task<T> StartOperation<T>(OperationType type)
        {
            if (_currentOperation != OperationType.None)
            {
                throw new InvalidOperationException(
                    $"HApps operation already in progress: {_currentOperation}");
            }

            _currentOperation = type;

            _operationTcs = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            return _operationTcs.Task.ContinueWith(t => (T)t.Result);
        }

        private void CompleteOperation(object result)
        {
            _operationTcs?.TrySetResult(result);
            ResetOperation();
        }

        private void FailOperation(Exception ex)
        {
            _operationTcs?.TrySetException(ex);
            ResetOperation();
        }

        private void ResetOperation()
        {
            _operationTcs = null;
            _currentOperation = OperationType.None;
        }

        private async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs)
        {
            var timeoutTask = Task.Delay(timeoutMs);

            var completed = await Task.WhenAny(task, timeoutTask);

            if (completed == timeoutTask)
            {
                ResetOperation();
                throw new TimeoutException("HApps operation timeout");
            }

            return await task;
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

            CompleteOperation(_isInitialized);
        }

        private void HandleProfile(UserData user)
        {
            if (_currentOperation != OperationType.Profile)
                return;

            if (user != null)
            {
                _userData = user;
                _loggedIn = true;
            }

            CompleteOperation(user);
        }

        private void HandlePaymentCreated(PaymentData data)
        {
            Debug.Log($"[HApps] Payment started: {data?.paymentId}");
        }

        private void HandlePaymentCompleted(PaymentData data)
        {
            if (_currentOperation != OperationType.Payment)
                return;

            CompleteOperation(data);
        }

        #endregion
    }
}