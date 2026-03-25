# HApps Web SDK

Unity SDK for HApps WebGL integrations.

This SDK supports two distinct integration modes:

1. Standalone WebGL auth via backend IDP popup
2. Embedded portal integration via JS bridge

## Supported Public API

The supported integration surface is the static `HApps` facade:

```csharp
Task<bool> HApps.Initialize()
Task<UserData> HApps.GetProfile()
Task<PaymentData> HApps.MakePayment(string orderId)
Task<string> HApps.OpenIdpAuthPopup(string url)
Task<bool> HApps.OpenPortalAuthPopup()
bool HApps.IsPortalSite()
void HApps.Shutdown()
```

Method semantics:

- `Initialize()` prepares embedded portal integration and waits for the platform init response.
- `GetProfile()` requests the current user profile from the platform.
- `MakePayment(orderId)` starts a payment flow for an already created backend order.
- `OpenIdpAuthPopup(url)` opens standalone backend auth popup and returns a short-lived auth ticket.
- `OpenPortalAuthPopup()` starts portal-managed auth and returns `true` when portal auth completes successfully.
- `IsPortalSite()` reflects `window.HApps.isPortal` from the JS environment.
- `Shutdown()` disposes the current provider instance.

`HApps.Provider` is public in code, but integrators should treat the `HApps` static methods above as the primary API.

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

This flow does not require SDK initialization.

### Unity Example

```csharp
var url = $"{serverUrl}/api/auth/idp?token={launchToken}";
var ticket = await HApps.OpenIdpAuthPopup(url);

if (!string.IsNullOrEmpty(ticket))
{
    await Gateway.Post("/api/auth/idp/finish", new { ticket });
}
```

### Expected Result

- the popup authenticates the user via your backend
- Unity receives an auth ticket
- your backend exchanges that ticket for the real auth/session token

### Backend Requirements

Required endpoints:

- `GET /api/auth/idp`
- `GET /api/auth/idp/callback`
- `POST /api/auth/idp/finish`

Backend must:

1. Generate OIDC URL with PKCE, state, and nonce.
2. Store auth state in Redis with short TTL.
3. Redirect user to the identity provider.
4. On callback, validate state, exchange code, load user info, and issue a short-lived auth ticket.
5. On `/finish`, consume the ticket exactly once and issue the real auth token.

Suggested Redis keys:

- `auth_state:{state}`
- `auth_ticket:{ticket}`

## Embedded Portal Flow

This flow requires platform JS and SDK initialization.

### WebGL Template Setup

Your `index.html` must include the platform script:

```html
<script src="hooligapps.js"></script>
<script>
    HooligappsIntegration.init({
        platformOrigin: "https://portal.domain",
        unityObjectName: "HAppsJSBridge",
        unityMethodName: "OnHooligappsMessage"
    });
</script>
```

After Unity is created:

```javascript
createUnityInstance(canvas, config).then(instance => {
    window.unityInstance = instance;
    HooligappsIntegration.setUnityInstance(instance);
});
```

### Recommended Unity Flow

```csharp
if (!HApps.IsPortalSite())
{
    throw new Exception("Embedded flow requires portal environment");
}

var initialized = await HApps.Initialize();
if (!initialized)
{
    throw new Exception("HApps initialization failed");
}

var authOk = await HApps.OpenPortalAuthPopup();
if (!authOk)
{
    throw new Exception("Portal auth failed");
}

var profile = await HApps.GetProfile();
```

### Notes

- `Initialize()` is for embedded flow only.
- `OpenPortalAuthPopup()` is the public auth entrypoint for portal-managed login.
- `GetProfile()` should be called after initialization and, if needed by your flow, after portal auth completes.
- `IsPortalSite()` depends on `window.HApps.isPortal`. It is an environment signal, not a user-profile fetch.

## Authentication Model

The SDK exposes two different auth entrypoints because the ownership of auth is different in each flow.

### `OpenIdpAuthPopup(url)`

Use this when auth is handled by your backend.

- input: backend-generated auth URL
- result: auth ticket string
- follow-up: your backend exchanges the ticket for the real session token

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

- `orderId` must already exist in your backend/business system.
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

- `HApps.Provider` is public, but direct provider access should be treated as internal unless you intentionally depend on SDK internals.
- `IsPortalSite()` depends on the JS contract `window.HApps.isPortal`.
- `MakePayment()` accepts `orderId`, not `PaymentItem`.
- `Initialize()` and `GetProfile()` may fail if the JS bridge is not correctly wired in the WebGL template.

## Security Requirements

- Always validate `event.origin` in JS.
- Never use `"*"` in `postMessage`.
- Use PKCE for OIDC.
- Validate `state` and `nonce`.
- Use short Redis TTL.
- Tickets must be single-use.
- Never trust client payment result.
- Always verify payment server-side.

## Version

HApps Web SDK - Integration Guide v2.1
