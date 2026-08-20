# WebGL Migration: Unity SDK 2.0.6 to 3.0.1

This guide covers WebGL integrations only. Native Android APIs added in 3.x are not required for an existing web game.

The supported combination after migration is:

- HApps Unity SDK `3.0.1`
- HApps browser JS SDK `1.0.3`

Do not combine Unity SDK 3.0.1 with an unversioned browser script or a different JS SDK contract.

## 1. Update The Unity Package

Change the package tag in `Packages/manifest.json`:

```diff
{
  "dependencies": {
-    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v2.0.6"
+    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.0.1"
  }
}
```

Allow Unity to resolve and recompile the package before changing game code. The namespace remains `HAppsSDK`.

## 2. Move Web Calls Under `HApps.Web`

Version 3.0.1 removes the flat web shortcuts from the static `HApps` facade. All web operations, web state, and the web auth event now belong to `HApps.Web`.

| SDK 2.0.6 | SDK 3.0.1 |
| --- | --- |
| `HApps.Connect()` | `HApps.Web.Connect()` |
| `HApps.GetProfile()` | `HApps.Web.GetProfile()` |
| `HApps.MakePayment(orderId)` | `HApps.Web.MakePayment(orderId)` |
| `HApps.OpenIdpAuthPopup(url)` | `HApps.Web.OpenIdpAuthPopup(url)` |
| `HApps.OpenPortalAuthPopup()` | `HApps.Web.OpenPortalAuthPopup()` |
| `HApps.OpenAgeVerification(adultMode)` | `HApps.Web.OpenAgeVerification(adultMode)` |
| `HApps.SetTheaterMode(enabled)` | `HApps.Web.SetTheaterMode(enabled)` |
| `HApps.IsPortalSite()` | `HApps.Web.IsPortalSite()` |
| `HApps.IsReady()` | `HApps.Web.IsReady()` |
| `HApps.AuthCompleted` | `HApps.Web.AuthCompleted` |
| `HApps.Provider.Signature` | `HApps.Web.Signature` |
| `HApps.Provider.IsInitialized` | `HApps.Web.IsInitialized` |
| `HApps.Provider.IsLoggedIn` | `HApps.Web.IsLoggedIn` |
| `HApps.Provider.CurrentUser` | `HApps.Web.CurrentUser` |

`HApps.Provider` no longer exists. Replace every direct reference to it, including cached variables and diagnostic checks.

These calls remain unchanged:

```csharp
HApps.SetDebugLogging(true);
HApps.SetDebugLogging(false);
HApps.Shutdown();
```

`SetDebugLogging` controls Unity SDK debug and warning messages. Errors are still logged when debug logging is disabled. It does not control logging inside the browser JS SDK.

### Before: SDK 2.0.6

```csharp
using HAppsSDK;

var connected = await HApps.Connect();
if (!connected)
    return;

var signature = HApps.Provider.Signature;
var profile = await HApps.GetProfile();
var payment = await HApps.MakePayment(orderId);
```

### After: SDK 3.0.1

```csharp
using HAppsSDK;

var connected = await HApps.Web.Connect();
if (!connected)
    return;

var signature = HApps.Web.Signature;
var profile = await HApps.Web.GetProfile();
var payment = await HApps.Web.MakePayment(orderId);
```

## 3. Move The Auth Event Subscription

Update both subscription and unsubscription:

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
    Debug.Log($"auth_complete: {user?.userId}");
}
```

The handler signature has not changed. Always unsubscribe the same handler to avoid retaining scene objects.

## 4. Pin And Configure JS SDK 1.0.3

Version 2.0.6 documentation used unversioned script URLs. Replace them with an explicit 1.0.3 URL.

Development:

```html
<script src="https://hooli.games/public/js/sdk/1.0.3/hooligapps.debug.js"></script>
```

Production:

```html
<script src="https://hooli.games/public/js/sdk/1.0.3/hooligapps.js"></script>
```

`debug: true` is not a valid `HApps.init(...)` option in JS SDK 1.0.3. Choose the debug script when browser-side logging is required.

Keep these Unity bridge names exactly as shown:

```javascript
unityObjectName: "HAppsJSBridge",
unityMethodName: "OnMessage"
```

### Standalone WebGL

Set `isPortal: false`. A standalone page does not need `ssoLoginUrl` for the IDP popup flow.

```javascript
HApps.init({
    platformOrigin: "https://hooli.games",
    isPortal: false,
    unityObjectName: "HAppsJSBridge",
    unityMethodName: "OnMessage",
    gameInstance: unityInstance
});
```

Authenticate from Unity with:

```csharp
var result = await HApps.Web.OpenIdpAuthPopup(authUrl);

switch (result.Flow)
{
    case AuthPopupFlow.Ticket:
        // Exchange result.ticket on your backend.
        break;
    case AuthPopupFlow.Cookie:
        // The cookie session is already established.
        break;
    case AuthPopupFlow.Cancelled:
        // The popup did not complete.
        break;
}
```

In standalone mode, the JS `ready` promise resolves immediately with `user: null`. It is not proof that the user is authenticated.

### Embedded Portal WebGL

Set `isPortal: true` and provide the backend SSO endpoint:

```javascript
HApps.init({
    platformOrigin: "https://hooli.games",
    ssoLoginUrl: "https://your-backend.example/api/sign",
    isPortal: true,
    unityObjectName: "HAppsJSBridge",
    unityMethodName: "OnMessage",
    gameInstance: unityInstance
});
```

With deployed JS SDK 1.0.3, the embedded `HApps.init(...).ready` promise does not resolve after the initial successful portal login. Do not block game startup on it. Use Unity connection as the readiness gate:

```csharp
var connected = await HApps.Web.Connect();
if (!connected)
{
    // Show an integration error or retry from your game flow.
    return;
}
```

The configured `/api/sign` endpoint receives:

```json
{
  "token": "platform-launch-signature"
}
```

It must return:

```json
{
  "signature": "backend-session-signature"
}
```

The browser obtains user data from the portal launch message. Do not return the old documented `{ userData, signatureData }` shape from `/api/sign`.

## 5. Account For Current Runtime Behavior

### Timeouts And Exceptions

- `Connect()` and `GetProfile()` time out after 30 seconds.
- payment, IDP popup, and portal auth operations time out after 180 seconds.
- profile errors from JS SDK 1.0.3 surface as `HAppsException` instead of leaving `GetProfile()` pending.
- an immediate payment rejection now completes with its returned `PaymentData` instead of waiting indefinitely.
- calling `MakePayment()` while another payment is active throws `InvalidOperationException`; the original payment remains active.
- calls through a provider reference retained after `HApps.Shutdown()` throw `ObjectDisposedException`. Access `HApps.Web` again only when intentionally starting a new SDK lifecycle.

Wrap awaited operations according to the error handling policy of the game:

```csharp
try
{
    var profile = await HApps.Web.GetProfile();
}
catch (HAppsException exception)
{
    Debug.LogError($"Profile request failed: {exception.Message}");
}
catch (System.TimeoutException)
{
    Debug.LogError("Profile request timed out.");
}
```

### Portal Auth For An Already Verified User

After a successful `Connect()`, `OpenPortalAuthPopup()` returns `true` immediately when `HApps.Web.CurrentUser.verified` is already `true`. In that path no popup opens and no new `AuthCompleted` event is emitted.

Do not wait for the event after awaiting the method:

```csharp
var authenticated = await HApps.Web.OpenPortalAuthPopup();
if (authenticated)
{
    var signature = HApps.Web.Signature;
    // Continue the backend session flow.
}
```

Keep the event subscription only when the game must also react to external `auth_complete` messages.

### Theater Mode Limitation

`HApps.Web.SetTheaterMode(bool)` remains in the Unity API for compatibility, but browser JS SDK 1.0.3 does not dispatch its `set_theater_mode` event. The call has no effect with the supported contract. Remove any logic that relies on it succeeding.

## 6. Migration Checklist

- package URL points to `#v3.0.1`
- browser script URL contains `/sdk/1.0.3/`
- all web calls use `HApps.Web.*`
- no code references `HApps.Provider`
- auth subscriptions use `HApps.Web.AuthCompleted`
- standalone initialization sets `isPortal: false`
- embedded initialization sets `isPortal: true` and provides `ssoLoginUrl`
- `unityObjectName` is `HAppsJSBridge`
- `unityMethodName` is `OnMessage`
- no `debug` property is passed to `HApps.init(...)`
- embedded startup waits for `HApps.Web.Connect()`, not the JS `ready` promise
- `/api/sign` accepts `{ token }` and returns `{ signature }`
- payment and popup calls handle timeout and exception paths
- game logic does not depend on `SetTheaterMode()` with JS SDK 1.0.3

## 7. WebGL Smoke Test

After Unity recompiles, create a development WebGL build with `hooligapps.debug.js` and verify:

1. The page creates exactly one `HAppsJSBridge` Unity object.
2. Embedded mode completes `HApps.Web.Connect()` and exposes a non-empty signature when the platform session is valid.
3. `GetProfile()` returns the expected `userId`, `userName`, and `verified` values.
4. Standalone popup auth handles `ticket`, `cookie`, and cancellation results.
5. Portal auth works for both unverified and already verified profiles.
6. A rejected payment completes without hanging, and starting a second concurrent payment is handled by the game.
7. `HApps.SetDebugLogging(false)` suppresses Unity SDK debug/warning output while errors remain visible.
8. `HApps.Shutdown()` is called when the SDK lifecycle ends and no disposed provider reference is reused.
