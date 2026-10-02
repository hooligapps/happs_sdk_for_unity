# HApps Mobile Integration

This guide describes the native Android flow. Projects on `v3.2.0` should start with [Updating from HApps SDK 3.2.0](MIGRATION_FROM_3.2.0.md). Older projects should first read [Mobile Migration: SDK 3.1.2 to 3.2.0](MIGRATION_MOBILE_3.1.2_TO_3.2.0.md). Optional AppsFlyer attribution is implemented by the [separate integration package](../../../Integrations/com.happs.sdk.appsflyer/README.md).

Changes to configuration, cancellation, session events and exceptions are documented in [Updating from HApps SDK 3.2.0](MIGRATION_FROM_3.2.0.md).

## 1. Requirements and environments

- HApps Unity SDK `3.4.0`.
- Android API 23 or newer.
- iOS is not supported.

| Environment | `PortalUrl` |
| --- | --- |
| DEV | `https://portal.example.com` |
| PROD | `https://hooli.games` |

Use DEV for integration testing and PROD for production builds. Client configuration, releases, players, devices, and tokens are environment-specific.

Confirm these values with HApps before integration:

| Value | Example |
| --- | --- |
| Mobile Client ID | `my-game-mobile` |
| Callback URI | `https://links.example.com/games/sample-game/callback` |
| Android package ID | `com.example.game` |
| Release signing certificate SHA-256 | `AA:BB:CC:...` |
| Game API secret | backend only |

For payments, provide HApps with the game backend validation and postback URLs. For updates, HApps must configure `minSupportedVersionCode` and publish at least one release.

## 2. Install the SDK

Add the package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.4.0",
    "com.google.external-dependency-manager": "https://github.com/googlesamples/unity-jar-resolver.git?path=upm#v1.2.188"
  }
}
```

Run `Assets > External Dependency Manager > Android Resolver > Force Resolve` after installing or updating the package.

The core package resolves AndroidX Browser 1.8.0 and supports the default Unity 2022.3 Android toolchain. It opens authentication, logout and payment in Custom Tabs. Unity 6 projects with Android Gradle Plugin 8.1 or newer can add Auth Tab payment returns with the optional package:

```json
"com.happs.sdk.browser-auth-tab": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/Integrations/com.happs.sdk.browser-auth-tab#v3.4.0"
```

The optional package upgrades AndroidX Browser to 1.9.0. Re-run Force Resolve after adding or removing it. Do not install it in a Unity 2022.3 project that uses the default Gradle 7.2 and Android Gradle Plugin 7.1.2 toolchain.

Add the separate AppsFlyer package only when the game needs install attribution. Its installation and initialization are documented in [HApps AppsFlyer integration](../../../Integrations/com.happs.sdk.appsflyer/README.md).

## 3. Configure Android App Links

Ask HApps for the callback domain and game slug assigned in each environment. The callback domain may be the same as `PortalUrl` or a separate domain. Browser return behavior is handled by the SDK Custom Tab and Android App Links; it does not require a separate host.

Send HApps the Android package ID and SHA-256 fingerprint of the certificate that signs the APK. HApps publishes the association at `https://<callback-domain>/.well-known/assetlinks.json`.

DEV and PROD can use different callback domains and associations. Configure a DEV build with its DEV callback domain and debug or DEV signing certificate, and configure a production build with its PROD callback domain and release signing certificate. Each game uses its own `/games/<game-slug>/` path.

Add the `queries` block directly under the root `manifest` element so the SDK can discover Chrome Custom Tabs on Android 11 and newer. Merge the verified HTTPS filter into the application's launcher activity, replacing the example host with the assigned host:

```xml
<queries>
    <intent>
        <action android:name="android.support.customtabs.action.CustomTabsService" />
    </intent>
    <package android:name="com.android.chrome" />
    <package android:name="com.chrome.beta" />
    <package android:name="com.chrome.dev" />
    <package android:name="com.chrome.canary" />
</queries>

<activity
    android:name="com.unity3d.player.UnityPlayerActivity"
    android:exported="true"
    android:launchMode="singleTask">

    <intent-filter android:autoVerify="true">
        <action android:name="android.intent.action.VIEW" />
        <category android:name="android.intent.category.DEFAULT" />
        <category android:name="android.intent.category.BROWSABLE" />

        <data
            android:scheme="https"
            android:host="links.example.com"
            android:path="/games/sample-game/callback" />
    </intent-filter>
</activity>
```

This filter accepts:

```text
https://links.example.com/games/sample-game/callback
```

The association served by HApps has this shape:

```json
[
  {
    "relation": ["delegate_permission/common.handle_all_urls"],
    "target": {
      "namespace": "android_app",
      "package_name": "com.example.game",
      "sha256_cert_fingerprints": ["AA:BB:CC:..."]
    }
  }
]
```

The file must be available over HTTPS without authentication or redirects. The package ID and fingerprint must match the installed APK. The same callback URI is used for authentication, logout and payment. The server identifies the operation with `type=login`, `type=logout`, or `type=payment`. Authentication also adds `code` and `state`, or `error` and `state`; logout adds `state` and `status`; payment adds `orderId` and may add `status`.

Authentication and logout return through the verified App Link. When the optional Auth Tab package is installed, a supported browser watches the payment callback host and path. Reaching that callback closes the Auth Tab and returns control to the game before the callback page needs to load. Without the optional package, or when the browser does not support Auth Tab, payment uses a Custom Tab and the callback page must provide its normal fallback return. A payment callback only returns the user to the app; it does not confirm payment.

After installing the APK, verify the association on Android 12 or newer:

```bash
adb shell pm verify-app-links --re-verify com.example.game
adb shell pm get-app-links com.example.game
```

The callback domain should report `verified`. If the same APK intentionally supports both DEV and PROD, declare each domain in a separate intent filter and register the package and corresponding signing certificate on both domains.

Test the link by opening it from another application or by resolving it through ADB. Typing or pasting the URL into a browser address bar explicitly asks the browser to open the page and may not launch the application. The SDK opens interactive mobile URLs in an Android Custom Tab and allows verified redirects to leave the browser and return to the game.

## 4. Configure the SDK

Call once before using `HApps.Mobile`:

```csharp
using HAppsSDK;

HApps.ConfigureMobile(new HAppsMobileAuthOptions
{
    PortalUrl = "https://portal.example.com",
    ClientId = "my-game-mobile",
    CallbackUri = "https://links.example.com/games/sample-game/callback"
});
```

The SDK sends this exact URI for authentication and logout:

```text
https://links.example.com/games/sample-game/callback
```

HApps uses the same URI for the payment return.

`CallbackUri` is independent from `PortalUrl`: API and OIDC requests use `PortalUrl`. Android uses the verified callback association for authentication and logout App Links and for the payment Auth Tab redirect. The callback URI must exactly match the URI registered for the mobile client.

The SDK derives all OIDC and Mobile API paths from `PortalUrl`. Its default credential storage is isolated by `PortalUrl` and `ClientId`.

The default Android storage uses Android Keystore. Do not use `PlayerPrefsMobileTokenStore` in production because it stores credentials as plaintext.

## 5. Check for an update

Call before session initialization:

```csharp
MobileCheckUpdateResult update =
    await HApps.Mobile.CheckForUpdateAsync(currentVersionCode);
```

| Field | Meaning |
| --- | --- |
| `UpdateAvailable` | a newer published `versionCode` exists |
| `Required` | the current version is below `minSupportedVersionCode` |
| `LatestVersionCode` | latest published Android version code |
| `LatestVersionName` | latest display version |
| `DownloadUrl` | APK URL |
| `Sha256` | APK SHA-256 or `null` |
| `ReleaseNotes` | release notes or `null` |

The application decides how to handle the result:

```csharp
if (update.Required)
{
    ShowRequiredUpdate(update);
    return;
}

if (update.UpdateAvailable)
    ShowOptionalUpdate(update);
```

The SDK does not download, verify, or install the APK. A `404 mobile_resource_not_found` usually means that the native client or its published release is missing in the selected environment.

## 6. Initialize the player

```csharp
MobileSession session = await HApps.Mobile.InitSessionAsync();

string publicId = session.PublicId;
bool signedIn = session.IsAuthorized;
```

| Field | Meaning |
| --- | --- |
| `PublicId` | current HApps player ID |
| `IsAuthorized` | the player is signed in to a HApps account |
| `Verified` | separate server-side verification state |
| `AccessToken` | short-lived HApps mobile token |
| `AccessTokenExpiresAtUtc` | token expiry in Unix seconds |
| `DeviceId` | installation ID, not a player ID |
| `AnalyticProvider` | SDK 3.2.0: optional analytics provider name, such as `appsflyer` |
| `AnalyticKey` | SDK 3.2.0: optional provider SDK key; never log this value |

Use `IsAuthorized`, not `Verified`, to determine login state. Load the profile and progress from the game backend using `PublicId`.

Player resolution after login:

- If the account has no player for this game, the anonymous player is attached to the account and keeps its `PublicId`.
- If the account already has a player, the device switches to that player's `PublicId` and progress.
- Progress is not merged; the existing account player takes precedence.

Reload game data whenever `PublicId` changes. Clearing application data or reinstalling creates a new installation and anonymous player until the user signs in again.

These inherited APIs are not supported on mobile:

```csharp
HApps.Mobile.GetProfile();
HApps.Mobile.MakePayment(orderId);
```

## 7. Login

```csharp
MobileLoginResult login = await HApps.Mobile.LoginAsync();

if (!login.IsSuccess)
{
    ShowLoginError(login.Error);
    return;
}

string publicId = login.PublicId;
```

- Only one `LoginAsync()` may run at a time; a second call throws `InvalidOperationException`.
- The operation throws `TimeoutException` if the entire login attempt does not complete within 180 seconds.
- If Android terminates the game while the browser is open, the login cannot be resumed. Start login again after relaunch.
- Reload the player from the game backend after successful login.

## 8. Renew the session

```csharp
MobileSession session = await HApps.Mobile.RefreshSessionAsync();
```

There is no refresh token. `InitSessionAsync()` and `RefreshSessionAsync()` renew the session through `session/init`; concurrent calls share one operation.

For payment creation, the SDK automatically renews the session and retries once after `401 invalid_mobile_session` or `401 mobile_session_expired`.

## 9. Logout

```csharp
await HApps.Mobile.LogoutAsync();
MobileSession anonymousSession = await HApps.Mobile.InitSessionAsync();
```

Logout opens the browser confirmation and waits for the server callback with the matching `state`. On `status=success`, the SDK clears local credentials and device keys. On `status=cancelled`, it preserves the current mobile session and completes the task with `OperationCanceledException`. A failed or timed-out logout does not clear local state.

The SDK uses the shared `CallbackUri` with the following logout confirmation URLs:

```text
/games/:slug/app/callback?state=...&type=logout&status=success
/games/:slug/app/callback?state=...&type=logout&status=cancelled
```

The SDK sends the base `CallbackUri` as `postLogoutRedirectUri`. The server adds the logout state, operation type and result to the final callback URL. Both results use the same Android App Link path; query parameters do not need separate Android manifest entries.

## 10. Create a payment

```csharp
MobileCreatePaymentResult payment =
    await HApps.Mobile.CreatePaymentAsync(new MobileCreatePaymentRequest
    {
        RequestId = purchaseRequestId,
        ProductId = "coins_pack_1",
        Price = 1.99m,
        Currency = "USD",
        Description = "Coins Pack 1"
    });
```

| Field | Constraint |
| --- | --- |
| `RequestId` | required, maximum 128 characters |
| `ProductId` | required, maximum 128 characters |
| `Price` | `0.01`–`99999999.99` |
| `Currency` | required, maximum 10 characters |
| `Description` | required, maximum 500 characters |

`RequestId` is the game idempotency key:

- reuse it when retrying the same unfinished purchase;
- use a new value for a new purchase;
- never reuse it for another player or product.

The SDK creates the order and opens `PaymentUrl`. With the optional Auth Tab package installed, it configures the browser with the host and path from `CallbackUri`. Reaching that callback closes the browser surface and resumes the game; query parameters are allowed and do not require additional manifest filters. Without the optional package, or on an unsupported browser, it uses a Custom Tab fallback.

`OrderId` confirms order creation only; it does not confirm payment. Returning to the game also does not prove that payment succeeded.

The SDK does not wait for `payment_complete`. Grant the product only after the game backend verifies the HApps postback. Prevent multiple checkout flows from being opened simultaneously.

## 11. Game backend payment endpoints

The Game API secret is backend-only. Verify every HApps request using `X-Game-Id`, `X-Timestamp`, and `X-Signature`:

```ts
import { HAppsClient } from "happs-nodejs";

HAppsClient.verifyHAppsRequest({
  secret: process.env.HOOLI_CLIENT_SECRET,
  method: req.method,
  path: req.path,
  timestamp: req.headers["x-timestamp"],
  signature: req.headers["x-signature"],
  body: req.body,
  clientId: req.headers["x-game-id"],
  expectedClientId: process.env.HOOLI_CLIENT_ID,
});
```

### 11.1 Payment validation

HApps sends:

```json
{
  "publicId": "player-public-id",
  "requestId": "purchase-request-id",
  "productId": "coins_pack_1",
  "price": 1.99,
  "currency": "USD",
  "desc": "Coins Pack 1"
}
```

Parse the request and return product data from the backend catalog:

```ts
const payment = HAppsClient.parsePaymentValidationRequest(req.body);
const product = await catalog.get(payment.productId);

res.json({
  ok: true,
  product: {
    productId: product.id,
    price: product.price,
    currency: product.currency,
    desc: product.description,
    name: product.name
  }
});
```

Do not trust client-provided price, currency, or description. To reject the request, return `{ "ok": false, "error": "reason" }`. The validation timeout is 10 seconds.

### 11.2 Payment postback

After approval, HApps sends:

```json
{
  "publicId": "player-public-id",
  "orderId": "happs-order-id",
  "purchaseId": "happs-order-id",
  "requestId": "purchase-request-id",
  "status": "approved",
  "transactionId": "provider-transaction-id",
  "price": "1.99",
  "currency": "USD",
  "isTest": false
}
```

`purchaseId` currently equals `orderId`.

```ts
const postback = HAppsClient.parsePaymentPostback(req.body);

await purchases.grantOnce({
  requestId: postback.requestId,
  orderId: postback.orderId,
  publicId: postback.publicId,
  price: postback.price,
  currency: postback.currency,
  transactionId: postback.transactionId
});

res.json({ ok: true });
```

Before granting the product, match `requestId`, `orderId`, `publicId`, `price`, and `currency` against the stored purchase and require `status == "approved"`. Processing must be idempotent.

## 12. Errors and logging

| HTTP | Action |
| --- | --- |
| `400` | fix the request or registered redirect URI; do not retry unchanged |
| `401` | reinitialize the session; payment creation already retries once |
| `403` | verify that the game and native client are active |
| `404` | verify the environment, endpoint, client, and published release |
| `409` | check device state or `RequestId` reuse |
| `429` | use bounded exponential backoff |
| `5xx` | retry later |

Enable or disable sanitized SDK debug logs:

```csharp
HApps.SetDebugLogging(true);
HApps.SetDebugLogging(false);
```

Errors are always logged. Debug mode logs HTTP methods, URLs, sanitized request and response data, and status codes. Tokens, authorization codes, signatures, Dev Keys, sensitive redirect URLs, and URL query strings are replaced or omitted.
