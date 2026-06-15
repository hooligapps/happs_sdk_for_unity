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

		public void Configure(HAppsMobileAuthOptions options, IMobileTokenStore tokenStore = null)
		{
			_options = options ?? throw new ArgumentNullException(nameof(options));
			_tokenStore = tokenStore ?? new InMemoryMobileTokenStore();
			HAppsLog.Log($"Mobile configured: authority={_options.Authority}, clientId={_options.ClientId}, redirectUri={_options.RedirectUri}, logoutRedirectUri={_options.PostLogoutRedirectUri}, scope={_options.Scope}");
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
			HAppsLog.Log("Mobile logout: clearing local state");
			_userData = null;
			_loggedIn = false;
			await _tokenStore.ClearAsync();
		}

		public Task RefreshSessionAsync()
		{
			throw new NotSupportedException("RefreshSessionAsync is not implemented yet.");
		}

		public override Task<UserData> GetProfile()
		{
			throw new NotSupportedException("GetProfile is not implemented for mobile auth yet. Resolve user profile via your backend after game-login.");
		}

		public override Task<PaymentData> MakePayment(string orderId)
		{
			throw new NotSupportedException("Mobile payment flow is not implemented yet.");
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
					RefreshToken = tokens.refresh_token
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

	}
}
