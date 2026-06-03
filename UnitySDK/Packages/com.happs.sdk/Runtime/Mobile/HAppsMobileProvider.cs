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
				HandleLoginDeepLink(url, state, codeVerifier, discovery, loginTcs);
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
				Application.OpenURL(authorizeUrl);

				var result = await loginTcs.Task;

				if (result.IsSuccess)
				{
					_userData = result.User;
					_loggedIn = result.User != null || !string.IsNullOrEmpty(result.AccessToken);
				}

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
			await _tokenStore.ClearAsync();
			_userData = null;
			_loggedIn = false;

			if (_options == null)
				return;

			var discovery = await GetDiscoveryAsync();
			if (string.IsNullOrEmpty(discovery.end_session_endpoint) || string.IsNullOrEmpty(_options.PostLogoutRedirectUri))
				return;

			Application.OpenURL($"{discovery.end_session_endpoint}?post_logout_redirect_uri={Uri.EscapeDataString(_options.PostLogoutRedirectUri)}");
		}

		public async Task RefreshSessionAsync()
		{
			EnsureConfigured();
			var stored = await _tokenStore.LoadAsync();

			if (stored == null || string.IsNullOrEmpty(stored.RefreshToken))
				throw new InvalidOperationException("No refresh token is available.");

			var discovery = await GetDiscoveryAsync();
			var tokenResponse = await RequestTokenAsync(discovery.token_endpoint, new Dictionary<string, string>
			{
				["grant_type"] = "refresh_token",
				["client_id"] = _options.ClientId,
				["refresh_token"] = stored.RefreshToken
			});

			var tokenSet = new MobileTokenSet
			{
				AccessToken = tokenResponse.access_token,
				IdToken = string.IsNullOrEmpty(tokenResponse.id_token) ? stored.IdToken : tokenResponse.id_token,
				RefreshToken = string.IsNullOrEmpty(tokenResponse.refresh_token) ? stored.RefreshToken : tokenResponse.refresh_token
			};

			await _tokenStore.SaveAsync(tokenSet);

			if (!string.IsNullOrEmpty(discovery.userinfo_endpoint) && !string.IsNullOrEmpty(tokenSet.AccessToken))
			{
				_userData = await RequestUserInfoAsync(discovery.userinfo_endpoint, tokenSet.AccessToken);
				_loggedIn = _userData != null;
			}
			else
			{
				_loggedIn = !string.IsNullOrEmpty(tokenSet.AccessToken);
			}
		}

		public override async Task<UserData> GetProfile()
		{
			EnsureConfigured();
			var stored = await _tokenStore.LoadAsync();
			var discovery = await GetDiscoveryAsync();

			if (stored == null || string.IsNullOrEmpty(stored.AccessToken))
				return _userData;

			if (string.IsNullOrEmpty(discovery.userinfo_endpoint))
				return _userData;

			_userData = await RequestUserInfoAsync(discovery.userinfo_endpoint, stored.AccessToken);
			_loggedIn = _userData != null;
			return _userData;
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
			var json = await SendGetAsync(url);
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
			OidcDiscoveryDocument discovery,
			TaskCompletionSource<MobileLoginResult> loginTcs)
		{
			if (loginTcs.Task.IsCompleted)
				return;

			try
			{
				if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(_options.RedirectUri, StringComparison.OrdinalIgnoreCase))
					return;

				var query = ParseQuery(url);

				if (query.TryGetValue("error", out var error))
				{
					loginTcs.TrySetResult(new MobileLoginResult { Error = error });
					return;
				}

				if (!query.TryGetValue("state", out var returnedState) || returnedState != expectedState)
				{
					loginTcs.TrySetException(new InvalidOperationException("OIDC state mismatch."));
					return;
				}

				if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
				{
					loginTcs.TrySetException(new InvalidOperationException("OIDC redirect does not contain authorization code."));
					return;
				}

				var tokenResponse = await RequestTokenAsync(discovery.token_endpoint, new Dictionary<string, string>
				{
					["grant_type"] = "authorization_code",
					["client_id"] = _options.ClientId,
					["code"] = code,
					["redirect_uri"] = _options.RedirectUri,
					["code_verifier"] = codeVerifier
				});

				var tokenSet = new MobileTokenSet
				{
					AccessToken = tokenResponse.access_token,
					IdToken = tokenResponse.id_token,
					RefreshToken = tokenResponse.refresh_token
				};

				await _tokenStore.SaveAsync(tokenSet);

				UserData user = null;
				if (!string.IsNullOrEmpty(discovery.userinfo_endpoint) && !string.IsNullOrEmpty(tokenSet.AccessToken))
					user = await RequestUserInfoAsync(discovery.userinfo_endpoint, tokenSet.AccessToken);

				loginTcs.TrySetResult(new MobileLoginResult
				{
					AccessToken = tokenSet.AccessToken,
					IdToken = tokenSet.IdToken,
					RefreshToken = tokenSet.RefreshToken,
					User = user
				});
			}
			catch (Exception ex)
			{
				loginTcs.TrySetException(ex);
			}
		}

		private async Task<OidcTokenResponse> RequestTokenAsync(string tokenEndpoint, Dictionary<string, string> payload)
		{
			var body = Encoding.UTF8.GetBytes(ToQueryString(payload));
			using var request = new UnityWebRequest(tokenEndpoint, UnityWebRequest.kHttpVerbPOST);
			request.uploadHandler = new UploadHandlerRaw(body);
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			if (request.result != UnityWebRequest.Result.Success)
				throw new InvalidOperationException($"OIDC token request failed: {request.error}");

			var response = JsonUtility.FromJson<OidcTokenResponse>(request.downloadHandler.text);
			if (response != null && !string.IsNullOrEmpty(response.error))
				throw new InvalidOperationException($"OIDC token error: {response.error}");

			if (response == null || string.IsNullOrEmpty(response.access_token))
				throw new InvalidOperationException("OIDC token response is invalid.");

			return response;
		}

		private async Task<UserData> RequestUserInfoAsync(string userInfoEndpoint, string accessToken)
		{
			using var request = UnityWebRequest.Get(userInfoEndpoint);
			request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			if (request.result != UnityWebRequest.Result.Success)
				throw new InvalidOperationException($"OIDC userinfo request failed: {request.error}");

			var userInfo = JsonUtility.FromJson<OidcUserInfoResponse>(request.downloadHandler.text);
			if (userInfo == null)
				return null;

			return new UserData
			{
				userId = userInfo.sub,
				userName = !string.IsNullOrEmpty(userInfo.preferred_username) ? userInfo.preferred_username : userInfo.name,
				verified = userInfo.email_verified
			};
		}

		private static async Task<string> SendGetAsync(string url)
		{
			using var request = UnityWebRequest.Get(url);
			request.SetRequestHeader("Accept", "application/json");
			var operation = request.SendWebRequest();
			await AwaitAsyncOperation(operation);

			if (request.result != UnityWebRequest.Result.Success)
				throw new InvalidOperationException($"GET request failed: {request.error}");

			return request.downloadHandler.text;
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
			public string scope;
			public string error;
		}

		[Serializable]
		private sealed class OidcUserInfoResponse
		{
			public string sub;
			public string name;
			public string preferred_username;
			public bool email_verified;
		}
	}
}
