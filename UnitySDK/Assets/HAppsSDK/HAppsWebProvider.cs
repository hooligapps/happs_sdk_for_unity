using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsWebProvider : HAppsProvider
    {
        private enum OperationType
        {
            Init,
            Profile,
            Payment,
            AuthTicket
        }

        private const int DEFAULT_TIMEOUT_MS = 30000;

        private readonly HAppsJSBridge _bridge;

        private readonly Dictionary<OperationType, TaskCompletionSource<object>> _operations
            = new();

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

        public override Task<bool> Initialize()
        {
            return StartOperation<bool>(
                OperationType.Init,
                () => _bridge.SendMessage("init", "{}"),
                allowRestart: false,
                timeoutMs: DEFAULT_TIMEOUT_MS);
        }

        public override Task<UserData> RequestProfile()
        {
            return StartOperation<UserData>(
                OperationType.Profile,
                () => _bridge.SendMessage("getProfile", "{}"),
                allowRestart: true,
                timeoutMs: DEFAULT_TIMEOUT_MS);
        }

        public override Task<PaymentData> RequestPayment(PaymentItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var json = JsonUtility.ToJson(new PaymentRequest { item = item });

            return StartOperation<PaymentData>(
                OperationType.Payment,
                () => _bridge.SendMessage("makePayment", json),
                allowRestart: false,
                timeoutMs: DEFAULT_TIMEOUT_MS);
        }

        public override Task<string> OpenAuthPopup(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Action start = () => _bridge.OpenAuthPopup(url);
#else
            Action start = () => Application.OpenURL(url);
#endif

            return StartOperation<string>(
                OperationType.AuthTicket,
                start,
                allowRestart: true,
                timeoutMs: null); // auth без таймаута
        }

        #endregion

        #region Core Operation Logic

        private Task<T> StartOperation<T>(
            OperationType type,
            Action startAction,
            bool allowRestart,
            int? timeoutMs)
        {
            if (_operations.TryGetValue(type, out var existing))
            {
                if (!allowRestart)
                    throw new InvalidOperationException($"{type} already in progress");

                existing.TrySetResult(default);
                _operations.Remove(type);
            }

            Debug.Log($"[HApps] Starting {type}");

            var tcs = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _operations[type] = tcs;

            startAction?.Invoke();

            return AwaitResult<T>(type, tcs, timeoutMs);
        }

        private async Task<T> AwaitResult<T>(
            OperationType type,
            TaskCompletionSource<object> tcs,
            int? timeoutMs)
        {
            Task<object> task = tcs.Task;

            if (timeoutMs.HasValue)
            {
                var timeoutTask = Task.Delay(timeoutMs.Value);
                var completed = await Task.WhenAny(task, timeoutTask);

                if (completed == timeoutTask)
                {
                    _operations.Remove(type);
                    throw new TimeoutException($"HApps {type} timeout");
                }
            }

            var result = await task;
            return (T)result;
        }

        private void Complete(OperationType type, object result)
        {
            if (!_operations.TryGetValue(type, out var tcs))
                return;

            Debug.Log($"[HApps] Completed {type}: {result}");

            tcs.TrySetResult(result);
            _operations.Remove(type);
        }

        #endregion

        #region JS Callbacks

        private void HandleInitialized(
            InitData init,
            UserData user,
            SignatureData signature)
        {
            if (user != null)
            {
                _userData = user;
                _loggedIn = true;
            }

            Signature = signature?.signature;
            _isInitialized = init?.ready == true || user != null;

            Complete(OperationType.Init, _isInitialized);
        }

        private void HandleProfile(UserData user)
        {
            _userData = user;
            _loggedIn = user != null;

            Complete(OperationType.Profile, user);
        }

        private void HandlePaymentCompleted(PaymentData data)
        {
            Complete(OperationType.Payment, data);
        }

        private void HandleAuthTicket(string ticket)
        {
            Complete(OperationType.AuthTicket, ticket);
        }

        #endregion
    }
}