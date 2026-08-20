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

The SDK is distributed as a Unity package from:

- `UnitySDK/Packages/com.happs.sdk`

Upgrading an existing WebGL integration from SDK 2.0.6: see [WebGL Migration: 2.0.6 to 3.0.1](UnitySDK/Packages/com.happs.sdk/MIGRATION_WEB_2.0.6_TO_3.0.1.md).

This SDK supports three distinct integration modes:

1. Standalone WebGL auth via backend IDP popup
2. Embedded portal integration via JS bridge
3. Native Android session, OIDC login, and payment creation

## Supported Public API

The supported integration surface is the static `HApps` facade. The preferred grouped API is:

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

Method semantics:

- `HApps.Web.Connect()` requests platform data through the initialized browser bridge, stores portal signature on the provider, and waits for the platform connect response.
- `HApps.Web.GetProfile()` requests the current user profile from the platform.
- `HApps.Web.MakePayment(orderId)` starts a payment flow for an already created backend order.
- `HApps.Web.OpenIdpAuthPopup(url)` opens standalone backend auth popup and returns `AuthPopupData` for either ticket-based or cookie-based session auth.
- `HApps.Web.OpenPortalAuthPopup()` opens portal-managed auth UI and returns `true` when portal auth completes successfully. If the connected profile is already verified, it returns `true` locally without opening a popup or emitting a new `AuthCompleted` event.
- `HApps.Web.OpenAgeVerification(adultMode)` opens portal-managed age verification UI from the game.
- `HApps.Web.SetTheaterMode(enabled)` is retained in the public API, but deployed JS SDK 1.0.3 does not dispatch its `set_theater_mode` event. Do not depend on this call until the browser contract is updated.
- `HApps.Web.AuthCompleted` fires when the external page script sends `auth_complete`, even if you are not awaiting `OpenPortalAuthPopup()`.
- `HApps.Web.IsPortalSite()` reflects `window.HApps.isPortal()` from the JS environment.
- `HApps.Web.IsReady()` reflects `window.HApps.isReady()` from the JS environment.
- Debug logging is disabled by default. `HApps.SetDebugLogging(enabled)` toggles sanitized debug and warning logs; errors always log.
- Native mobile auth currently supports Android only. Its default token store encrypts data with an Android Keystore-backed AES-GCM key.
- `PlayerPrefsMobileTokenStore` is retained only as an explicitly insecure legacy/dev option.
- `HApps.Mobile` is the native/mobile branch for portal session bootstrap, OIDC login, and mobile payment creation.
- `Shutdown()` disposes both WebGL and mobile provider instances. Late mobile results can no longer update SDK state after shutdown.

## Choose Your Flow

Use standalone flow when:

- the game runs as standalone WebGL
- no portal iframe is involved
- login is initiated from the game UI

Use embedded portal flow when:

- the game runs inside the HApps portal
- the platform controls session and auth
- the WebGL template includes the platform JS bridge

## Standalone Flow

This flow does not require calling `await HApps.Web.Connect()` from Unity C#.

Your WebGL template still needs to load and initialize the browser bridge script so Unity can communicate with the page environment.

### Unity Example

```csharp
var url = $"{serverUrl}/api/auth/idp?token={launchToken}";
var authPopupData = await HApps.Web.OpenIdpAuthPopup(url);

switch (authPopupData.Flow)
{
    case AuthPopupFlow.Ticket:
        if (!string.IsNullOrEmpty(authPopupData.ticket))
        {
            await Gateway.Post("/api/auth/idp/finish", new { ticket = authPopupData.ticket });
        }
        break;

    case AuthPopupFlow.Cookie:
        // Auth already completed through cookie session.
        break;

    case AuthPopupFlow.Cancelled:
        return;
}
```

### Expected Result

- the popup authenticates the user via your backend
- Unity receives `AuthPopupData`
- popup auth supports two success modes:
  - `ticket`: your backend exchanges `ticket` for the real auth/session token
  - `cookie`: auth is already completed through cookie session without a ticket roundtrip

### Standalone WebGL Template Example

```html
<script src="https://hooli.games/public/js/sdk/1.0.3/hooligapps.debug.js"></script>
```

```javascript
function initHApps(unityInstance) {
    const PLATFORM_ORIGIN = "https://portal.example.com";

    if (typeof HApps === "undefined") {
        console.error("HApps SDK is not defined. Check script includes.");
        return;
    }

    const result = HApps.init({
        platformOrigin: PLATFORM_ORIGIN,
        isPortal: false,
        unityObjectName: "HAppsJSBridge",
        unityMethodName: "OnMessage",
        gameInstance: unityInstance
    });

    result.ready.then(function(data) {
        console.log("HApps ready, user:", data.user);
    }).catch(function(err) {
        console.error("HApps login failed:", err);
    });
}

createUnityInstance(canvas, config, onProgress).then((unityInstance) => {
    initHApps(unityInstance);
});
```

Required bridge config for Unity:

- `isPortal: false`
- `unityObjectName: "HAppsJSBridge"`
- `unityMethodName: "OnMessage"`

In standalone mode, `result.ready` resolves immediately with `user: null`. User authentication is performed later through `HApps.Web.OpenIdpAuthPopup(url)`.

### Backend Requirements

Required endpoints:

- `GET /api/auth/idp`
- `GET /api/auth/idp/callback`
- `POST /api/auth/idp/finish`

Backend must:

1. Generate OIDC URL with PKCE, state, and nonce.
2. Store auth state server-side with short TTL.
3. Redirect user to the identity provider.
4. On callback, validate state, exchange code, load user info, and issue a short-lived auth ticket.
5. On `/finish`, consume the ticket exactly once and issue the real auth token.

## Embedded Portal Flow

This flow requires platform JS bootstrap and Unity-side connection.

### WebGL Template Setup

Load one of the HApps browser SDK scripts in the page and initialize it with `HApps.init(...)`.

Choose one script variant:

```html
<!-- Development -->
<script src="https://hooli.games/public/js/sdk/1.0.3/hooligapps.debug.js"></script>

<!-- Production -->
<!-- <script src="https://hooli.games/public/js/sdk/1.0.3/hooligapps.js"></script> -->
```

### Portal WebGL Template Example

Example portal page setup:

```html
<script src="https://hooli.games/public/js/sdk/1.0.3/hooligapps.debug.js"></script>
```

```javascript
function initHApps(unityInstance) {
    const BACKEND_HOST = "https://your-backend.example/api";
    const PLATFORM_ORIGIN = "https://hooli.games";

    if (typeof HApps === "undefined") {
        console.error("HApps SDK is not defined. Check script includes.");
        return;
    }

    HApps.init({
        platformOrigin: PLATFORM_ORIGIN,
        ssoLoginUrl: BACKEND_HOST + "/sign",
        isPortal: true,
        unityObjectName: "HAppsJSBridge",
        unityMethodName: "OnMessage",
        gameInstance: unityInstance
    });
}

createUnityInstance(canvas, config, onProgress).then((unityInstance) => {
    initHApps(unityInstance);
});
```

Required bridge config for Unity:

- `isPortal: true`
- `unityObjectName: "HAppsJSBridge"`
- `unityMethodName: "OnMessage"`

Common `HApps.init(...)` config:

- `platformOrigin`
- `gameInstance`
- `unityObjectName: "HAppsJSBridge"`
- `unityMethodName: "OnMessage"`

Mode-specific config:

- standalone: `isPortal: false`
- embedded portal: `isPortal: true` and `ssoLoginUrl`

Optional config:

- `maxRetries`
- `retryDelayMs`

`debug` is not an `HApps.init(...)` option in JS SDK 1.0.3. Select `hooligapps.debug.js` when browser-side debug output is needed.

Do not use the embedded `result.ready` promise as the Unity readiness gate with deployed JS SDK 1.0.3: its initial successful portal login does not resolve that promise. Use `await HApps.Web.Connect()` in Unity. This limitation does not apply to standalone mode, where `ready` resolves immediately.

### Recommended Unity Flow

```csharp
if (!HApps.Web.IsPortalSite())
{
    // Use your own fallback or error handling here.
    return;
}

var connected = await HApps.Web.Connect();
if (!connected)
{
    // Handle connection failure.
    return;
}

var signature = HApps.Web.Signature;
if (string.IsNullOrEmpty(signature))
{
    // Handle missing portal signature.
    return;
}

var authResponse = await Gateway.Post("/api/auth/portal", new
{
    signature = signature
});

var profile = await HApps.Web.GetProfile();
```

Interactive portal login from inside the game is a separate flow:

```csharp
var portalAuthOk = await HApps.Web.OpenPortalAuthPopup();
if (!portalAuthOk)
{
    // Handle portal auth failure.
    return;
}

var authResponse = await Gateway.Post("/api/auth/portal", new
{
    signature = HApps.Web.Signature
});
```

If you need to react to an external `auth_complete` without awaiting `OpenPortalAuthPopup()`, subscribe to the event:

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

### Notes

- `Connect()` is for embedded flow only.
- `Connect()` gives Unity access to platform-side context and stores portal signature in `HApps.Web.Signature`.
- your game backend should use that signature to resolve the authenticated user/session on the server side
- `OpenPortalAuthPopup()` is the public auth entrypoint for showing portal login UI from the game
- if the connected profile is already verified, `OpenPortalAuthPopup()` returns `true` locally and does not emit a new `AuthCompleted` event
- `GetProfile()` should be called after connection and, if needed by your flow, after portal auth completes
- `IsPortalSite()` depends on `window.HApps.isPortal()`. It is an environment signal, not a user-profile fetch.

## Authentication Model

The SDK exposes two different auth entrypoints because the ownership of auth is different in each flow.

### `OpenIdpAuthPopup(url)`

Use this when auth is handled by your backend.

- input: backend-generated auth URL
- result: `AuthPopupData`
- popup auth supports two success modes:
  - `ticket`: your backend exchanges the ticket for the real session token
  - `cookie`: auth is already completed through cookie session

### `OpenPortalAuthPopup()`

Use this when auth is handled by the portal.

- input: no parameters
- result: `bool`
- follow-up: after success, the portal auth popup completes and updated profile/signature data become available through the SDK flow
- typical use: call your backend again with the updated `HApps.Web.Signature`
- already verified profile: returns `true` locally without a popup or a new `AuthCompleted` event

## Portal Auth Flow

Embedded portal auth works in two stages:

1. The page initializes the browser bridge with `HApps.init(...)`.
2. Unity calls `HApps.Web.Connect()` to receive platform context and store portal signature in `HApps.Web.Signature`.
3. Your backend can use that signature to resolve or create the authenticated user/session.
4. Unity can also call `HApps.Web.OpenPortalAuthPopup()` to show portal auth UI from inside the game.
5. After portal auth completes, your backend can use the refreshed signature if that flow needs server-side auth resolution.

These methods are not interchangeable.

## Profile

```csharp
var profile = await HApps.Web.GetProfile();

if (profile != null)
{
    Debug.Log($"{profile.userName} ({profile.userId})");
}
```

`UserData` currently contains:

- `userId`
- `userName`
- `verified`

## Mobile Flow

The mobile provider is built around three separate responsibilities:

- portal session bootstrap
- OIDC login in the system browser
- payment creation that opens checkout in the browser

Configure it once:

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

Recommended sequence:

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

Mobile behavior:

- `InitSessionAsync()` ensures a device keypair exists, registers the device if needed, then calls portal `device/register` and signed `session/init`
- `LoginAsync()` runs OIDC Authorization Code + PKCE, calls `oidc/start`, exchanges the returned `code` for `id_token`, then calls `oidc/exchange` and a fresh `session/init`
- after `oidc/exchange`, the SDK switches the device to the new account-linked mobile session
- `CreatePaymentAsync()` sends the current portal access token in `Authorization: Bearer ...`
- if payment creation returns `401 invalid_mobile_session` or `401 mobile_session_expired`, the SDK retries through `InitSessionAsync()`
- `LogoutAsync()` calls `oidc/logout`, opens the returned logout URL, and clears local mobile device state
- `CreatePaymentAsync()` only starts checkout and opens `paymentUrl`; payment confirmation stays on the integrator/backend side
- concurrent `InitSessionAsync()` and `RefreshSessionAsync()` calls share one session operation
- only one `LoginAsync()` operation can be active; another login attempt fails instead of replacing its callback state
- logout invalidates queued session work and cancels an active login; shutdown rejects late state updates, although an already started HTTP request may still finish internally
- native mobile support requires Android API 23 or newer because the default token store uses AES-GCM with Android Keystore
- `HAppsMobileAuthOptions.Scope` remains for source compatibility but is not sent by this SDK version; the server controls requested OIDC scopes

## AuthPopupData

`OpenIdpAuthPopup(url)` returns `AuthPopupData`:

- `flow`
- `ticket`

Supported `flow` values:

- `ticket`
- `cookie`
- `cancelled`

Use `authPopupData.Flow` in Unity code:

- `ticket` means ticket-based session flow
- `cookie` means cookie-based session flow
- `cancelled` means the popup flow did not complete successfully

Read `authPopupData.ticket` only when the flow requires it.

## Payments

The SDK payment API is order-based.

```csharp
var payment = await HApps.Web.MakePayment(orderId);

if (payment.IsSuccess)
{
    // Grant rewards only after backend verification.
}
```

Important points:

- `orderId` must already be created by your backend/business layer.
- `MakePayment()` does not build an order for you.
- if `MakePayment()` is called again while the previous payment is still active, the second call throws `InvalidOperationException`; the first payment remains active
- client-side payment success is not enough to grant rewards
- backend verification is mandatory

Payment lifecycle:

`Unity -> JS -> Portal -> Checkout -> Portal -> JS -> Unity`

For mobile, the SDK only creates the payment and opens checkout. Final payment verification should be handled by your backend or app-specific flow.

## Backend Requirements For Embedded Flow

In addition to standalone endpoints, backend should support:

- `POST /api/sign`

Backend must:

1. Validate launch token with the platform.
2. Create or load the user.
3. Return the signature expected by JS SDK 1.0.3. User data comes from the platform launch message, not this response.

JS SDK 1.0.3 sends this request body:

```json
{
  "token": "platform-launch-signature"
}
```

Expected response shape:

```json
{
  "signature": "..."
}
```

## Known Caveats

- `IsPortalSite()` depends on the JS contract `window.HApps.isPortal()`.
- `MakePayment()` accepts `orderId`, not `PaymentItem`.
- `Connect()` and `GetProfile()` may fail if the JS bridge is not correctly wired in the WebGL template.
- `HApps.init(...)` in the page template and `HApps.Web.Connect()` in Unity are different steps. The first bootstraps the browser bridge, the second waits for the Unity-side bridge connection flow.
- the embedded `HApps.init(...).ready` promise does not resolve after the initial successful portal login in deployed JS SDK 1.0.3; use Unity `Connect()` as the readiness gate
- `SetTheaterMode()` is present in the Unity API, but JS SDK 1.0.3 does not dispatch `set_theater_mode`; the call has no effect with the supported browser contract.

## Security Requirements

- Always validate `event.origin` in JS.
- Never use `"*"` in `postMessage`.
- Use PKCE for OIDC.
- Validate `state` and `nonce`.
- Tickets must be single-use.
- Never trust client payment result.
- Always verify payment server-side.

## Version

HApps Unity SDK - Integration Guide v3.0.1 (JS SDK 1.0.3)
