# HApps Unity SDK

Unity SDK 3.1.2 for HApps WebGL integrations through JS SDK 1.1.0 and native Android integrations.

## Installation

Add the package to your Unity project through `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.1.2"
  }
}
```

Use a release tag such as `v3.1.2`. During development you can temporarily point to a commit hash instead of a tag.

For an existing WebGL project, follow [WebGL Migration: SDK 2.0.6 to 3.1.1](MIGRATION_WEB_2.0.6_TO_3.1.1.md).

For native Android integration, follow [Mobile Integration](MOBILE_INTEGRATION.md).

## Runtime API

```csharp
Task<bool> HApps.Web.Connect()
Task<UserData> HApps.Web.GetProfile()
Task<PaymentData> HApps.Web.MakePayment(string orderId)
Task<AuthPopupData> HApps.Web.OpenIdpAuthPopup(string url)
Task<bool> HApps.Web.OpenPortalAuthPopup()
void HApps.Web.OpenAgeVerification(bool adultMode = true)
void HApps.Web.SetFullscreen(bool enabled)
void HApps.Web.SetTheaterMode(bool enabled)
void HApps.Web.OpenExternalUrl(string url)
event Action<UserData, SignatureData> HApps.Web.AuthCompleted
event Action<UserData> HApps.Web.UserChanged
event Action<HAppsErrorData> HApps.Web.Error
bool HApps.Web.IsPortalSite()
bool HApps.Web.IsReady()

Task<MobileSession> HApps.Mobile.InitSessionAsync()
Task<MobileLoginResult> HApps.Mobile.LoginAsync()
Task<MobileSession> HApps.Mobile.RefreshSessionAsync()
Task<MobileCreatePaymentResult> HApps.Mobile.CreatePaymentAsync(MobileCreatePaymentRequest request)
Task<MobileCheckUpdateResult> HApps.Mobile.CheckForUpdateAsync(int versionCode)
Task HApps.Mobile.LogoutAsync()

void HApps.ConfigureMobile(HAppsMobileAuthOptions options, IMobileTokenStore tokenStore = null)
void HApps.SetDebugLogging(bool enabled)
void HApps.Shutdown()
```

## WebGL Bridge Requirements

Your WebGL page must:

- load `https://cdn.hooli.games/sdk/1.1.0/hooligapps.js`
- use the JS SDK `1.1.0` contract; unversioned builds are not supported by Unity SDK `3.1.2`
- initialize the core client with `HApps.init(...)`
- attach Unity with `HApps.unity.attach(...)`
- use `objectName: "HAppsJSBridge"`
- use `methodName: "OnMessage"`
- set `isPortal: false` for standalone pages
- set `isPortal: true` and provide `ssoLoginUrl` for embedded portal pages

Set `debug: true` in `HApps.init(...)` when browser-side logging is required. In standalone mode, the `ready` promise resolves immediately with `user: null`; authentication is performed through `OpenIdpAuthPopup(url)`.

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
- call `HApps.Web.SetFullscreen(enabled)` to switch the portal fullscreen layout
- call `HApps.Web.OpenExternalUrl(url)` to ask the portal to open an external URL
- subscribe to `HApps.Web.AuthCompleted` if auth can complete outside the awaited popup flow
- subscribe to `HApps.Web.UserChanged` for profile changes and `HApps.Web.Error` for browser SDK errors

If the connected profile is already verified, `OpenPortalAuthPopup()` returns `true` locally without opening a popup or emitting a new `AuthCompleted` event.

`SetTheaterMode(bool)` is dispatched by JS SDK 1.1.0.
`SetFullscreen(bool)` is dispatched by JS SDK 1.1.0.

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
- `OpenIdpAuthPopup(url)` returns `AuthPopupData`, not plain `string`
- `AuthPopupData` supports both ticket-based and cookie-based session auth
- `Connect()` and `OpenPortalAuthPopup()` are separate steps
- `OpenAgeVerification()` is a fire-and-forget bridge call with no completion callback
- `UserChanged` updates `CurrentUser` before invoking subscribers
- `Error` is not correlated with a specific pending operation
- `HApps.init(...)` and `HApps.unity.attach(...)` are separate browser-side setup steps
- debug logging is disabled by default; `SetDebugLogging(true)` enables sanitized debug/warn logs, while errors always log
- SDK logs never include tokens, authorization codes, signatures, deep-link query strings, or auth request/response bodies
- `MakePayment()` accepts a backend-created `orderId`
- a second `MakePayment()` call throws `InvalidOperationException` while the first payment is still active; it does not replace the first operation
- payment fulfillment must be checked through the game backend; the Unity SDK does not validate postbacks or poll payment delivery
- mobile `GetProfile()` and mobile `MakePayment(orderId)` are not part of the current native flow
- sample scene/scripts remain in the host project, not in the package
