# HApps Mobile Integration

For optional AppsFlyer attribution in SDK 3.2.0, see [Attribution contract](ATTRIBUTION.md) and the [separate integration package](../../../Integrations/com.happs.sdk.appsflyer/README.md). Deploy backend support before enabling the adapter. The existing SDK 3.1.2 flow below does not require AppsFlyer.

## 1. Requirements and environments

- HApps Unity SDK `3.1.2`.
- Android API 23 or newer.
- iOS is not supported.

| Environment | `PortalUrl` |
| --- | --- |
| DEV | `https://portal.igra.rocks` |
| PROD | `https://hooli.games` |

Use DEV for integration testing and PROD for production builds. Client configuration, releases, players, devices, and tokens are environment-specific.

Confirm these values with HApps before integration:

| Value | Example |
| --- | --- |
| Mobile Client ID | `my-game-mobile` |
| Redirect URI | `com.example.game://auth/callback` |
| Post Logout Redirect URI | `com.example.game://logout` |
| Payment Redirect URI | `com.example.game://payment/callback` |
| Game API secret | backend only |

For payments, provide HApps with the game backend validation and postback URLs. For updates, HApps must configure `minSupportedVersionCode` and publish at least one release.

## 2. Install the SDK

Add the package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.1.2"
  }
}
```

Use a release tag, not `main`.

## 3. Configure Android deep links

Merge this filter into the application's launcher activity:

```xml
<activity
    android:name="com.unity3d.player.UnityPlayerActivity"
    android:exported="true"
    android:launchMode="singleTask">

    <intent-filter>
        <action android:name="android.intent.action.VIEW" />
        <category android:name="android.intent.category.DEFAULT" />
        <category android:name="android.intent.category.BROWSABLE" />

        <data android:scheme="com.example.game" android:host="auth" />
        <data android:scheme="com.example.game" android:host="logout" />
        <data android:scheme="com.example.game" android:host="payment" />
    </intent-filter>
</activity>
```

This filter accepts:

```text
com.example.game://auth/callback
com.example.game://logout
com.example.game://payment/callback
```

The URIs registered with HApps must match exactly. The payment callback only returns the user to the app; it does not confirm payment.

## 4. Configure the SDK

Call once before using `HApps.Mobile`:

```csharp
using HAppsSDK;

HApps.ConfigureMobile(new HAppsMobileAuthOptions
{
    PortalUrl = "https://portal.igra.rocks",
    ClientId = "my-game-mobile",
    RedirectUri = "com.example.game://auth/callback",
    PostLogoutRedirectUri = "com.example.game://logout"
});
```

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
| `AppsFlyerKey` | SDK 3.2.0: optional server configuration for the AppsFlyer adapter; never log this value |

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
- The operation throws `TimeoutException` if no callback arrives within 180 seconds.
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

Logout revokes the current device session, opens the browser logout URL, and clears local credentials and device keys. Local state is cleared even if the remote request fails. The method does not wait for the final browser callback.

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

The SDK creates the order and opens `PaymentUrl`. `OrderId` confirms order creation only; it does not confirm payment.

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
