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
		private HAppsMobileAuthOptions _options;
		private IMobileTokenStore _tokenStore = new InMemoryMobileTokenStore();
		private HAppsMobileDeepLinkListener _deepLinkListener;
		private OidcDiscoveryDocument _discovery;
		private string _pendingLoginState;
		private MobileSession _currentSession;

		public MobileSession CurrentSession => _currentSession;

		public void Configure(HAppsMobileAuthOptions options, IMobileTokenStore tokenStore = null)
		{
			_options = options ?? throw new ArgumentNullException(nameof(options));
			_tokenStore = tokenStore ?? new InMemoryMobileTokenStore();
			HAppsLog.Log($"Mobile configured: authority={_options.Authority}, clientId={_options.ClientId}, redirectUri={_options.RedirectUri}, logoutRedirectUri={_options.PostLogoutRedirectUri}, scope={_options.Scope}, initSessionUrl={_options.InitSessionUrl}, refreshSessionUrl={_options.RefreshSessionUrl}");
		}

		public Task<MobileSession> InitSessionAsync()
		{
			EnsureConfigured();
			return InitSessionInternalAsync();
		}

		public async Task<MobileLoginResult> LoginAsync()
		{
			EnsureConfigured();
			EnsureDeepLinkListener();

			var discovery = await GetDiscoveryAsync();
			var codeVerifier = CreateCodeVerifier();
			var codeChallenge = CreateCodeChallenge(codeVerifier);
			var state = CreateRandomUrlSafeString(32);
			var nonce = CreateRandomUrlSafeString(32);
			var loginTcs = new TaskCompletionSource<MobileLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pendingLoginState = state;

			void OnDeepLink(string url)
			{
				HandleLoginDeepLink(url, state, codeVerifier, loginTcs);
			}

			_deepLinkListener.DeepLinkReceived += OnDeepLink;

			using var timeoutCts = new CancellationTokenSource(_options.LoginTimeoutMs);
			using var timeoutReg = timeoutCts.Token.Register(() =>
			{
				loginTcs.TrySetException(new TimeoutException("Mobile login timed out while waiting for the redirect callback."));
			});

			try
			{
				var authorizeUrl = BuildAuthorizeUrl(discovery.authorization_endpoint, state, nonce, codeChallenge);
				HAppsLog.Log($"Opening mobile auth URL: {authorizeUrl}");
				HAppsLog.Log($"Mobile auth state={state}, nonce={nonce}, codeVerifierLength={codeVerifier.Length}");
				Application.OpenURL(authorizeUrl);

				var result = await loginTcs.Task;
				HAppsLog.Log($"Mobile login result: success={result.IsSuccess}, hasAccessToken={!string.IsNullOrEmpty(result.AccessToken)}, error={result.Error}");
				return result;
			}
			finally
			{
				_pendingLoginState = null;
				_deepLinkListener.DeepLinkReceived -= OnDeepLink;
			}
		}

		public async Task LogoutAsync()
		{
			EnsureConfigured();

			HAppsLog.Log("Mobile logout: clearing local state");
			_userData = null;
			_loggedIn = false;
			_currentSession = null;
			await _tokenStore.ClearAsync();

			HAppsLog.Log("Mobile logout: requesting fresh anonymous session");
			await InitSessionInternalAsync();
		}

		public async Task<MobileSession> RefreshSessionAsync()
		{
			EnsureConfigured();

			var tokenSet = await _tokenStore.LoadAsync();
			if (tokenSet == null || string.IsNullOrWhiteSpace(tokenSet.RefreshToken))
				throw new InvalidOperationException("RefreshSessionAsync requires mobile refresh token.");

			return await RefreshSessionInternalAsync(tokenSet.RefreshToken);
		}

		public override Task<UserData> GetProfile()
		{
			throw new NotSupportedException("GetProfile is not implemented for mobile auth yet. Resolve user profile via your backend after game-login.");
		}

		public override Task<PaymentData> MakePayment(string orderId)
		{
			throw new NotSupportedException("Mobile payment flow is not implemented yet.");
		}

		public async Task<MobileCreatePaymentResult> CreatePaymentAsync(MobileCreatePaymentRequest request)
		{
			EnsureConfigured();

			if (request == null)
				throw new ArgumentNullException(nameof(request));

			EnsureSessionToken();

			try
			{
				return await CreatePaymentInternalAsync(request);
			}
			catch (MobileApiException ex) when (IsInvalidMobileSession(ex))
			{
				HAppsLog.Warn($"Create payment requires session recovery. statusCode={ex.StatusCode}, response={ex.ResponseBody}");
				await RecoverSessionAsync();
				return await CreatePaymentInternalAsync(request);
			}
		}

		public override void Dispose()
		{
			if (_deepLinkListener != null)
				_deepLinkListener.DeepLinkReceived -= HandleStrayDeepLink;

			if (_deepLinkListener != null)
				UnityEngine.Object.Destroy(_deepLinkListener.gameObject);

			_deepLinkListener = null;
			_discovery = null;
		}

		private void EnsureConfigured()
		{
			if (_options == null)
				throw new InvalidOperationException("Call HApps.ConfigureMobile(...) before using HApps.Mobile.");

			if (string.IsNullOrWhiteSpace(_options.Authority))
				throw new InvalidOperationException("Mobile auth Authority is not configured.");

			if (string.IsNullOrWhiteSpace(_options.ClientId))
				throw new InvalidOperationException("Mobile auth ClientId is not configured.");

			if (string.IsNullOrWhiteSpace(_options.RedirectUri))
				throw new InvalidOperationException("Mobile auth RedirectUri is not configured.");
		}

		private void EnsureSessionToken()
		{
			if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.AccessToken))
				throw new InvalidOperationException("Portal access token is missing. Call InitSessionAsync/RefreshSessionAsync first.");
		}

		private async Task<MobileCreatePaymentResult> CreatePaymentInternalAsync(MobileCreatePaymentRequest request)
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
			HAppsLog.Log($"Creating mobile payment: url={_options.CreatePaymentUrl}");
			HAppsLog.Log($"Create payment request body: {json}");
			var responseText = await SendAuthorizedJsonPostAsync(_options.CreatePaymentUrl, _currentSession.AccessToken, json);
			HAppsLog.Log($"Create payment raw response: {responseText}");
			var response = JsonUtility.FromJson<CreatePaymentResponse>(responseText);
			if (response == null || string.IsNullOrWhiteSpace(response.orderId) || string.IsNullOrWhiteSpace(response.paymentUrl))
				throw new InvalidOperationException("Create payment response is invalid.");

			HAppsLog.Log($"Create payment response: orderId={response.orderId}, paymentUrl={response.paymentUrl}");
			Application.OpenURL(response.paymentUrl);

			return new MobileCreatePaymentResult
			{
				OrderId = response.orderId,
				PaymentUrl = response.paymentUrl
			};
		}

		private async Task RecoverSessionAsync()
		{
			var tokenSet = await _tokenStore.LoadAsync();

			if (!string.IsNullOrWhiteSpace(tokenSet?.RefreshToken))
			{
				try
				{
					HAppsLog.Log("Recovering mobile session via refreshSession");
					await RefreshSessionInternalAsync(tokenSet.RefreshToken);
					return;
				}
				catch (MobileApiException ex) when (IsInvalidMobileRefresh(ex))
				{
					HAppsLog.Warn($"Refresh token is invalid. Falling back to initSession. response={ex.ResponseBody}");
				}
			}

			HAppsLog.Log("Recovering mobile session via initSession");
			await InitSessionInternalAsync();
		}

		private static bool IsInvalidMobileSession(MobileApiException ex)
		{
			return ex.StatusCode == 401 && ex.ResponseBody.IndexOf("invalid_mobile_session", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool IsInvalidMobileRefresh(MobileApiException ex)
		{
			return ex.StatusCode == 401 && ex.ResponseBody.IndexOf("invalid_mobile_refresh", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void EnsureDeepLinkListener()
		{
			if (_deepLinkListener != null)
				return;

			var go = new GameObject("HAppsMobileDeepLinkListener");
			UnityEngine.Object.DontDestroyOnLoad(go);
			_deepLinkListener = go.AddComponent<HAppsMobileDeepLinkListener>();
			_deepLinkListener.DeepLinkReceived += HandleStrayDeepLink;
			HAppsLog.Log("Mobile deep link listener created");
		}

		private void HandleStrayDeepLink(string url)
		{
			if (string.IsNullOrEmpty(_pendingLoginState))
				return;

			HAppsLog.Log($"Pending mobile auth state is active. Deep link received: {url}");
		}

		private async Task<OidcDiscoveryDocument> GetDiscoveryAsync()
		{
			if (_discovery != null)
				return _discovery;

			var authority = _options.Authority.TrimEnd('/');
			var url = $"{authority}/.well-known/openid-configuration";
			HAppsLog.Log($"Loading OIDC discovery: {url}");
			var json = await SendGetAsync(url);
			HAppsLog.Log($"OIDC discovery response: {json}");
			_discovery = JsonUtility.FromJson<OidcDiscoveryDocument>(json);

			if (_discovery == null || string.IsNullOrEmpty(_discovery.authorization_endpoint) || string.IsNullOrEmpty(_discovery.token_endpoint))
				throw new InvalidOperationException("OIDC discovery document is invalid.");

			return _discovery;
		}

		private async Task<MobileSession> InitSessionInternalAsync()
		{
			if (string.IsNullOrWhiteSpace(_options.InitSessionUrl))
				throw new InvalidOperationException("Portal mobile session endpoint is not configured.");

			var payload = new InitSessionRequest
			{
				clientId = _options.ClientId
			};
			var json = JsonUtility.ToJson(payload);

			HAppsLog.Log($"Requesting portal initSession: url={_options.InitSessionUrl}");
			HAppsLog.Log($"Portal initSession request body: {json}");
			using var request = new UnityWebRequest(_options.InitSessionUrl, UnityWebRequest.kHttpVerbPOST);
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Accept", "application/json");

			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			var responseText = request.downloadHandler.text;
			HAppsLog.Log($"Portal initSession response: {responseText}");

			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"Portal initSession request failed: endpoint={_options.InitSessionUrl} error={request.error} response={responseText}");
				throw new MobileApiException((long)request.responseCode, responseText, $"Portal initSession request failed: {request.error} {responseText}");
			}

			var response = JsonUtility.FromJson<InitSessionResponse>(responseText);
			if (response == null || string.IsNullOrWhiteSpace(response.accessToken) || string.IsNullOrWhiteSpace(response.refreshToken))
				throw new InvalidOperationException("Portal initSession response is invalid.");

			var session = new MobileSession
			{
				AccessToken = response.accessToken,
				RefreshToken = response.refreshToken,
				PublicId = response.publicId,
				Verified = response.verified
			};

			_currentSession = session;
			_loggedIn = true;
			_userData = new UserData
			{
				userId = response.publicId,
				verified = response.verified
			};

			var tokenSet = await _tokenStore.LoadAsync() ?? new MobileTokenSet();
			tokenSet.PortalToken = response.accessToken;
			tokenSet.AccessToken = response.accessToken;
			tokenSet.RefreshToken = response.refreshToken;
			tokenSet.PublicId = response.publicId;
			tokenSet.Verified = response.verified;
			await _tokenStore.SaveAsync(tokenSet);

			HAppsLog.Log($"Portal initSession updated: publicId={response.publicId}, verified={response.verified}, accessTokenLength={response.accessToken?.Length ?? 0}, refreshTokenLength={response.refreshToken?.Length ?? 0}");
			return session;
		}

		private async Task<MobileSession> RefreshSessionInternalAsync(string refreshToken)
		{
			if (string.IsNullOrWhiteSpace(_options.RefreshSessionUrl))
				throw new InvalidOperationException("Portal mobile refresh endpoint is not configured.");

			var payload = new RefreshSessionRequest
			{
				refreshToken = refreshToken
			};
			var json = JsonUtility.ToJson(payload);

			HAppsLog.Log($"Requesting portal refreshSession: url={_options.RefreshSessionUrl}");
			HAppsLog.Log($"Portal refreshSession request body: {json}");
			using var request = new UnityWebRequest(_options.RefreshSessionUrl, UnityWebRequest.kHttpVerbPOST);
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Accept", "application/json");

			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			var responseText = request.downloadHandler.text;
			HAppsLog.Log($"Portal refreshSession response: {responseText}");

			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"Portal refreshSession request failed: endpoint={_options.RefreshSessionUrl} error={request.error} response={responseText}");
				throw new MobileApiException((long)request.responseCode, responseText, $"Portal refreshSession request failed: {request.error} {responseText}");
			}

			var response = JsonUtility.FromJson<RefreshSessionResponse>(responseText);
			if (response == null || string.IsNullOrWhiteSpace(response.accessToken))
				throw new InvalidOperationException("Portal refreshSession response is invalid.");

			var session = new MobileSession
			{
				AccessToken = response.accessToken,
				RefreshToken = refreshToken,
				PublicId = response.publicId,
				Verified = response.verified
			};

			_currentSession = session;
			_loggedIn = true;
			_userData = new UserData
			{
				userId = response.publicId,
				verified = response.verified
			};

			var tokenSet = await _tokenStore.LoadAsync() ?? new MobileTokenSet();
			tokenSet.PortalToken = response.accessToken;
			tokenSet.AccessToken = response.accessToken;
			tokenSet.RefreshToken = refreshToken;
			tokenSet.PublicId = response.publicId;
			tokenSet.Verified = response.verified;
			await _tokenStore.SaveAsync(tokenSet);

			HAppsLog.Log($"Portal refreshSession updated: publicId={response.publicId}, verified={response.verified}, accessTokenLength={response.accessToken?.Length ?? 0}");
			return session;
		}

		private string BuildAuthorizeUrl(string authorizationEndpoint, string state, string nonce, string codeChallenge)
		{
			var query = new Dictionary<string, string>
			{
				["client_id"] = _options.ClientId,
				["redirect_uri"] = _options.RedirectUri,
				["response_type"] = "code",
				["scope"] = string.IsNullOrWhiteSpace(_options.Scope) ? "openid profile offline_access" : _options.Scope,
				["code_challenge"] = codeChallenge,
				["code_challenge_method"] = "S256",
				["state"] = state,
				["nonce"] = nonce
			};

			if (!string.IsNullOrWhiteSpace(_currentSession?.PublicId))
				query["linkId"] = _currentSession.PublicId;

			return $"{authorizationEndpoint}?{ToQueryString(query)}";
		}

		private async void HandleLoginDeepLink(
			string url,
			string expectedState,
			string codeVerifier,
			TaskCompletionSource<MobileLoginResult> loginTcs)
		{
			if (loginTcs.Task.IsCompleted)
				return;

			try
			{
				HAppsLog.Log($"Handling mobile deep link: {url}");

				if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(_options.RedirectUri, StringComparison.OrdinalIgnoreCase))
				{
					HAppsLog.Warn($"Ignoring deep link that does not match redirectUri. expected={_options.RedirectUri}");
					return;
				}

				var query = ParseQuery(url);
				HAppsLog.Log($"Deep link query parsed. Keys={string.Join(",", query.Keys)}");

				if (query.TryGetValue("error", out var error))
				{
					HAppsLog.Warn($"Mobile auth deep link returned error={error}");
					loginTcs.TrySetResult(new MobileLoginResult { Error = error });
					return;
				}

				if (!query.TryGetValue("state", out var returnedState) || returnedState != expectedState)
				{
					HAppsLog.Error($"OIDC state mismatch. expected={expectedState}, actual={returnedState}");
					loginTcs.TrySetException(new InvalidOperationException("OIDC state mismatch."));
					return;
				}

				if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
				{
					HAppsLog.Error("OIDC redirect does not contain authorization code.");
					loginTcs.TrySetException(new InvalidOperationException("OIDC redirect does not contain authorization code."));
					return;
				}

				HAppsLog.Log($"Authorization code received. length={code.Length}");
				var discovery = await GetDiscoveryAsync();
				var tokens = await ExchangeCodeAsync(
					discovery.token_endpoint,
					_options.ClientId,
					code,
					_options.RedirectUri,
					codeVerifier);

				await _tokenStore.SaveAsync(new MobileTokenSet
				{
					AccessToken = tokens.access_token,
					IdToken = tokens.id_token,
					RefreshToken = tokens.refresh_token,
					PortalToken = _currentSession?.AccessToken,
					PublicId = _currentSession?.PublicId,
					Verified = _currentSession?.Verified ?? false
				});

				_loggedIn = !string.IsNullOrEmpty(tokens.access_token);
				HAppsLog.Log($"Token exchange completed. accessTokenLength={tokens.access_token?.Length ?? 0}, refreshTokenLength={tokens.refresh_token?.Length ?? 0}");
				loginTcs.TrySetResult(new MobileLoginResult
				{
					AccessToken = tokens.access_token,
					IdToken = tokens.id_token,
					RefreshToken = tokens.refresh_token,
					TokenType = tokens.token_type,
					ExpiresIn = tokens.expires_in,
					Scope = tokens.scope,
					Code = code,
					CodeVerifier = codeVerifier,
					RedirectUri = _options.RedirectUri
				});
			}
			catch (Exception ex)
			{
				loginTcs.TrySetException(ex);
			}
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

		private static async Task<string> SendGetAsync(string url)
		{
			using var request = UnityWebRequest.Get(url);
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"GET request failed: {url} error={request.error}");
				throw new InvalidOperationException($"GET request failed: {request.error}");
			}

			return request.downloadHandler.text;
		}

		private static async Task<string> SendAuthorizedJsonPostAsync(string url, string bearerToken, string json)
		{
			using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json ?? "{}"));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Accept", "application/json");
			request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"Authorized POST request failed: {url} error={request.error} response={request.downloadHandler.text}");
				throw new MobileApiException((long)request.responseCode, request.downloadHandler.text, $"POST request failed: {request.error} {request.downloadHandler.text}");
			}

			return request.downloadHandler.text;
		}

		private static async Task<OidcTokenResponse> ExchangeCodeAsync(
			string tokenEndpoint,
			string clientId,
			string code,
			string redirectUri,
			string codeVerifier)
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
			HAppsLog.Log($"Exchanging authorization code at {tokenEndpoint}. codeLength={code?.Length ?? 0}, verifierLength={codeVerifier?.Length ?? 0}");
			HAppsLog.Log($"Token exchange request URL: {tokenEndpoint}");
			HAppsLog.Log($"Token exchange request body: {payload}");
			using var request = new UnityWebRequest(tokenEndpoint, UnityWebRequest.kHttpVerbPOST);
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			var responseText = request.downloadHandler.text;
			HAppsLog.Log($"Token exchange response: {responseText}");

			if (request.result != UnityWebRequest.Result.Success)
			{
				HAppsLog.Error($"Token exchange failed: endpoint={tokenEndpoint} error={request.error} response={responseText}");
				throw new InvalidOperationException($"Token exchange failed: {request.error} {responseText}");
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

		private static string ToBase64Url(byte[] bytes)
		{
			return Convert.ToBase64String(bytes)
				.TrimEnd('=')
				.Replace('+', '-')
				.Replace('/', '_');
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
			public string refresh_token;
			public string token_type;
			public int expires_in;
			public string scope;
		}

		[Serializable]
		private sealed class InitSessionResponse
		{
			public string accessToken;
			public string refreshToken;
			public string publicId;
			public bool verified;
		}

		[Serializable]
		private sealed class InitSessionRequest
		{
			public string clientId;
		}

		[Serializable]
		private sealed class RefreshSessionRequest
		{
			public string refreshToken;
		}

		[Serializable]
		private sealed class RefreshSessionResponse
		{
			public string accessToken;
			public string publicId;
			public bool verified;
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
