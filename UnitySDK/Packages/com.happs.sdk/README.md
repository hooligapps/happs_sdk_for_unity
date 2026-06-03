# HApps Unity SDK

Unity package for HApps WebGL integrations.

## Installation

Add the package to your Unity project through `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v2.0.5"
  }
}
```

Use a release tag such as `v2.0.5`. During development you can temporarily point to a commit hash instead of a tag.

## Runtime API

```csharp
Task<bool> HApps.Web.Connect()
Task<UserData> HApps.Web.GetProfile()
Task<PaymentData> HApps.Web.MakePayment(string orderId)
Task<AuthPopupData> HApps.Web.OpenIdpAuthPopup(string url)
Task<bool> HApps.Web.OpenPortalAuthPopup()
event Action<UserData, SignatureData> HApps.Web.AuthCompleted
bool HApps.Web.IsPortalSite()
bool HApps.Web.IsReady()

Task<UserData> HApps.Mobile.GetProfile()
Task<PaymentData> HApps.Mobile.MakePayment(string orderId)
Task<MobileLoginResult> HApps.Mobile.LoginAsync()
Task HApps.Mobile.LogoutAsync()
Task HApps.Mobile.RefreshSessionAsync()

void HApps.Shutdown()
```

Legacy flat methods such as `HApps.Connect()` are still available as Web compatibility shortcuts.

## WebGL Bridge Requirements

Your WebGL page must:

- load `https://hooli.games/public/js/sdk/hooligapps.js` or `https://hooli.games/public/js/sdk/hooligapps.debug.js`
- initialize the browser bridge with `HApps.init(...)`
- use `unityObjectName: "HAppsJSBridge"`
- use `unityMethodName: "OnMessage"`

## Integration Modes

Standalone flow:

- use `OpenIdpAuthPopup(url)`
- inspect returned `AuthPopupData`
- popup auth supports two success modes:
- `ticket`: exchange `ticket` on your backend
- `cookie`: auth is already completed through cookie session

Embedded portal flow:

- call `HApps.Web.Connect()` to receive platform context and store portal signature in `HApps.Provider.Signature`
- use that signature in your backend auth flow to resolve the user/session
- call `HApps.Web.OpenPortalAuthPopup()` when you need to show portal login UI from the game
- subscribe to `HApps.Web.AuthCompleted` if you need to react to external `auth_complete` messages without awaiting `HApps.Web.OpenPortalAuthPopup()`
- after portal auth, your backend can use the refreshed signature if that flow needs server-side auth resolution
- call `HApps.Web.GetProfile()` when needed

Mobile flow:

- use `HApps.Mobile` for native/mobile auth-specific work
- the mobile provider contract is included in the package
- the actual native OIDC implementation is not part of this package yet

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

## Notes

- `IsPortalSite()` depends on `window.HApps.isPortal()`
- `IsReady()` depends on `window.HApps.isReady()`
- `HApps.Web.AuthCompleted` fires on incoming `auth_complete` messages from the page script
- `MakePayment()` accepts backend-created `orderId`
- `OpenIdpAuthPopup(url)` returns `AuthPopupData`, not plain `string`
- `AuthPopupData` supports both ticket-based and cookie-based session auth
- `Connect()` and `OpenPortalAuthPopup()` are separate steps in embedded portal auth
- `Connect()` stores the current portal signature in `HApps.Provider.Signature`
- sample scene/scripts remain in the host project, not in the package
