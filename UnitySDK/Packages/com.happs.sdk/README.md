# HApps Unity SDK

Unity package for HApps WebGL integrations.

## Installation

Add the package to your Unity project through `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v1.0.0"
  }
}
```

Use a release tag such as `v1.0.0`. During development you can temporarily point to a commit hash instead of a tag.

## Runtime API

```csharp
Task<bool> HApps.Initialize()
Task<UserData> HApps.GetProfile()
Task<PaymentData> HApps.MakePayment(string orderId)
Task<AuthPopupData> HApps.OpenIdpAuthPopup(string url)
Task<bool> HApps.OpenPortalAuthPopup()
bool HApps.IsPortalSite()
void HApps.Shutdown()
```

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

- call `Initialize()`
- call `OpenPortalAuthPopup()`
- call `GetProfile()` when needed

## Notes

- `IsPortalSite()` depends on `window.HApps.isPortal()`
- `MakePayment()` accepts backend-created `orderId`
- `OpenIdpAuthPopup(url)` returns `AuthPopupData`, not plain `string`
- `AuthPopupData` supports both ticket-based and cookie-based session auth
- sample scene/scripts remain in the host project, not in the package
