using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsWebProvider : HAppsProvider
    {
        public const string Version = "3.5.0";

        public event Action<WebAuthResult> AuthCompleted;
        public event Action<bool> AgeVerificationCompleted;
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

        private readonly HAppsJSBridge _bridge;
        private bool _disposed;
        private string _paymentOrderId;

        private readonly Dictionary<OperationType, OperationBase> _operations
            = new();

        public HAppsWebProvider()
        {
            var go = new GameObject("HAppsJSBridge");
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(go);

            _bridge = go.AddComponent<HAppsJSBridge>();

            _bridge.Tick += TickOperations;
            _bridge.OnConnected += HandleConnected;
            _bridge.OnProfile += HandleProfile;
            _bridge.OnPaymentCreated += HandlePaymentCreated;
            _bridge.OnPaymentCompleted += HandlePaymentCompleted;
            _bridge.OnAuthPopupCompleted += HandleAuthPopupCompleted;
            _bridge.OnPortalAuthCompleted += HandlePortalAuthCompleted;
            _bridge.OnAgeVerificationCompleted += HandleAgeVerificationCompleted;
            _bridge.OnUserChanged += HandleUserChanged;
            _bridge.OnError += HandleError;

            HAppsLog.Log("Provider created");
        }

        public override Task<bool> Connect() => Connect(CancellationToken.None);

        public Task<bool> Connect(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StartOperation<bool>(
                OperationType.Connect,
                () => _bridge.SendMessage("connect", "{}"),
                DEFAULT_TIMEOUT_MS, cancellationToken);
        }

        public override Task<UserData> GetProfile() => GetProfile(CancellationToken.None);

        public Task<UserData> GetProfile(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StartOperation<UserData>(
                OperationType.GetProfile,
                () => _bridge.SendMessage("get_profile", "{}"),
                DEFAULT_TIMEOUT_MS, cancellationToken);
        }

        public override Task<PaymentData> MakePayment(string orderId) => MakePayment(orderId, CancellationToken.None);

        public Task<PaymentData> MakePayment(string orderId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("Order ID is required.", nameof(orderId));
            var json = JsonUtility.ToJson(new PaymentRequest { orderId = orderId });

            return StartOperation<PaymentData>(
                OperationType.MakePayment,
                () => { _paymentOrderId = orderId; _bridge.SendMessage("open_payment", json); },
                INTERACTIVE_TIMEOUT_MS, cancellationToken);
        }

        public override Task<AuthPopupData> OpenIdpAuthPopup(string url) =>
            OpenIdpAuthPopup(url, null, CancellationToken.None);

        public Task<AuthPopupData> OpenIdpAuthPopup(string url, string callbackOrigin) =>
            OpenIdpAuthPopup(url, callbackOrigin, CancellationToken.None);

        public Task<AuthPopupData> OpenIdpAuthPopup(string url, CancellationToken cancellationToken) =>
            OpenIdpAuthPopup(url, null, cancellationToken);

        public Task<AuthPopupData> OpenIdpAuthPopup(
            string url,
            string callbackOrigin,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = string.IsNullOrWhiteSpace(callbackOrigin)
                ? JsonUtility.ToJson(new OpenAuthPopupRequest { url = url })
                : JsonUtility.ToJson(new OpenAuthPopupWithOriginRequest
                {
                    url = url,
                    callbackOrigin = callbackOrigin.Trim()
                });

            return StartOperation<AuthPopupData>(
                OperationType.OpenAuthPopup,
                () => _bridge.SendMessage("popup_auth", json),
                INTERACTIVE_TIMEOUT_MS, cancellationToken);
        }

        public override Task<WebAuthResult> OpenPortalAuthPopup() => OpenPortalAuthPopup(CancellationToken.None);

        public Task<WebAuthResult> OpenPortalAuthPopup(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
                throw new ObjectDisposedException(nameof(HAppsWebProvider));

            if (_userData?.verified == true)
                return Task.FromResult(new WebAuthResult(
                    true,
                    _userData,
                    string.IsNullOrEmpty(Signature) ? null : new SignatureData { signature = Signature },
                    AuthAction.Unknown));

            return StartOperation<WebAuthResult>(
                OperationType.OpenPortalAuth,
                () => _bridge.SendMessage("portal_auth", "{}"),
                INTERACTIVE_TIMEOUT_MS, cancellationToken);
        }

        public override void OpenAgeVerification()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HAppsWebProvider));
            _bridge.SendMessage("open_age_verification", "{}");
        }

        public override void SetTheaterMode(bool enabled)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HAppsWebProvider));
            var json = JsonUtility.ToJson(new SetTheaterModeRequest
            {
                enabled = enabled
            });

            _bridge.SendMessage("set_theater_mode", json);
        }

        public override void SetFullscreen(bool enabled)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HAppsWebProvider));
            var json = JsonUtility.ToJson(new SetFullscreenRequest
            {
                enabled = enabled
            });

            _bridge.SendMessage("set_fullscreen", json);
        }

        public void OpenExternalUrl(string url)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HAppsWebProvider));

            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));

            var json = JsonUtility.ToJson(new OpenExternalUrlRequest
            {
                url = url.Trim()
            });

            _bridge.SendMessage("open_external_url", json);
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            HAppsLog.Log("Provider dispose");

            if (_bridge != null)
            {
                _bridge.Tick -= TickOperations;
                _bridge.OnConnected -= HandleConnected;
                _bridge.OnProfile -= HandleProfile;
                _bridge.OnPaymentCreated -= HandlePaymentCreated;
                _bridge.OnPaymentCompleted -= HandlePaymentCompleted;
                _bridge.OnAuthPopupCompleted -= HandleAuthPopupCompleted;
                _bridge.OnPortalAuthCompleted -= HandlePortalAuthCompleted;
                _bridge.OnAgeVerificationCompleted -= HandleAgeVerificationCompleted;
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

        private void RaiseAuthCompleted(WebAuthResult result)
        {
            HAppsEvents.Invoke(AuthCompleted, result);
        }

        private Task<T> StartOperation<T>(OperationType type, Action startAction, int? timeoutMs, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HAppsWebProvider));

            TickOperations(Time.realtimeSinceStartupAsDouble);
            if (_operations.ContainsKey(type))
                throw new InvalidOperationException($"{type} already running");

            var op = new Operation<T>(timeoutMs, Time.realtimeSinceStartupAsDouble, cancellationToken);

            _operations[type] = op;

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

        private void TickOperations(double now)
        {
            if (_operations.Count == 0) return;
            foreach (var entry in new List<KeyValuePair<OperationType, OperationBase>>(_operations))
            {
                entry.Value.Tick(now);
                if (entry.Value.UntypedTask.IsCompleted)
                    CleanupFailedOperation(entry.Key, entry.Value);
            }
        }

        private void CleanupFailedOperation(OperationType type, OperationBase operation)
        {
            if (_operations.TryGetValue(type, out var current) && ReferenceEquals(current, operation))
                _operations.Remove(type);
        }

        private void Complete<T>(OperationType type, T result)
        {
            TickOperations(Time.realtimeSinceStartupAsDouble);
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
            if (error != null && (!string.IsNullOrEmpty(error.code) || !string.IsNullOrEmpty(error.message)))
            {
                Fail(OperationType.GetProfile, new HAppsException(error));
                return;
            }

            _userData = user;
            _loggedIn = user != null;

            Complete(OperationType.GetProfile, user);
        }

        private bool MatchesPayment(PaymentData data)
        {
            if (data == null || string.IsNullOrEmpty(data.orderId) ||
                !string.Equals(data.orderId, _paymentOrderId, StringComparison.Ordinal))
            {
                HAppsLog.Warn("Ignoring payment response without a matching order ID.");
                return false;
            }
            return true;
        }

        private void HandlePaymentCreated(PaymentData data)
        {
            if (!MatchesPayment(data)) return;

            if (data.Status != PaymentStatus.Started)
                Complete(OperationType.MakePayment, data);
        }

        private void HandlePaymentCompleted(PaymentData data)
        {
            if (!MatchesPayment(data)) return;
            HAppsJSBridge.TryFocusWindow();
            Complete(OperationType.MakePayment, data);
        }

        private void HandleAuthPopupCompleted(AuthPopupData authPopupData)
        {
            Complete(OperationType.OpenAuthPopup, authPopupData);
        }

        private void HandlePortalAuthCompleted(UserData user, SignatureData signature, AuthAction action)
        {
            if (user != null)
            {
                _userData = user;
                _loggedIn = true;
            }

            var sig = signature?.signature ?? "";

            if (!string.IsNullOrEmpty(sig))
                Signature = sig;

            var result = new WebAuthResult(
                !string.IsNullOrEmpty(sig),
                user,
                signature,
                action);

            Complete(OperationType.OpenPortalAuth, result);
            RaiseAuthCompleted(result);
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
            HAppsEvents.Invoke(UserChanged, user);
        }

        private void HandleAgeVerificationCompleted(bool confirmed)
        {
            HAppsEvents.Invoke(AgeVerificationCompleted, confirmed);
        }

        private void HandleError(HAppsErrorData error)
        {
            if (error == null)
            {
                HAppsLog.Warn("JS error message does not contain error data");
                return;
            }

            HAppsLog.Error($"JS SDK error: {error}");
            HAppsEvents.Invoke(Error, error);
        }

        [Serializable]
        private sealed class OpenAuthPopupWithOriginRequest
        {
            public string url;
            public string callbackOrigin;
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
