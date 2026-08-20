using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HAppsSDK
{
	public sealed class HAppsMobileProvider : HAppsProvider
	{
		private const int SessionExpirySkewSeconds = 5;

		private HAppsMobileAuthOptions _options;
		private IMobileTokenStore _tokenStore = new InMemoryMobileTokenStore();
		private HAppsMobileDeepLinkListener _deepLinkListener;
		private OidcDiscoveryDocument _discovery;
		private string _pendingLoginState;
		private MobileSession _currentSession;
		private readonly SemaphoreSlim _sessionGate = new SemaphoreSlim(1, 1);
		private readonly object _lifecycleSync = new object();
		private readonly object _sessionTaskSync = new object();
		private Task<MobileSession> _sessionRefreshTask;
		private int _sessionRefreshVersion;
		private TaskCompletionSource<MobileLoginResult> _activeLoginTcs;
		private bool _loginInProgress;
		private bool _disposed;
		private int _stateVersion;

		public MobileSession CurrentSession => _currentSession;

		public void Configure(HAppsMobileAuthOptions options, IMobileTokenStore tokenStore = null)
		{
			ThrowIfDisposed();
			_options = options ?? throw new ArgumentNullException(nameof(options));
			_tokenStore = tokenStore ?? CreateDefaultTokenStore(_options.PlayerPrefsStorageKey);
			HAppsLog.Log("Mobile configured");
		}

		private static IMobileTokenStore CreateDefaultTokenStore(string storageKey)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			return new AndroidKeystoreMobileTokenStore(storageKey);
#else
			return new InMemoryMobileTokenStore();
#endif
		}

		public Task<MobileSession> InitializeAsync()
			=> InitSessionAsync();

		public Task<MobileSession> InitSessionAsync()
		{
			EnsureConfigured();
			var stateVersion = CaptureStateVersion();
			return RefreshSessionSingleFlightAsync(stateVersion);
		}

		public Task<MobileSession> RefreshSessionAsync()
		{
			EnsureConfigured();
			var stateVersion = CaptureStateVersion();
			return RefreshSessionSingleFlightAsync(stateVersion);
		}

		public async Task<MobileLoginResult> LoginAsync()
		{
			EnsureConfigured();
			EnsureOidcConfigured();
			var stateVersion = BeginLogin();
			Action<string> onDeepLink = null;
			TaskCompletionSource<MobileLoginResult> loginTcs = null;
			try
			{
				EnsureDeepLinkListener();
				var deviceState = await RunSessionExclusiveAsync(
					stateVersion,
					() => EnsureDeviceRegisteredAsync(stateVersion));
				await GetDiscoveryAsync(stateVersion);
				ThrowIfStateInvalid(stateVersion);

				var codeVerifier = CreateCodeVerifier();
				var codeChallenge = CreateCodeChallenge(codeVerifier);
				var startResponse = await RunSessionExclusiveAsync(
					stateVersion,
					() => StartOidcAsync(deviceState.DeviceId, codeChallenge, stateVersion));
				ThrowIfStateInvalid(stateVersion);

				loginTcs = new TaskCompletionSource<MobileLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
				lock (_lifecycleSync)
				{
					ThrowIfStateInvalid(stateVersion);
					_activeLoginTcs = loginTcs;
					_pendingLoginState = startResponse.state;
				}

				onDeepLink = url => HandleLoginDeepLink(
					url,
					startResponse.state,
					codeVerifier,
					stateVersion,
					loginTcs);
				_deepLinkListener.DeepLinkReceived += onDeepLink;

				using var timeoutCts = new CancellationTokenSource(_options.LoginTimeoutMs);
				using var timeoutReg = timeoutCts.Token.Register(() =>
				{
					loginTcs.TrySetException(new TimeoutException("Mobile login timed out while waiting for the redirect callback."));
				});

				HAppsLog.Log("Opening mobile auth URL");
				Application.OpenURL(startResponse.authorizationUrl);
				var result = await loginTcs.Task;
				ThrowIfStateInvalid(stateVersion);
				HAppsLog.Log($"Mobile login completed: success={result.IsSuccess}");
				return result;
			}
			finally
			{
				if (_deepLinkListener != null && onDeepLink != null)
					_deepLinkListener.DeepLinkReceived -= onDeepLink;

				EndLogin(loginTcs);
			}
		}

		public async Task LogoutAsync()
		{
			EnsureConfigured();
			await _sessionGate.WaitAsync();

			try
			{
				var stateVersion = BeginStateReset("Mobile login was cancelled by logout.");
				ThrowIfStateInvalid(stateVersion);
				var tokenSet = await _tokenStore.LoadAsync();
				if (tokenSet == null || string.IsNullOrWhiteSpace(tokenSet.DeviceId))
				{
					await ClearLocalStateAsync();
					return;
				}

				if (string.IsNullOrWhiteSpace(tokenSet.IdToken) || string.IsNullOrWhiteSpace(_options.OidcLogoutUrl))
				{
					await ClearLocalStateAsync();
					return;
				}

				await ExecuteRemoteLogoutAndClearLocalStateAsync(async () =>
				{
					var proof = CreateProofAsync(
						"oidc-logout",
						tokenSet,
						HashHex(tokenSet.IdToken),
						HashHex(_options.PostLogoutRedirectUri));

					var request = new OidcLogoutRequest
					{
						clientId = _options.ClientId,
						deviceId = tokenSet.DeviceId,
						idToken = tokenSet.IdToken,
						postLogoutRedirectUri = _options.PostLogoutRedirectUri,
						timestamp = proof.Timestamp,
						nonce = proof.Nonce,
						signature = proof.Signature
					};

					var response = await SendJsonPostAsync<OidcLogoutRequest, OidcLogoutResponse>(
						_options.OidcLogoutUrl,
						request,
						_options.HttpTimeoutSeconds);
					if (response == null || string.IsNullOrWhiteSpace(response.logoutUrl))
						throw new InvalidOperationException("OIDC logout response is invalid.");

					ThrowIfStateInvalid(stateVersion);
					HAppsLog.Log("Opening mobile logout URL");
					Application.OpenURL(response.logoutUrl);
				});
			}
			finally
			{
				_sessionGate.Release();
			}
		}

		private async Task ExecuteRemoteLogoutAndClearLocalStateAsync(Func<Task> remoteLogout)
		{
			try
			{
				await remoteLogout();
			}
			finally
			{
				await ClearLocalStateAsync();
			}
		}

		public override Task<UserData> GetProfile()
		{
			throw new NotSupportedException("GetProfile is not implemented for mobile auth yet.");
		}

		public override Task<PaymentData> MakePayment(string orderId)
		{
			throw new NotSupportedException("Mobile payment flow is not implemented via MakePayment. Use CreatePaymentAsync.");
		}

		public async Task<MobileCreatePaymentResult> CreatePaymentAsync(MobileCreatePaymentRequest request)
		{
			EnsureConfigured();
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			var stateVersion = CaptureStateVersion();
			var accessToken = await RunSessionExclusiveAsync(
				stateVersion,
				async () => (await EnsureActiveSessionInternalAsync(stateVersion)).AccessToken);

			try
			{
				return await CreatePaymentInternalAsync(request, accessToken, stateVersion);
			}
			catch (MobileApiException ex) when (IsRecoverableSessionError(ex))
			{
				HAppsLog.Warn($"Create payment requires session recovery. statusCode={ex.StatusCode}");
				accessToken = (await RefreshSessionSingleFlightAsync(stateVersion)).AccessToken;
				return await CreatePaymentInternalAsync(request, accessToken, stateVersion);
			}
		}

		public override void Dispose()
		{
			TaskCompletionSource<MobileLoginResult> loginTcs;
			lock (_lifecycleSync)
			{
				if (_disposed)
					return;

				_disposed = true;
				Interlocked.Increment(ref _stateVersion);
				loginTcs = _activeLoginTcs;
				_activeLoginTcs = null;
				_pendingLoginState = null;
			}

			loginTcs?.TrySetException(new ObjectDisposedException(nameof(HAppsMobileProvider)));

			if (_deepLinkListener != null)
				_deepLinkListener.DeepLinkReceived -= HandleStrayDeepLink;

			if (_deepLinkListener != null)
			{
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(_deepLinkListener.gameObject);
				else
					UnityEngine.Object.DestroyImmediate(_deepLinkListener.gameObject);
			}

			_deepLinkListener = null;
			_discovery = null;
			_currentSession = null;
			_userData = null;
			_loggedIn = false;
		}

		private void EnsureConfigured()
		{
			ThrowIfDisposed();

			if (_options == null)
				throw new InvalidOperationException("Call HApps.ConfigureMobile(...) before using HApps.Mobile.");

			if (string.IsNullOrWhiteSpace(_options.ClientId))
				throw new InvalidOperationException("Mobile auth ClientId is not configured.");

			if (string.IsNullOrWhiteSpace(_options.DeviceRegisterUrl))
				throw new InvalidOperationException("Mobile device register endpoint is not configured.");

			if (string.IsNullOrWhiteSpace(_options.InitSessionUrl))
				throw new InvalidOperationException("Mobile init session endpoint is not configured.");

			if (_options.HttpTimeoutSeconds <= 0)
				throw new InvalidOperationException("Mobile HTTP timeout must be greater than zero.");

			if (_options.LoginTimeoutMs <= 0)
				throw new InvalidOperationException("Mobile login timeout must be greater than zero.");
		}

		private int CaptureStateVersion()
		{
			ThrowIfDisposed();
			return Volatile.Read(ref _stateVersion);
		}

		private int BeginLogin()
		{
			lock (_lifecycleSync)
			{
				ThrowIfDisposed();
				if (_loginInProgress)
					throw new InvalidOperationException("Mobile login is already in progress.");

				_loginInProgress = true;
				return _stateVersion;
			}
		}

		private void EndLogin(TaskCompletionSource<MobileLoginResult> loginTcs)
		{
			lock (_lifecycleSync)
			{
				if (ReferenceEquals(_activeLoginTcs, loginTcs))
					_activeLoginTcs = null;

				_pendingLoginState = null;
				_loginInProgress = false;
			}
		}

		private int BeginStateReset(string loginCancellationMessage)
		{
			TaskCompletionSource<MobileLoginResult> loginTcs;
			int stateVersion;
			lock (_lifecycleSync)
			{
				ThrowIfDisposed();
				stateVersion = Interlocked.Increment(ref _stateVersion);
				loginTcs = _activeLoginTcs;
				_activeLoginTcs = null;
				_pendingLoginState = null;
			}

			loginTcs?.TrySetException(new OperationCanceledException(loginCancellationMessage));
			return stateVersion;
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(HAppsMobileProvider));
		}

		private void ThrowIfStateInvalid(int stateVersion)
		{
			ThrowIfDisposed();
			if (stateVersion != Volatile.Read(ref _stateVersion))
				throw new OperationCanceledException("Mobile state changed while the operation was running.");
		}

		private async Task<T> RunSessionExclusiveAsync<T>(int stateVersion, Func<Task<T>> action)
		{
			await _sessionGate.WaitAsync();
			try
			{
				ThrowIfStateInvalid(stateVersion);
				var result = await action();
				ThrowIfStateInvalid(stateVersion);
				return result;
			}
			finally
			{
				_sessionGate.Release();
			}
		}

		private Task<MobileSession> RefreshSessionSingleFlightAsync(int stateVersion)
		{
			lock (_sessionTaskSync)
			{
				if (_sessionRefreshTask != null && _sessionRefreshVersion == stateVersion)
					return _sessionRefreshTask;

				_sessionRefreshVersion = stateVersion;
				var refreshTask = RunSessionExclusiveAsync(
					stateVersion,
					() => InitSessionInternalAsync(forceRefresh: true, stateVersion));
				_sessionRefreshTask = refreshTask;
				ObserveSessionRefreshCompletion(refreshTask);
				return refreshTask;
			}
		}

		private void ObserveSessionRefreshCompletion(Task<MobileSession> task)
		{
			task.ContinueWith(completed =>
			{
				_ = completed.Exception;
				lock (_sessionTaskSync)
				{
					if (ReferenceEquals(_sessionRefreshTask, task))
						_sessionRefreshTask = null;
				}
			}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		}

		private async Task RunSessionExclusiveAsync(int stateVersion, Func<Task> action)
		{
			await _sessionGate.WaitAsync();
			try
			{
				ThrowIfStateInvalid(stateVersion);
				await action();
				ThrowIfStateInvalid(stateVersion);
			}
			finally
			{
				_sessionGate.Release();
			}
		}

		private void EnsureOidcConfigured()
		{
			if (string.IsNullOrWhiteSpace(_options.Authority))
				throw new InvalidOperationException("Mobile auth Authority is not configured.");
			if (string.IsNullOrWhiteSpace(_options.RedirectUri))
				throw new InvalidOperationException("Mobile auth RedirectUri is not configured.");
			if (string.IsNullOrWhiteSpace(_options.OidcStartUrl))
				throw new InvalidOperationException("Mobile OIDC start endpoint is not configured.");
			if (string.IsNullOrWhiteSpace(_options.OidcExchangeUrl))
				throw new InvalidOperationException("Mobile OIDC exchange endpoint is not configured.");
			if (string.IsNullOrWhiteSpace(_options.PostLogoutRedirectUri))
				throw new InvalidOperationException("Mobile PostLogoutRedirectUri is not configured.");
		}

		private async Task<MobileSession> EnsureActiveSessionInternalAsync(int stateVersion)
		{
			ThrowIfStateInvalid(stateVersion);
			if (_currentSession != null && !IsExpired(_currentSession.AccessTokenExpiresAtUtc) && !string.IsNullOrWhiteSpace(_currentSession.AccessToken))
				return _currentSession;

			var tokenSet = await _tokenStore.LoadAsync();
			ThrowIfStateInvalid(stateVersion);
			if (tokenSet != null && !IsExpired(tokenSet.AccessTokenExpiresAtUtc) && !string.IsNullOrWhiteSpace(tokenSet.AccessToken))
				return ApplyCachedSession(tokenSet);

			return await InitSessionInternalAsync(forceRefresh: true, stateVersion);
		}

		private async Task<MobileCreatePaymentResult> CreatePaymentInternalAsync(
			MobileCreatePaymentRequest request,
			string accessToken,
			int stateVersion)
		{
			if (string.IsNullOrWhiteSpace(_options.CreatePaymentUrl))
				throw new InvalidOperationException("Portal create payment endpoint is not configured.");

			var payload = new CreatePaymentPayload
			{
				productId = request.ProductId,
				price = request.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
				currency = request.Currency,
				desc = request.Description,
				requestId = request.RequestId
			};

			var json = JsonUtility.ToJson(payload);
			HAppsLog.Log("Creating mobile payment");
			var responseText = await SendAuthorizedJsonPostAsync(
				_options.CreatePaymentUrl,
				accessToken,
				json,
				_options.HttpTimeoutSeconds);
			ThrowIfStateInvalid(stateVersion);
			var response = JsonUtility.FromJson<CreatePaymentResponse>(responseText);
			if (response == null || string.IsNullOrWhiteSpace(response.orderId) || string.IsNullOrWhiteSpace(response.paymentUrl))
				throw new InvalidOperationException("Create payment response is invalid.");

			HAppsLog.Log("Mobile payment created");
			Application.OpenURL(response.paymentUrl);

			return new MobileCreatePaymentResult
			{
				OrderId = response.orderId,
				PaymentUrl = response.paymentUrl
			};
		}

		private static bool IsRecoverableSessionError(MobileApiException ex)
		{
			if (ex.StatusCode != 401)
				return false;

			return ContainsCode(ex.ResponseBody, "invalid_mobile_session")
				|| ContainsCode(ex.ResponseBody, "mobile_session_expired");
		}

		private static bool ContainsCode(string body, string code)
		{
			return !string.IsNullOrWhiteSpace(body)
				&& body.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void EnsureDeepLinkListener()
		{
			ThrowIfDisposed();
			if (_deepLinkListener != null)
				return;

			var go = new GameObject("HAppsMobileDeepLinkListener");
			if (Application.isPlaying)
				UnityEngine.Object.DontDestroyOnLoad(go);
			_deepLinkListener = go.AddComponent<HAppsMobileDeepLinkListener>();
			_deepLinkListener.DeepLinkReceived += HandleStrayDeepLink;
			HAppsLog.Log("Mobile deep link listener created");
		}

		private void HandleStrayDeepLink(string url)
		{
			if (_disposed)
				return;

			if (string.IsNullOrEmpty(_pendingLoginState))
				return;

			HAppsLog.Log("Pending mobile auth state is active; deep link received");
		}

		private async Task<OidcDiscoveryDocument> GetDiscoveryAsync(int stateVersion)
		{
			ThrowIfStateInvalid(stateVersion);
			if (_discovery != null)
				return _discovery;

			var authority = _options.Authority.TrimEnd('/');
			var url = $"{authority}/.well-known/openid-configuration";
			HAppsLog.Log("Loading OIDC discovery");
			var json = await SendGetAsync(url, _options.HttpTimeoutSeconds);
			ThrowIfStateInvalid(stateVersion);
			var discovery = JsonUtility.FromJson<OidcDiscoveryDocument>(json);

			if (discovery == null || string.IsNullOrEmpty(discovery.authorization_endpoint) || string.IsNullOrEmpty(discovery.token_endpoint))
				throw new InvalidOperationException("OIDC discovery document is invalid.");

			_discovery = discovery;
			return discovery;
		}

		private async Task<MobileSession> InitSessionInternalAsync(bool forceRefresh, int stateVersion)
		{
			ThrowIfStateInvalid(stateVersion);
			var tokenSet = await EnsureDeviceRegisteredAsync(stateVersion);

			if (!forceRefresh && !IsExpired(tokenSet.AccessTokenExpiresAtUtc) && !string.IsNullOrWhiteSpace(tokenSet.AccessToken))
				return ApplyCachedSession(tokenSet);

			var proof = CreateProofAsync("session-init", tokenSet);
			var payload = new InitSessionRequest
			{
				clientId = _options.ClientId,
				deviceId = tokenSet.DeviceId,
				timestamp = proof.Timestamp,
				nonce = proof.Nonce,
				signature = proof.Signature
			};

			HAppsLog.Log("Requesting mobile session");
			var response = await SendJsonPostAsync<InitSessionRequest, InitSessionResponse>(
				_options.InitSessionUrl,
				payload,
				_options.HttpTimeoutSeconds);
			ThrowIfStateInvalid(stateVersion);
			if (response == null || string.IsNullOrWhiteSpace(response.accessToken) || string.IsNullOrWhiteSpace(response.publicId))
				throw new InvalidOperationException("Portal initSession response is invalid.");

			return await ApplySessionResponseAsync(tokenSet, response, stateVersion);
		}

		private async Task<MobileTokenSet> EnsureDeviceRegisteredAsync(int stateVersion)
		{
			ThrowIfStateInvalid(stateVersion);
			var tokenSet = await _tokenStore.LoadAsync() ?? new MobileTokenSet();
			ThrowIfStateInvalid(stateVersion);
			EnsureDeviceKeyMaterial(tokenSet);

			if (!string.IsNullOrWhiteSpace(tokenSet.DeviceId))
				return tokenSet;

			var publicKeyBytes = DecodeBase64Url(tokenSet.DevicePublicKey);
			var publicKeyHash = HashHex(publicKeyBytes);
			var timestamp = GetUnixTimeSeconds();
			var nonce = Guid.NewGuid().ToString();
			var message = BuildProofMessage("device-register", _options.ClientId, publicKeyHash, timestamp.ToString(), nonce);
			var signature = SignWithDeviceKey(tokenSet, message);

			var request = new RegisterDeviceRequest
			{
				clientId = _options.ClientId,
				publicKey = tokenSet.DevicePublicKey,
				timestamp = timestamp,
				nonce = nonce,
				signature = signature
			};

			HAppsLog.Log("Registering mobile device");
			var response = await SendJsonPostAsync<RegisterDeviceRequest, RegisterDeviceResponse>(
				_options.DeviceRegisterUrl,
				request,
				_options.HttpTimeoutSeconds);
			ThrowIfStateInvalid(stateVersion);
			if (response == null || string.IsNullOrWhiteSpace(response.deviceId))
				throw new InvalidOperationException("Device register response is invalid.");

			tokenSet.DeviceId = response.deviceId;
			await _tokenStore.SaveAsync(tokenSet);
			ThrowIfStateInvalid(stateVersion);
			HAppsLog.Log("Mobile device registered");
			return tokenSet;
		}

		private async Task<OidcStartResponse> StartOidcAsync(string deviceId, string codeChallenge, int stateVersion)
		{
			ThrowIfStateInvalid(stateVersion);
			var tokenSet = await _tokenStore.LoadAsync();
			ThrowIfStateInvalid(stateVersion);
			var proof = CreateProofAsync(
				"oidc-start",
				tokenSet,
				HashHex(_options.RedirectUri),
				codeChallenge);

			var request = new OidcStartRequest
			{
				clientId = _options.ClientId,
				deviceId = deviceId,
				redirectUri = _options.RedirectUri,
				codeChallenge = codeChallenge,
				timestamp = proof.Timestamp,
				nonce = proof.Nonce,
				signature = proof.Signature
			};

			HAppsLog.Log("Starting mobile OIDC");
			var response = await SendJsonPostAsync<OidcStartRequest, OidcStartResponse>(
				_options.OidcStartUrl,
				request,
				_options.HttpTimeoutSeconds);
			ThrowIfStateInvalid(stateVersion);
			if (response == null || string.IsNullOrWhiteSpace(response.authorizationUrl) || string.IsNullOrWhiteSpace(response.state))
				throw new InvalidOperationException("OIDC start response is invalid.");

			return response;
		}

		private async void HandleLoginDeepLink(
			string url,
			string expectedState,
			string codeVerifier,
			int stateVersion,
			TaskCompletionSource<MobileLoginResult> loginTcs)
		{
			if (loginTcs.Task.IsCompleted)
				return;

			try
			{
				ThrowIfStateInvalid(stateVersion);
				HAppsLog.Log("Handling mobile deep link");

				if (!MatchesRedirectUri(url, _options.RedirectUri))
				{
					HAppsLog.Warn("Ignoring deep link that does not match redirectUri");
					return;
				}

				var query = ParseQuery(url);
				if (query.TryGetValue("error", out var error))
				{
					HAppsLog.Warn("Mobile auth deep link returned an error");
					loginTcs.TrySetResult(new MobileLoginResult { Error = error });
					return;
				}

				if (!query.TryGetValue("state", out var returnedState) || returnedState != expectedState)
				{
					HAppsLog.Error("OIDC state mismatch");
					loginTcs.TrySetException(new InvalidOperationException("OIDC state mismatch."));
					return;
				}

				if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
				{
					HAppsLog.Error("OIDC redirect does not contain authorization code.");
					loginTcs.TrySetException(new InvalidOperationException("OIDC redirect does not contain authorization code."));
					return;
				}

				var discovery = await GetDiscoveryAsync(stateVersion);
				var oidcTokens = await ExchangeCodeAsync(
					discovery.token_endpoint,
					_options.ClientId,
					code,
					_options.RedirectUri,
					codeVerifier,
					_options.HttpTimeoutSeconds);
				ThrowIfStateInvalid(stateVersion);

				if (string.IsNullOrWhiteSpace(oidcTokens.id_token))
					throw new InvalidOperationException("Token exchange response does not contain id_token.");

				var socialId = ExtractSubjectFromJwt(oidcTokens.id_token);
				var result = await RunSessionExclusiveAsync(stateVersion, async () =>
				{
					await ExchangeOidcAsync(expectedState, oidcTokens.id_token, stateVersion);
					var session = await InitSessionInternalAsync(forceRefresh: true, stateVersion);
					var tokenSet = await _tokenStore.LoadAsync() ?? new MobileTokenSet();
					ThrowIfStateInvalid(stateVersion);
					tokenSet.IdToken = oidcTokens.id_token;
					tokenSet.OidcAccessToken = oidcTokens.access_token;
					tokenSet.SocialId = socialId;
					await _tokenStore.SaveAsync(tokenSet);
					ThrowIfStateInvalid(stateVersion);
					_currentSession.IsAuthorized = true;
					_loggedIn = true;
					_userData = new UserData
					{
						userId = session.PublicId,
						userName = socialId,
						verified = session.Verified
					};

					return new MobileLoginResult
					{
						AccessToken = session.AccessToken,
						IdToken = oidcTokens.id_token,
						OidcAccessToken = oidcTokens.access_token,
						TokenType = oidcTokens.token_type,
						ExpiresIn = session.AccessTokenExpiresAtUtc > 0
							? (int)Math.Max(0, session.AccessTokenExpiresAtUtc - GetUnixTimeSeconds())
							: oidcTokens.expires_in,
						Scope = oidcTokens.scope,
						Code = code,
						CodeVerifier = codeVerifier,
						RedirectUri = _options.RedirectUri,
						PublicId = session.PublicId,
						SocialId = socialId,
						Verified = session.Verified,
						DeviceId = session.DeviceId
					};
				});

				loginTcs.TrySetResult(result);
			}
			catch (Exception ex)
			{
				loginTcs.TrySetException(ex);
			}
		}

		private async Task ExchangeOidcAsync(string state, string idToken, int stateVersion)
		{
			ThrowIfStateInvalid(stateVersion);
			var tokenSet = await _tokenStore.LoadAsync();
			ThrowIfStateInvalid(stateVersion);
			var proof = CreateProofAsync(
				"oidc-exchange",
				tokenSet,
				HashHex(idToken),
				state);

			var request = new OidcExchangeRequest
			{
				clientId = _options.ClientId,
				deviceId = tokenSet.DeviceId,
				idToken = idToken,
				state = state,
				timestamp = proof.Timestamp,
				nonce = proof.Nonce,
				signature = proof.Signature
			};

			HAppsLog.Log("Exchanging mobile OIDC session");
			var response = await SendJsonPostAsync<OidcExchangeRequest, OidcExchangeResponse>(
				_options.OidcExchangeUrl,
				request,
				_options.HttpTimeoutSeconds);
			ThrowIfStateInvalid(stateVersion);
			if (response == null || !response.ok)
				throw new InvalidOperationException("OIDC exchange response is invalid.");
		}

		private async Task<MobileSession> ApplySessionResponseAsync(
			MobileTokenSet tokenSet,
			InitSessionResponse response,
			int stateVersion)
		{
			ThrowIfStateInvalid(stateVersion);
			tokenSet.AccessToken = response.accessToken;
			tokenSet.AccessTokenExpiresAtUtc = GetUnixTimeSeconds() + Math.Max(0, response.expiresIn - SessionExpirySkewSeconds);
			tokenSet.PublicId = response.publicId;
			tokenSet.Verified = response.verified;
			await _tokenStore.SaveAsync(tokenSet);
			ThrowIfStateInvalid(stateVersion);
			return ApplyCachedSession(tokenSet);
		}

		private MobileSession ApplyCachedSession(MobileTokenSet tokenSet)
		{
			_currentSession = new MobileSession
			{
				DeviceId = tokenSet.DeviceId,
				AccessToken = tokenSet.AccessToken,
				AccessTokenExpiresAtUtc = tokenSet.AccessTokenExpiresAtUtc,
				PublicId = tokenSet.PublicId,
				Verified = tokenSet.Verified,
				IsAuthorized = !string.IsNullOrWhiteSpace(tokenSet.IdToken)
			};

			_loggedIn = _currentSession.IsAuthorized;
			_userData = new UserData
			{
				userId = tokenSet.PublicId,
				userName = tokenSet.SocialId,
				verified = tokenSet.Verified
			};

			HAppsLog.Log($"Mobile session applied: verified={tokenSet.Verified}, isAuthorized={_currentSession.IsAuthorized}");
			return _currentSession;
		}

		private async Task ClearLocalStateAsync()
		{
			var tokenSet = await _tokenStore.LoadAsync();
			TryDeleteDeviceKey(tokenSet);
			_userData = null;
			_loggedIn = false;
			_currentSession = null;
			await _tokenStore.ClearAsync();
		}

		private MobileProof CreateProofAsync(string action, MobileTokenSet tokenSet, params string[] extraFields)
		{
			if (tokenSet == null || string.IsNullOrWhiteSpace(tokenSet.DeviceId))
				throw new InvalidOperationException("Mobile device credentials are not initialized.");

			var timestamp = GetUnixTimeSeconds();
			var nonce = Guid.NewGuid().ToString();
			var fields = new List<string> { _options.ClientId, tokenSet.DeviceId };
			if (extraFields != null && extraFields.Length > 0)
				fields.AddRange(extraFields);
			fields.Add(timestamp.ToString());
			fields.Add(nonce);
			var message = BuildProofMessage(action, fields.ToArray());

			return new MobileProof
			{
				Timestamp = timestamp,
				Nonce = nonce,
				Signature = SignWithDeviceKey(tokenSet, message)
			};
		}

		private static async Task<string> SendGetAsync(string url, int timeoutSeconds)
		{
			using var request = UnityWebRequest.Get(url);
			request.timeout = timeoutSeconds;
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"GET request failed: statusCode={request.responseCode}, error={request.error}");
				throw new InvalidOperationException($"GET request failed: {request.error}");
			}

			return request.downloadHandler.text;
		}

		private static async Task<TResponse> SendJsonPostAsync<TRequest, TResponse>(string url, TRequest payload, int timeoutSeconds)
			where TResponse : class
		{
			var json = JsonUtility.ToJson(payload);
			HAppsLog.Log("Sending JSON POST");
			using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
			request.timeout = timeoutSeconds;
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			var responseText = request.downloadHandler.text;
			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"POST request failed: statusCode={request.responseCode}, error={request.error}");
				throw new MobileApiException((long)request.responseCode, responseText, $"POST request failed: {request.error}");
			}

			return JsonUtility.FromJson<TResponse>(responseText);
		}

		private static async Task<string> SendAuthorizedJsonPostAsync(string url, string bearerToken, string json, int timeoutSeconds)
		{
			using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
			request.timeout = timeoutSeconds;
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json ?? "{}"));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Accept", "application/json");
			request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"Authorized POST request failed: statusCode={request.responseCode}, error={request.error}");
				throw new MobileApiException((long)request.responseCode, request.downloadHandler.text, $"POST request failed: {request.error}");
			}

			return request.downloadHandler.text;
		}

		private static async Task<OidcTokenResponse> ExchangeCodeAsync(
			string tokenEndpoint,
			string clientId,
			string code,
			string redirectUri,
			string codeVerifier,
			int timeoutSeconds)
		{
			var form = new Dictionary<string, string>
			{
				["grant_type"] = "authorization_code",
				["client_id"] = clientId,
				["code"] = code,
				["redirect_uri"] = redirectUri,
				["code_verifier"] = codeVerifier
			};

			var payload = ToQueryString(form);
			HAppsLog.Log("Exchanging authorization code");
			using var request = new UnityWebRequest(tokenEndpoint, UnityWebRequest.kHttpVerbPOST);
			request.timeout = timeoutSeconds;
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			var responseText = request.downloadHandler.text;
			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"Token exchange failed: statusCode={request.responseCode}, error={request.error}");
				throw new InvalidOperationException($"Token exchange failed: {request.error}");
			}

			var response = JsonUtility.FromJson<OidcTokenResponse>(responseText);
			if (response == null || string.IsNullOrWhiteSpace(response.access_token))
				throw new InvalidOperationException("Token exchange response does not contain access_token.");

			return response;
		}

		private static Task AwaitAsyncOperation(AsyncOperation operation)
		{
			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			operation.completed += _ => tcs.TrySetResult(true);
			return tcs.Task;
		}

		private static Dictionary<string, string> ParseQuery(string url)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			var questionMarkIndex = url.IndexOf('?');
			var hashIndex = url.IndexOf('#');

			string query;
			if (questionMarkIndex >= 0)
			{
				var queryEndIndex = hashIndex > questionMarkIndex ? hashIndex : url.Length;
				if (questionMarkIndex == queryEndIndex - 1)
					return result;

				query = url.Substring(questionMarkIndex + 1, queryEndIndex - questionMarkIndex - 1);
			}
			else if (hashIndex >= 0 && hashIndex < url.Length - 1)
			{
				query = url.Substring(hashIndex + 1);
			}
			else
			{
				return result;
			}

			var fragments = query.Split('&');
			foreach (var fragment in fragments)
			{
				if (string.IsNullOrWhiteSpace(fragment))
					continue;

				var equalsIndex = fragment.IndexOf('=');
				if (equalsIndex < 0)
				{
					result[Uri.UnescapeDataString(fragment)] = string.Empty;
					continue;
				}

				var key = Uri.UnescapeDataString(fragment.Substring(0, equalsIndex));
				var value = Uri.UnescapeDataString(fragment.Substring(equalsIndex + 1));
				result[key] = value;
			}

			return result;
		}

		private static bool MatchesRedirectUri(string actualValue, string expectedValue)
		{
			if (!Uri.TryCreate(actualValue, UriKind.Absolute, out var actual) ||
				!Uri.TryCreate(expectedValue, UriKind.Absolute, out var expected))
			{
				return false;
			}

			return string.Equals(actual.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(actual.UserInfo, expected.UserInfo, StringComparison.Ordinal) &&
				actual.Port == expected.Port &&
				string.Equals(actual.AbsolutePath, expected.AbsolutePath, StringComparison.Ordinal);
		}

		private static string ToQueryString(Dictionary<string, string> values)
		{
			var builder = new StringBuilder();
			var isFirst = true;

			foreach (var pair in values)
			{
				if (!isFirst)
					builder.Append('&');

				isFirst = false;
				builder.Append(Uri.EscapeDataString(pair.Key));
				builder.Append('=');
				builder.Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
			}

			return builder.ToString();
		}

		private static string CreateCodeVerifier()
		{
			return CreateRandomUrlSafeString(64);
		}

		private static string CreateCodeChallenge(string codeVerifier)
		{
			using var sha256 = SHA256.Create();
			var bytes = Encoding.ASCII.GetBytes(codeVerifier);
			var hash = sha256.ComputeHash(bytes);
			return ToBase64Url(hash);
		}

		private static string CreateRandomUrlSafeString(int byteLength)
		{
			var bytes = new byte[byteLength];
			using var rng = RandomNumberGenerator.Create();
			rng.GetBytes(bytes);
			return ToBase64Url(bytes);
		}

		private static long GetUnixTimeSeconds()
		{
			return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		}

		private static bool IsExpired(long expiresAtUtc)
		{
			return expiresAtUtc <= 0 || expiresAtUtc <= GetUnixTimeSeconds();
		}

		private void EnsureDeviceKeyMaterial(MobileTokenSet tokenSet)
		{
			if (tokenSet == null)
				throw new ArgumentNullException(nameof(tokenSet));

#if UNITY_ANDROID && !UNITY_EDITOR
			if (string.IsNullOrWhiteSpace(tokenSet.DeviceKeyAlias))
				tokenSet.DeviceKeyAlias = BuildAndroidKeyAlias();

			if (!AndroidKeyExists(tokenSet.DeviceKeyAlias))
			{
				GenerateAndroidKeyPair(tokenSet.DeviceKeyAlias);
				HAppsLog.Log("Generated Android device key");
			}

			tokenSet.DevicePrivateKey = null;
			tokenSet.DevicePublicKey = GetAndroidPublicKey(tokenSet.DeviceKeyAlias);
			if (string.IsNullOrWhiteSpace(tokenSet.DevicePublicKey))
				throw new InvalidOperationException("Android keystore public key is unavailable.");
#else
			throw new NotSupportedException(
				"Mobile device signing is only implemented for Android runtime right now. " +
				"Run this flow on an Android build/device.");
#endif
		}

		private string BuildAndroidKeyAlias()
		{
			var suffix = HashHex(_options.ClientId ?? "mobile").Substring(0, 12);
			return $"happs_mobile_key_{suffix}";
		}

		private string SignWithDeviceKey(MobileTokenSet tokenSet, string message)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (string.IsNullOrWhiteSpace(tokenSet?.DeviceKeyAlias))
				throw new InvalidOperationException("Android device key alias is not initialized.");

			return SignWithAndroidKey(tokenSet.DeviceKeyAlias, message);
#else
			throw new NotSupportedException(
				"Mobile device signing is only implemented for Android runtime right now. " +
				"Run this flow on an Android build/device.");
#endif
		}

		private static (string PrivateKey, string PublicKey) GenerateSoftwareDeviceKeyPair()
		{
			ECDsa ecdsa = null;
			Exception lastError = null;

			try
			{
				ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			}
			catch (Exception ex)
			{
				lastError = ex;
			}

			if (ecdsa == null)
			{
				try
				{
					ecdsa = ECDsa.Create();
					if (ecdsa != null)
						ecdsa.KeySize = 256;
				}
				catch (Exception ex)
				{
					lastError = ex;
					ecdsa?.Dispose();
					ecdsa = null;
				}
			}

			if (ecdsa == null)
			{
				throw new NotSupportedException(
					"Software ECDSA key generation is not supported on this Unity runtime. " +
					"Use an Android native keystore implementation or a runtime that supports ECDsa.",
					lastError);
			}

			using (ecdsa)
			{
				return (
					ToBase64Url(ecdsa.ExportPkcs8PrivateKey()),
					ToBase64Url(ecdsa.ExportSubjectPublicKeyInfo()));
			}
		}

		private static string SignMessage(string privateKeyBase64Url, string message)
		{
			using var ecdsa = ECDsa.Create();
			var privateKeyBytes = DecodeBase64Url(privateKeyBase64Url);
			ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
			var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256);
			return ToBase64Url(signature);
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private static bool AndroidKeyExists(string alias)
		{
			using var keyStoreClass = new AndroidJavaClass("java.security.KeyStore");
			using var keyStore = keyStoreClass.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
			keyStore.Call("load", new object[] { null, null });
			return keyStore.Call<bool>("containsAlias", alias);
		}

		private static void GenerateAndroidKeyPair(string alias)
		{
			using var keyPairGeneratorClass = new AndroidJavaClass("java.security.KeyPairGenerator");
			using var keyPairGenerator = keyPairGeneratorClass.CallStatic<AndroidJavaObject>("getInstance", "EC", "AndroidKeyStore");
			using var keyProperties = new AndroidJavaClass("android.security.keystore.KeyProperties");
			var purposeSign = keyProperties.GetStatic<int>("PURPOSE_SIGN");
			var purposeVerify = keyProperties.GetStatic<int>("PURPOSE_VERIFY");
			using var builder = new AndroidJavaObject(
				"android.security.keystore.KeyGenParameterSpec$Builder",
				alias,
				purposeSign | purposeVerify);
			using var keyPropertiesStrings = new AndroidJavaClass("android.security.keystore.KeyProperties");
			var digestSha256 = keyPropertiesStrings.GetStatic<string>("DIGEST_SHA256");
			builder.Call<AndroidJavaObject>("setDigests", new object[] { new[] { digestSha256 } });
			using var ecSpec = new AndroidJavaObject("java.security.spec.ECGenParameterSpec", "secp256r1");
			builder.Call<AndroidJavaObject>("setAlgorithmParameterSpec", ecSpec);
			using var spec = builder.Call<AndroidJavaObject>("build");
			keyPairGenerator.Call("initialize", spec);
			keyPairGenerator.Call<AndroidJavaObject>("generateKeyPair");
		}

		private static string GetAndroidPublicKey(string alias)
		{
			using var keyStoreClass = new AndroidJavaClass("java.security.KeyStore");
			using var keyStore = keyStoreClass.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
			keyStore.Call("load", new object[] { null, null });
			using var certificate = keyStore.Call<AndroidJavaObject>("getCertificate", alias);
			if (certificate == null)
				return null;
			using var publicKey = certificate.Call<AndroidJavaObject>("getPublicKey");
			var encoded = publicKey.Call<byte[]>("getEncoded");
			return encoded == null ? null : ToBase64Url(encoded);
		}

		private static string SignWithAndroidKey(string alias, string message)
		{
			using var keyStoreClass = new AndroidJavaClass("java.security.KeyStore");
			using var keyStore = keyStoreClass.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
			keyStore.Call("load", new object[] { null, null });
			using var privateKey = keyStore.Call<AndroidJavaObject>("getKey", new object[] { alias, null });
			if (privateKey == null)
				throw new InvalidOperationException($"Android keystore private key not found for alias={alias}");

			using var signatureClass = new AndroidJavaClass("java.security.Signature");
			using var signature = signatureClass.CallStatic<AndroidJavaObject>("getInstance", "SHA256withECDSA");
			signature.Call("initSign", privateKey);
			signature.Call("update", Encoding.UTF8.GetBytes(message));
			var signed = signature.Call<byte[]>("sign");
			return ToBase64Url(signed);
		}

		private static void DeleteAndroidKey(string alias)
		{
			if (string.IsNullOrWhiteSpace(alias))
				return;

			using var keyStoreClass = new AndroidJavaClass("java.security.KeyStore");
			using var keyStore = keyStoreClass.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
			keyStore.Call("load", new object[] { null, null });
			if (keyStore.Call<bool>("containsAlias", alias))
				keyStore.Call("deleteEntry", alias);
		}
#endif

		private void TryDeleteDeviceKey(MobileTokenSet tokenSet)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (tokenSet == null || string.IsNullOrWhiteSpace(tokenSet.DeviceKeyAlias))
				return;

			try
			{
				DeleteAndroidKey(tokenSet.DeviceKeyAlias);
			}
			catch (Exception ex)
			{
				HAppsLog.Warn($"Failed to delete Android device key. errorType={ex.GetType().Name}");
			}
#endif
		}

		private static string BuildProofMessage(string action, params string[] fields)
		{
			var values = new List<string> { "v1", action };
			values.AddRange(fields);
			return string.Join("|", values.ToArray());
		}

		private static string HashHex(string value)
		{
			return HashHex(Encoding.UTF8.GetBytes(value ?? string.Empty));
		}

		private static string HashHex(byte[] value)
		{
			using var sha256 = SHA256.Create();
			var bytes = sha256.ComputeHash(value ?? Array.Empty<byte>());
			var builder = new StringBuilder(bytes.Length * 2);
			for (var i = 0; i < bytes.Length; i++)
				builder.Append(bytes[i].ToString("x2"));
			return builder.ToString();
		}

		private static string ExtractSubjectFromJwt(string jwt)
		{
			if (string.IsNullOrWhiteSpace(jwt))
				return null;

			var parts = jwt.Split('.');
			if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
				return null;

			try
			{
				var json = Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
				var payload = JsonUtility.FromJson<JwtPayload>(json);
				return string.IsNullOrWhiteSpace(payload?.sub) ? null : payload.sub;
			}
			catch
			{
				return null;
			}
		}

		private static string ToBase64Url(byte[] bytes)
		{
			return Convert.ToBase64String(bytes)
				.TrimEnd('=')
				.Replace('+', '-')
				.Replace('/', '_');
		}

		private static byte[] DecodeBase64Url(string value)
		{
			var normalized = (value ?? string.Empty).Replace('-', '+').Replace('_', '/');
			switch (normalized.Length % 4)
			{
				case 2:
					normalized += "==";
					break;
				case 3:
					normalized += "=";
					break;
			}

			return Convert.FromBase64String(normalized);
		}

		[Serializable]
		private sealed class OidcDiscoveryDocument
		{
			public string authorization_endpoint;
			public string token_endpoint;
			public string userinfo_endpoint;
			public string end_session_endpoint;
		}

		[Serializable]
		private sealed class OidcTokenResponse
		{
			public string access_token;
			public string id_token;
			public string token_type;
			public int expires_in;
			public string scope;
		}

		[Serializable]
		private sealed class RegisterDeviceRequest
		{
			public string clientId;
			public string publicKey;
			public long timestamp;
			public string nonce;
			public string signature;
		}

		[Serializable]
		private sealed class RegisterDeviceResponse
		{
			public string deviceId;
		}

		[Serializable]
		private sealed class InitSessionRequest
		{
			public string clientId;
			public string deviceId;
			public long timestamp;
			public string nonce;
			public string signature;
		}

		[Serializable]
		private sealed class InitSessionResponse
		{
			public string accessToken;
			public int expiresIn;
			public string publicId;
			public bool verified;
		}

		[Serializable]
		private sealed class OidcStartRequest
		{
			public string clientId;
			public string deviceId;
			public string redirectUri;
			public string codeChallenge;
			public long timestamp;
			public string nonce;
			public string signature;
		}

		[Serializable]
		private sealed class OidcStartResponse
		{
			public string authorizationUrl;
			public string state;
		}

		[Serializable]
		private sealed class OidcExchangeRequest
		{
			public string clientId;
			public string deviceId;
			public string idToken;
			public string state;
			public long timestamp;
			public string nonce;
			public string signature;
		}

		[Serializable]
		private sealed class OidcExchangeResponse
		{
			public bool ok;
		}

		[Serializable]
		private sealed class OidcLogoutRequest
		{
			public string clientId;
			public string deviceId;
			public string idToken;
			public string postLogoutRedirectUri;
			public long timestamp;
			public string nonce;
			public string signature;
		}

		[Serializable]
		private sealed class OidcLogoutResponse
		{
			public string logoutUrl;
			public string state;
		}

		[Serializable]
		private sealed class CreatePaymentPayload
		{
			public string productId;
			public string price;
			public string currency;
			public string desc;
			public string requestId;
		}

		[Serializable]
		private sealed class CreatePaymentResponse
		{
			public string orderId;
			public string paymentUrl;
		}

		[Serializable]
		private sealed class JwtPayload
		{
			public string sub;
		}

		private sealed class MobileProof
		{
			public long Timestamp;
			public string Nonce;
			public string Signature;
		}

		private sealed class MobileApiException : Exception
		{
			public long StatusCode { get; }
			public string ResponseBody { get; }

			public MobileApiException(long statusCode, string responseBody, string message) : base(message)
			{
				StatusCode = statusCode;
				ResponseBody = responseBody ?? string.Empty;
			}
		}
	}
}
