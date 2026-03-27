# HApps Web SDK

Unity SDK for HApps WebGL integrations.

## Installation

Add the package to your Unity project through `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v2.0.0"
  }
}
```

The SDK is distributed as a Unity package from:

- `UnitySDK/Packages/com.happs.sdk`

This SDK supports two distinct integration modes:

1. Standalone WebGL auth via backend IDP popup
2. Embedded portal integration via JS bridge

## Supported Public API

The supported integration surface is the static `HApps` facade:

```csharp
Task<bool> HApps.Initialize()
Task<UserData> HApps.GetProfile()
Task<PaymentData> HApps.MakePayment(string orderId)
Task<AuthPopupData> HApps.OpenIdpAuthPopup(string url)
Task<bool> HApps.OpenPortalAuthPopup()
bool HApps.IsPortalSite()
void HApps.Shutdown()
```

Method semantics:

- `Initialize()` prepares embedded portal integration and waits for the platform init response.
- `GetProfile()` requests the current user profile from the platform.
- `MakePayment(orderId)` starts a payment flow for an already created backend order.
- `OpenIdpAuthPopup(url)` opens standalone backend auth popup and returns `AuthPopupData` for either ticket-based or cookie-based session auth.
- `OpenPortalAuthPopup()` starts portal-managed auth and returns `true` when portal auth completes successfully.
- `IsPortalSite()` reflects `window.HApps.isPortal()` from the JS environment.
- `Shutdown()` disposes the current provider instance.

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

This flow does not require calling `await HApps.Initialize()` from Unity C#.

Your WebGL template still needs to load and initialize the browser bridge script so Unity can communicate with the page environment.

### Unity Example

```csharp
var url = $"{serverUrl}/api/auth/idp?token={launchToken}";
var authPopupData = await HApps.OpenIdpAuthPopup(url);

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
<script src="https://hooli.games/public/js/sdk/hooligapps.debug.js"></script>
```

```javascript
function initHApps(unityInstance) {
    const BACKEND_HOST = "https://your-backend.example/api";
    const PLATFORM_ORIGIN = "https://portal.example.com";

    if (typeof HApps === "undefined") {
        console.error("HApps SDK is not defined. Check script includes.");
        return;
    }

    const result = HApps.init({
        platformOrigin: PLATFORM_ORIGIN,
        ssoLoginUrl: BACKEND_HOST + "/sign",
        unityObjectName: "HAppsJSBridge",
        unityMethodName: "OnMessage",
        gameInstance: unityInstance,
        debug: true
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

- `unityObjectName: "HAppsJSBridge"`
- `unityMethodName: "OnMessage"`

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

This flow requires platform JS and SDK initialization.

### WebGL Template Setup

Load one of the HApps browser SDK scripts in the page and initialize it with `HApps.init(...)`.

Choose one script variant:

```html
<!-- Development -->
<script src="https://hooli.games/public/js/sdk/hooligapps.debug.js"></script>

<!-- Production -->
<!-- <script src="https://hooli.games/public/js/sdk/hooligapps.js"></script> -->
```

### Portal WebGL Template Example

Example portal page setup:

```html
<script src="https://hooli.games/public/js/sdk/hooligapps.debug.js"></script>
```

```javascript
function initHApps(unityInstance) {
    const BACKEND_HOST = "https://your-backend.example/api";
    const PLATFORM_ORIGIN = "https://hooli.games";

    if (typeof HApps === "undefined") {
        console.error("HApps SDK is not defined. Check script includes.");
        return;
    }

    const result = HApps.init({
        platformOrigin: PLATFORM_ORIGIN,
        ssoLoginUrl: BACKEND_HOST + "/sign",
        unityObjectName: "HAppsJSBridge",
        unityMethodName: "OnMessage",
        gameInstance: unityInstance,
        debug: true
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

- `unityObjectName: "HAppsJSBridge"`
- `unityMethodName: "OnMessage"`

Minimal `HApps.init(...)` config:

- `platformOrigin`
- `ssoLoginUrl`
- `gameInstance`

Optional config:

- `maxRetries`
- `retryDelayMs`

### Recommended Unity Flow

```csharp
if (!HApps.IsPortalSite())
{
    // Use your own fallback or error handling here.
    return;
}

var initialized = await HApps.Initialize();
if (!initialized)
{
    // Handle initialization failure.
    return;
}

var authOk = await HApps.OpenPortalAuthPopup();
if (!authOk)
{
    // Handle auth failure.
    return;
}

var profile = await HApps.GetProfile();
```

### Notes

- `Initialize()` is for embedded flow only.
- `OpenPortalAuthPopup()` is the public auth entrypoint for portal-managed login.
- `GetProfile()` should be called after initialization and, if needed by your flow, after portal auth completes.
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
- follow-up: after success, profile and signature data become available through the SDK flow

These methods are not interchangeable.

## Profile

```csharp
var profile = await HApps.GetProfile();

if (profile != null)
{
    Debug.Log($"{profile.userName} ({profile.userId})");
}
```

`UserData` currently contains:

- `userId`
- `userName`
- `verified`

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
var payment = await HApps.MakePayment(orderId);

if (payment.IsSuccess)
{
    // Grant rewards only after backend verification.
}
```

Important points:

- `orderId` must already be created by your backend/business layer.
- `MakePayment()` does not build an order for you.
- client-side payment success is not enough to grant rewards
- backend verification is mandatory

Payment lifecycle:

`Unity -> JS -> Portal -> Checkout -> Portal -> JS -> Unity`

## Backend Requirements For Embedded Flow

In addition to standalone endpoints, backend should support:

- `POST /api/sign`

Backend must:

1. Validate launch token with the platform.
2. Create or load the user.
3. Return user and signature data required by the client flow.

Example shape:

```json
{
  "userData": { "id": "123" },
  "signatureData": { "signature": "..." }
}
```

## Known Caveats

- `IsPortalSite()` depends on the JS contract `window.HApps.isPortal()`.
- `MakePayment()` accepts `orderId`, not `PaymentItem`.
- `Initialize()` and `GetProfile()` may fail if the JS bridge is not correctly wired in the WebGL template.
- `HApps.init(...)` in the page template and `HApps.Initialize()` in Unity are different steps. The first bootstraps the browser bridge, the second waits for the Unity-side embedded init flow.

## Security Requirements

- Always validate `event.origin` in JS.
- Never use `"*"` in `postMessage`.
- Use PKCE for OIDC.
- Validate `state` and `nonce`.
- Tickets must be single-use.
- Never trust client payment result.
- Always verify payment server-side.

## Version

HApps Web SDK - Integration Guide v2.1
