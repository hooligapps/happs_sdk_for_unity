using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsWebProvider : HAppsProvider
    {
        public const string Version = "3.1.2-preview.1";

        public event Action<UserData, SignatureData> AuthCompleted;
        public event Action<UserData> UserChanged;
        public event Action<HAppsErrorData> Error;
        public string Signature { get; private set; }
        public bool IsInitialized { get; private set; }

        private enum OperationType
        {
            Connect,
            GetProfile,
            MakePayment,
            OpenAuthPopup,
            OpenPortalAuth,
        }

        private const int DEFAULT_TIMEOUT_MS = 30000;
        private const int INTERACTIVE_TIMEOUT_MS = 180000;
        private const int PAYMENT_STATUS_MAX_REQUESTS = 10;
        private const float PAYMENT_STATUS_POLL_INTERVAL_SECONDS = 1f;

        private readonly HAppsJSBridge _bridge;
        private bool _disposed;
        private string _activePaymentOrderId;
        private int _paymentStatusRequestsSent;
        private int _paymentPollingGeneration;

        private readonly Dictionary<OperationType, OperationBase> _operations
            = new();

        public HAppsWebProvider()
        {
            var go = new GameObject("HAppsJSBridge");
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(go);

            _bridge = go.AddComponent<HAppsJSBridge>();

            _bridge.OnConnected += HandleConnected;
            _bridge.OnProfile += HandleProfile;
            _bridge.OnPaymentCreated += HandlePaymentCreated;
            _bridge.OnPaymentCompleted += HandlePaymentCompleted;
            _bridge.OnPaymentStatus += HandlePaymentStatus;
            _bridge.OnAuthPopupCompleted += HandleAuthPopupCompleted;
            _bridge.OnPortalAuthCompleted += HandlePortalAuthCompleted;
            _bridge.OnUserChanged += HandleUserChanged;
            _bridge.OnError += HandleError;

            HAppsLog.Log("Provider created");
        }

        public override Task<bool> Connect()
        {
            return StartOperation<bool>(
                OperationType.Connect,
                () => _bridge.SendMessage("connect", "{}"),
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
                () =>
                {
                    ResetPaymentPolling(orderId);
                    _bridge.SendMessage("open_payment", json);
                },
                false,
                INTERACTIVE_TIMEOUT_MS);
        }

        public override Task<AuthPopupData> OpenIdpAuthPopup(string url)
        {
            var json = JsonUtility.ToJson(new OpenAuthPopupRequest { url = url });

            return StartOperation<AuthPopupData>(
                OperationType.OpenAuthPopup,
                () => _bridge.SendMessage("popup_auth", json),
                true,
                INTERACTIVE_TIMEOUT_MS);
        }

        public override Task<bool> OpenPortalAuthPopup()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HAppsWebProvider));

            if (_userData?.verified == true)
                return Task.FromResult(true);

            return StartOperation<bool>(
                OperationType.OpenPortalAuth,
                () => _bridge.SendMessage("portal_auth", "{}"),
                true,
                INTERACTIVE_TIMEOUT_MS);
        }

        public override void OpenAgeVerification(bool adultMode = true)
        {
            var json = JsonUtility.ToJson(new OpenAgeVerificationRequest
            {
                adultMode = adultMode
            });

            _bridge.SendMessage("open_age_verification", json);
        }

        public override void SetTheaterMode(bool enabled)
        {
            var json = JsonUtility.ToJson(new SetTheaterModeRequest
            {
                enabled = enabled
            });

            _bridge.SendMessage("set_theater_mode", json);
        }

        public override void SetFullscreen(bool enabled)
        {
            var json = JsonUtility.ToJson(new SetFullscreenRequest
            {
                enabled = enabled
            });

            _bridge.SendMessage("set_fullscreen", json);
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelPaymentPolling();
            HAppsLog.Log("Provider dispose");

            if (_bridge != null)
            {
                _bridge.OnConnected -= HandleConnected;
                _bridge.OnProfile -= HandleProfile;
                _bridge.OnPaymentCreated -= HandlePaymentCreated;
                _bridge.OnPaymentCompleted -= HandlePaymentCompleted;
                _bridge.OnPaymentStatus -= HandlePaymentStatus;
                _bridge.OnAuthPopupCompleted -= HandleAuthPopupCompleted;
                _bridge.OnPortalAuthCompleted -= HandlePortalAuthCompleted;
                _bridge.OnUserChanged -= HandleUserChanged;
                _bridge.OnError -= HandleError;
            }

            var ex = new ObjectDisposedException("HAppsSDK");

            foreach (var op in _operations.Values)
                op.Fail(ex);

            _operations.Clear();

            if (_bridge != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(_bridge.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(_bridge.gameObject);
            }
        }

        public override bool IsPortalSite()
        {
            return HAppsJSBridge.IsPortalSite();
        }

        public bool IsReady()
        {
            return HAppsJSBridge.IsReady();
        }

        private void RaiseAuthCompleted(UserData user, SignatureData signature)
        {
            AuthCompleted?.Invoke(user, signature);
        }

        private Task<T> StartOperation<T>(OperationType type, Action startAction, bool allowRestart, int? timeoutMs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HAppsWebProvider));

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
                HAppsLog.Log($"Completed {type}");
                op.Complete(result);
            }
        }

        private void Fail(OperationType type, Exception error)
        {
            if (!_operations.Remove(type, out var operation))
                return;

            operation.Fail(error);
        }

        private void HandleConnected(InitData init, UserData user, SignatureData signature)
        {
            if (user != null)
            {
                _userData = user;
                _loggedIn = true;
            }

            Signature = signature?.signature;
            IsInitialized = init?.ready == true || user != null;

            Complete(OperationType.Connect, IsInitialized);
        }

        private void HandleProfile(UserData user, HAppsErrorData error)
        {
            if (error != null)
            {
                Fail(OperationType.GetProfile, new HAppsException(error));
                return;
            }

            _userData = user;
            _loggedIn = user != null;

            Complete(OperationType.GetProfile, user);
        }

        private void HandlePaymentCreated(PaymentData data)
        {
            if (data == null)
            {
                FailPayment(new InvalidOperationException("Payment response is empty."));
                return;
            }

            if (!string.IsNullOrEmpty(data.orderId))
                _activePaymentOrderId = data.orderId;

            // Older browser bridges report payment/status responses as "payment".
            if (_paymentStatusRequestsSent > 0)
            {
                HandlePaymentStatusResponse(data);
                return;
            }

            if (data.Status == PaymentStatus.Started)
                return;

            if (data.Status == PaymentStatus.Pending)
            {
                BeginPaymentStatusPolling(data);
                return;
            }

            CompletePayment(data);
        }

        private void HandlePaymentCompleted(PaymentData data)
        {
            HAppsJSBridge.TryFocusWindow();

            if (data == null)
            {
                FailPayment(new InvalidOperationException("Payment completion response is empty."));
                return;
            }

            // Some bridge versions reuse "payment_complete" for status responses.
            if (_paymentStatusRequestsSent > 0)
            {
                HandlePaymentStatusResponse(data);
                return;
            }

            if (data.IsFailed)
            {
                CompletePayment(data);
                return;
            }

            // Checkout completion only means that the provider accepted the payment.
            // Ask the portal for the postback-validated status before reporting success.
            BeginPaymentStatusPolling(data);
        }

        private void HandlePaymentStatus(PaymentData data)
        {
            HandlePaymentStatusResponse(data);
        }

        private void BeginPaymentStatusPolling(PaymentData data)
        {
            if (!string.IsNullOrEmpty(data?.orderId))
                _activePaymentOrderId = data.orderId;

            if (string.IsNullOrEmpty(_activePaymentOrderId))
            {
                FailPayment(new InvalidOperationException("Payment status cannot be requested without an orderId."));
                return;
            }

            _paymentStatusRequestsSent = 0;
            _paymentPollingGeneration++;
            RequestPaymentStatus(data);
        }

        private void HandlePaymentStatusResponse(PaymentData data)
        {
            if (data == null)
            {
                FailPayment(new InvalidOperationException("Payment status response is empty."));
                return;
            }

            if (!string.IsNullOrEmpty(data.orderId))
                _activePaymentOrderId = data.orderId;

            if (data.Status != PaymentStatus.Pending && data.Status != PaymentStatus.Started)
            {
                CompletePayment(data);
                return;
            }

            if (_paymentStatusRequestsSent >= PAYMENT_STATUS_MAX_REQUESTS)
            {
                HAppsLog.Warn("Payment is still pending after status polling completed");
                CompletePayment(data);
                return;
            }

            var generation = _paymentPollingGeneration;
            _bridge.RunAfterDelay(PAYMENT_STATUS_POLL_INTERVAL_SECONDS, () =>
            {
                if (_disposed || generation != _paymentPollingGeneration)
                    return;

                RequestPaymentStatus(data);
            });
        }

        private void RequestPaymentStatus(PaymentData lastKnownStatus)
        {
            if (!_operations.ContainsKey(OperationType.MakePayment))
                return;

            if (_paymentStatusRequestsSent >= PAYMENT_STATUS_MAX_REQUESTS)
            {
                CompletePayment(lastKnownStatus);
                return;
            }

            _paymentStatusRequestsSent++;
            var json = JsonUtility.ToJson(new PaymentConfirmRequest
            {
                orderId = _activePaymentOrderId
            });
            _bridge.SendMessage("payment_status", json);
        }

        private void CompletePayment(PaymentData data)
        {
            CancelPaymentPolling();
            Complete(OperationType.MakePayment, data);
        }

        private void FailPayment(Exception error)
        {
            CancelPaymentPolling();
            Fail(OperationType.MakePayment, error);
        }

        private void ResetPaymentPolling(string orderId)
        {
            _paymentPollingGeneration++;
            _paymentStatusRequestsSent = 0;
            _activePaymentOrderId = orderId;
        }

        private void CancelPaymentPolling()
        {
            _paymentPollingGeneration++;
            _paymentStatusRequestsSent = 0;
            _activePaymentOrderId = null;
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

            RaiseAuthCompleted(user, signature);
            Complete(OperationType.OpenPortalAuth, !string.IsNullOrEmpty(sig));
        }

        private void HandleUserChanged(UserData user)
        {
            if (user == null)
            {
                HAppsLog.Warn("JS user_changed message does not contain userData");
                return;
            }

            _userData = user;
            _loggedIn = true;
            UserChanged?.Invoke(user);
        }

        private void HandleError(HAppsErrorData error)
        {
            if (error == null)
            {
                HAppsLog.Warn("JS error message does not contain error data");
                return;
            }

            HAppsLog.Error($"JS SDK error: {error}");
            Error?.Invoke(error);
        }

        [Serializable]
        private sealed class OpenAgeVerificationRequest
        {
            public bool adultMode = true;
        }

        [Serializable]
        private sealed class SetTheaterModeRequest
        {
            public bool enabled;
        }

        [Serializable]
        private sealed class SetFullscreenRequest
        {
            public bool enabled;
        }
    }
}
