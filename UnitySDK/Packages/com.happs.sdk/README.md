# HApps Unity SDK

Unity SDK 3.0.1 for HApps WebGL integrations through JS SDK 1.0.3 and native Android integrations.

## Installation

Add the package to your Unity project through `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.0.1"
  }
}
```

Use a release tag such as `v3.0.1`. During development you can temporarily point to a commit hash instead of a tag.

For an existing WebGL project, follow [WebGL Migration: SDK 2.0.6 to 3.0.1](MIGRATION_WEB_2.0.6_TO_3.0.1.md).

## Runtime API

```csharp
Task<bool> HApps.Web.Connect()
Task<UserData> HApps.Web.GetProfile()
Task<PaymentData> HApps.Web.MakePayment(string orderId)
Task<AuthPopupData> HApps.Web.OpenIdpAuthPopup(string url)
Task<bool> HApps.Web.OpenPortalAuthPopup()
void HApps.Web.OpenAgeVerification(bool adultMode = true)
void HApps.Web.SetTheaterMode(bool enabled)
event Action<UserData, SignatureData> HApps.Web.AuthCompleted
bool HApps.Web.IsPortalSite()
bool HApps.Web.IsReady()

Task<MobileSession> HApps.Mobile.InitSessionAsync()
Task<MobileLoginResult> HApps.Mobile.LoginAsync()
Task<MobileSession> HApps.Mobile.RefreshSessionAsync()
Task<MobileCreatePaymentResult> HApps.Mobile.CreatePaymentAsync(MobileCreatePaymentRequest request)
Task HApps.Mobile.LogoutAsync()

void HApps.ConfigureMobile(HAppsMobileAuthOptions options, IMobileTokenStore tokenStore = null)
void HApps.SetDebugLogging(bool enabled)
void HApps.Shutdown()
```

## WebGL Bridge Requirements

Your WebGL page must:

- load `https://hooli.games/public/js/sdk/1.0.3/hooligapps.js` or `https://hooli.games/public/js/sdk/1.0.3/hooligapps.debug.js`
- use the existing JS SDK `1.0.3` contract; unversioned builds are not supported by Unity SDK `3.0.1`
- initialize the browser bridge with `HApps.init(...)`
- use `unityObjectName: "HAppsJSBridge"`
- use `unityMethodName: "OnMessage"`
- set `isPortal: false` for standalone pages
- set `isPortal: true` and provide `ssoLoginUrl` for embedded portal pages

`debug` is not an `HApps.init(...)` option in JS SDK 1.0.3. Load `hooligapps.debug.js` instead of `hooligapps.js` when browser-side debug output is needed. In standalone mode, the bridge `ready` promise resolves immediately with `user: null`; authentication is performed through `OpenIdpAuthPopup(url)`.

Do not use the embedded `HApps.init(...).ready` promise as the Unity readiness gate with deployed JS SDK 1.0.3: its initial successful portal login does not resolve that promise. Use `await HApps.Web.Connect()` in Unity instead.

## Web Integration Modes

Standalone IDP popup flow:

- use `HApps.Web.OpenIdpAuthPopup(url)`
- inspect returned `AuthPopupData`
- supported results:
  - `ticket`: exchange `ticket` on your backend
  - `cookie`: auth already completed through cookie session
  - `cancelled`: popup flow did not complete

Embedded portal flow:

- call `HApps.Web.Connect()` to receive platform context and current portal signature
- send `HApps.Web.Signature` to your backend if you need server-side user resolution
- call `HApps.Web.OpenPortalAuthPopup()` when the game must show portal login UI
- call `HApps.Web.OpenAgeVerification()` when the game must show portal age verification UI
- subscribe to `HApps.Web.AuthCompleted` if auth can complete outside the awaited popup flow

If the connected profile is already verified, `OpenPortalAuthPopup()` returns `true` locally without opening a popup or emitting a new `AuthCompleted` event.

`SetTheaterMode(bool)` remains in the Unity API, but JS SDK 1.0.3 does not dispatch the `set_theater_mode` event. Do not depend on it with the supported browser contract.

Example subscription:

```csharp
private void OnEnable()
{
    HApps.Web.AuthCompleted += HandleAuthCompleted;
}

private void OnDisable()
{
    HApps.Web.AuthCompleted -= HandleAuthCompleted;
}

private void HandleAuthCompleted(UserData user, SignatureData signature)
{
    Debug.Log($"auth_complete: {user?.userId}, {signature?.signature}");
}
```

## Mobile Flow

The mobile provider uses:

- portal session bootstrap through `initSession`
- OIDC Authorization Code + PKCE for user login
- deep link callback back into the app
- portal payment creation with browser redirect to checkout

Configure once:

```csharp
HApps.ConfigureMobile(new HAppsMobileAuthOptions
{
    Authority = "https://portal.igra.rocks/idp/oidc",
    ClientId = "your-mobile-client-id",
    RedirectUri = "com.example.game://auth/callback",
    PostLogoutRedirectUri = "com.example.game://logout",
    DeviceRegisterUrl = "https://portal.igra.rocks/api/v1/mobile/device/register",
    InitSessionUrl = "https://portal.igra.rocks/api/v1/mobile/session/init",
    OidcStartUrl = "https://portal.igra.rocks/api/v1/mobile/oidc/start",
    OidcExchangeUrl = "https://portal.igra.rocks/api/v1/mobile/oidc/exchange",
    OidcLogoutUrl = "https://portal.igra.rocks/api/v1/mobile/oidc/logout",
    CreatePaymentUrl = "https://portal.igra.rocks/api/v1/mobile/payments",
    HttpTimeoutSeconds = 30,
    LoginTimeoutMs = 180000
});
```

`HttpTimeoutSeconds` applies separately to every mobile API request. `LoginTimeoutMs` limits the wait for the app deep-link callback after opening the system browser. Logout always removes local credentials, even if the remote logout endpoint is unavailable.

Typical flow:

```csharp
var session = await HApps.Mobile.InitSessionAsync();

if (!session.Verified)
{
    var login = await HApps.Mobile.LoginAsync();
    if (!login.IsSuccess)
    {
        Debug.LogError(login.Error);
        return;
    }
}

var payment = await HApps.Mobile.CreatePaymentAsync(new MobileCreatePaymentRequest
{
    RequestId = "req-123",
    ProductId = "coins_pack_1",
    Price = 1.99m,
    Currency = "USD",
    Description = "Coins Pack 1"
});
```

Notes:

- Android is the only supported native mobile runtime in this release
- Android API 23 or newer is required by the default AES-GCM Android Keystore token store
- `InitSessionAsync()` ensures a device keypair exists, registers the device if needed, then calls signed `session/init`
- concurrent `InitSessionAsync()` and `RefreshSessionAsync()` calls share one session operation
- `LoginAsync()` starts OIDC login through `oidc/start`, exchanges the authorization `code`, then calls `oidc/exchange` and a fresh `session/init`
- only one `LoginAsync()` operation can be active; another login attempt fails instead of replacing its callback state
- after `oidc/exchange`, the SDK switches the device to the new account-linked mobile session
- `CreatePaymentAsync()` sends the current portal access token in `Authorization: Bearer ...`
- on `401 invalid_mobile_session` or `401 mobile_session_expired` the SDK retries through `InitSessionAsync()`
- `LogoutAsync()` calls `oidc/logout`, opens the returned logout URL, and clears local mobile device state
- logout invalidates queued session work and cancels an active login; `HApps.Shutdown()` rejects late state updates, although an already started HTTP request may still finish internally
- `CreatePaymentAsync()` opens the returned `paymentUrl`, but does not verify final payment status
- when no custom `IMobileTokenStore` is supplied, Android tokens are encrypted with AES-GCM using a non-exportable Android Keystore key
- `PlayerPrefsMobileTokenStore` is an insecure legacy/dev option because it stores tokens as plaintext
- `HAppsMobileAuthOptions.Scope` remains for source compatibility but is not sent by this SDK version; the server controls requested OIDC scopes

## Notes

- `IsPortalSite()` depends on `window.HApps.isPortal()`
- `IsReady()` depends on `window.HApps.isReady()`
- `OpenIdpAuthPopup(url)` returns `AuthPopupData`, not plain `string`
- `AuthPopupData` supports both ticket-based and cookie-based session auth
- `Connect()` and `OpenPortalAuthPopup()` are separate steps
- `OpenAgeVerification()` is a fire-and-forget bridge call with no completion callback
- `SetTheaterMode()` has no effect with JS SDK 1.0.3 because that browser version does not dispatch its event
- debug logging is disabled by default; `SetDebugLogging(true)` enables sanitized debug/warn logs, while errors always log
- SDK logs never include tokens, authorization codes, signatures, deep-link query strings, or auth request/response bodies
- `MakePayment()` accepts a backend-created `orderId`
- a second `MakePayment()` call throws `InvalidOperationException` while the first payment is still active; it does not replace the first operation
- mobile `GetProfile()` and mobile `MakePayment(orderId)` are not part of the current native flow
- sample scene/scripts remain in the host project, not in the package
