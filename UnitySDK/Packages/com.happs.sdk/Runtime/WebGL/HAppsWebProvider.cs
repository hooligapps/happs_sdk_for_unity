using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsWebProvider : HAppsProvider
    {
        public const string Version = "1.0.0";

        private enum OperationType
        {
            Init,
            GetProfile,
            MakePayment,
            OpenAuthPopup,
            OpenPortalAuth,
        }

        private const int DEFAULT_TIMEOUT_MS = 30000;

        private readonly HAppsJSBridge _bridge;

        private readonly Dictionary<OperationType, OperationBase> _operations
            = new();

        public HAppsWebProvider()
        {
            var go = new GameObject("HAppsJSBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);

            _bridge = go.AddComponent<HAppsJSBridge>();

            _bridge.OnInitialized += HandleInitialized;
            _bridge.OnProfile += HandleProfile;
            _bridge.OnPaymentCompleted += HandlePaymentCompleted;
            _bridge.OnAuthPopupCompleted += HandleAuthPopupCompleted;
            _bridge.OnPortalAuthCompleted += HandlePortalAuthCompleted;

            HAppsLog.Log("Provider created");
        }

        public override Task<bool> Initialize()
        {
            return StartOperation<bool>(
                OperationType.Init,
                () => _bridge.SendMessage("init", "{}"),
                false,
                DEFAULT_TIMEOUT_MS);
        }

        public override Task<UserData> GetProfile()
        {
            return StartOperation<UserData>(
                OperationType.GetProfile,
                () => _bridge.SendMessage("get_profile", "{}"),
                true,
                DEFAULT_TIMEOUT_MS);
        }

        public override Task<PaymentData> MakePayment(string orderId)
        {
            var json = JsonUtility.ToJson(new PaymentRequest { orderId = orderId });

            return StartOperation<PaymentData>(
                OperationType.MakePayment,
                () => _bridge.SendMessage("open_payment", json),
                false,
                null);
        }

        public override Task<AuthPopupData> OpenIdpAuthPopup(string url)
        {
            var json = JsonUtility.ToJson(new OpenAuthPopupRequest { url = url });

            return StartOperation<AuthPopupData>(
                OperationType.OpenAuthPopup,
                () => _bridge.SendMessage("popup_auth", json),
                true,
                null);
        }

        public override Task<bool> OpenPortalAuthPopup()
        {
            return StartOperation<bool>(
                OperationType.OpenPortalAuth,
                () => _bridge.SendMessage("portal_auth", "{}"),
                true,
                null);
        }

        public override void Dispose()
        {
            HAppsLog.Log("Provider dispose");

            if (_bridge != null)
            {
                _bridge.OnInitialized -= HandleInitialized;
                _bridge.OnProfile -= HandleProfile;
                _bridge.OnPaymentCompleted -= HandlePaymentCompleted;
                _bridge.OnAuthPopupCompleted -= HandleAuthPopupCompleted;
                _bridge.OnPortalAuthCompleted -= HandlePortalAuthCompleted;
            }

            var ex = new ObjectDisposedException("HAppsSDK");

            foreach (var op in _operations.Values)
                op.Fail(ex);

            _operations.Clear();
        }

        public override bool IsPortalSite()
        {
            return HAppsJSBridge.IsPortalSite();
        }

        private Task<T> StartOperation<T>(OperationType type, Action startAction, bool allowRestart, int? timeoutMs)
        {
            if (_operations.TryGetValue(type, out var existing))
            {
                if (!allowRestart)
                    throw new InvalidOperationException($"{type} already running");

                existing.Fail(new Exception("Operation restarted"));
                _operations.Remove(type);
            }

            var op = new Operation<T>(timeoutMs);

            _operations[type] = op;
            op.UntypedTask.ContinueWith(_ => CleanupFailedOperation(type, op),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

            HAppsLog.Log($"Starting {type}");

            try
            {
                startAction?.Invoke();
            }
            catch (Exception ex)
            {
                CleanupFailedOperation(type, op);
                op.Fail(ex);
            }

            return op.Task;
        }

        private void CleanupFailedOperation(OperationType type, OperationBase operation)
        {
            if (_operations.TryGetValue(type, out var current) && ReferenceEquals(current, operation))
                _operations.Remove(type);
        }

        private void Complete<T>(OperationType type, T result)
        {
            if (!_operations.Remove(type, out var opBase))
            {
                HAppsLog.Warn($"No pending operation for {type}");
                return;
            }

            if (opBase is Operation<T> op)
            {
                HAppsLog.Log($"Completed {type}: {result}");
                op.Complete(result);
            }
        }

        private void HandleInitialized(InitData init, UserData user, SignatureData signature)
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

            Complete(OperationType.GetProfile, user);
        }

        private void HandlePaymentCompleted(PaymentData data)
        {
            Complete(OperationType.MakePayment, data);
        }

        private void HandleAuthPopupCompleted(AuthPopupData authPopupData)
        {
            Complete(OperationType.OpenAuthPopup, authPopupData);
        }

        private void HandlePortalAuthCompleted(UserData user, SignatureData signature)
        {
            if (user != null)
            {
                _userData = user;
                _loggedIn = true;
            }

            var sig = signature?.signature ?? "";

            if (!string.IsNullOrEmpty(sig))
                Signature = sig;

            Complete(OperationType.OpenPortalAuth, !string.IsNullOrEmpty(sig));
        }
    }
}
