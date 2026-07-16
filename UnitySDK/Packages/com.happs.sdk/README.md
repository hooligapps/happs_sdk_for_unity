# HApps Unity SDK

Unity package for HApps WebGL and mobile integrations.

## Installation

Add the package to your Unity project through `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.0.0"
  }
}
```

Use a release tag such as `v3.0.0`. During development you can temporarily point to a commit hash instead of a tag.

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

Task<MobileSession> HApps.Mobile.InitSessionAsync()
Task<MobileLoginResult> HApps.Mobile.LoginAsync()
Task<MobileSession> HApps.Mobile.RefreshSessionAsync()
Task<MobileCreatePaymentResult> HApps.Mobile.CreatePaymentAsync(MobileCreatePaymentRequest request)
Task HApps.Mobile.LogoutAsync()

void HApps.ConfigureMobile(HAppsMobileAuthOptions options, IMobileTokenStore tokenStore = null)
void HApps.Shutdown()
```

## WebGL Bridge Requirements

Your WebGL page must:

- load `https://hooli.games/public/js/sdk/hooligapps.js` or `https://hooli.games/public/js/sdk/hooligapps.debug.js`
- initialize the browser bridge with `HApps.init(...)`
- use `unityObjectName: "HAppsJSBridge"`
- use `unityMethodName: "OnMessage"`

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
- subscribe to `HApps.Web.AuthCompleted` if auth can complete outside the awaited popup flow

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
    Scope = "openid email offline_access",
    InitSessionUrl = "https://portal.igra.rocks/api/v1/mobile/session/init",
    RefreshSessionUrl = "https://portal.igra.rocks/api/v1/mobile/session/refresh",
    CreatePaymentUrl = "https://portal.igra.rocks/api/v1/mobile/payments"
});
```

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

- `InitSessionAsync()` returns portal `publicId`, portal access token, refresh token, and `verified`
- `LoginAsync()` starts OIDC login in the system browser and stores returned OIDC tokens in the configured token store
- `BuildAuthorizeUrl` includes `linkId=publicId` when a portal session already exists
- `CreatePaymentAsync()` sends the current portal access token in `Authorization: Bearer ...`
- on `401 invalid_mobile_session` the SDK tries `RefreshSessionAsync()`
- on `401 invalid_mobile_refresh` the SDK falls back to `InitSessionAsync()`
- `LogoutAsync()` clears local mobile tokens and immediately requests a fresh anonymous portal session
- `CreatePaymentAsync()` opens the returned `paymentUrl`, but does not verify final payment status

## Notes

- `IsPortalSite()` depends on `window.HApps.isPortal()`
- `IsReady()` depends on `window.HApps.isReady()`
- `OpenIdpAuthPopup(url)` returns `AuthPopupData`, not plain `string`
- `Connect()` and `OpenPortalAuthPopup()` are separate steps
- `MakePayment()` accepts a backend-created `orderId`
- mobile `GetProfile()` and mobile `MakePayment(orderId)` are not part of the current native flow
- sample scene/scripts remain in the host project, not in the package
