# Updating from HApps SDK 3.2.0

This guide covers the client changes made after the `v3.2.0` release. Use the release tag provided by HApps for both the core SDK and the optional AppsFlyer package.

The game backend does not require changes. HApps must register the new callback URI and publish the Android domain association before the updated APK is released.

## 1. Update the mobile callback

The separate authentication and logout redirect fields are replaced by one `CallbackUri`:

```csharp
HApps.ConfigureMobile(new HAppsMobileAuthOptions
{
    PortalUrl = "https://portal.example.com",
    ClientId = "my-game-mobile",
    CallbackUri = "https://links.example.com/games/sample-game/callback"
});
```

The same callback is used for authentication, logout and payment returns. Keep the configured URI free of query parameters. The server adds `type=login`, `type=logout`, or `type=payment` to the returned URL together with the operation data.

`LogoutAsync()` now waits for the server callback. It clears local credentials only for a matching `type=logout&status=success` result. For `status=cancelled`, it preserves the current session and completes with `OperationCanceledException`.

Remove the old fields from application code:

```csharp
RedirectUri
PostLogoutRedirectUri
```

`CallbackUri` may use the same host as `PortalUrl` or a separate host. The host choice does not replace the Custom Tab and Android App Links configuration required for returning to the application.

## 2. Configure Android App Links

Add this block directly under the root `manifest` element. Android 11 and newer require it for the SDK to discover the preferred Custom Tabs provider:

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
```

Replace custom-scheme callback filters with a verified HTTPS App Link on the Unity launcher activity:

```xml
<intent-filter android:autoVerify="true">
    <action android:name="android.intent.action.VIEW" />
    <category android:name="android.intent.category.DEFAULT" />
    <category android:name="android.intent.category.BROWSABLE" />

    <data
        android:scheme="https"
        android:host="links.example.com"
        android:path="/games/sample-game/callback" />
</intent-filter>
```

Send HApps:

- the exact callback URI;
- the Android application ID;
- the SHA-256 fingerprint of every certificate used to sign an APK for that environment.

HApps publishes the association at:

```text
https://links.example.com/.well-known/assetlinks.json
```

The file must return HTTP 200 over HTTPS without authentication or redirects. Its package name and certificate fingerprint must match the installed APK. If one APK supports multiple environments, add each host in a separate intent filter.

On Android 12 or newer, inspect verification with:

```bash
adb shell pm verify-app-links --re-verify com.example.game
adb shell pm get-app-links com.example.game
```

Open the callback link from another application when testing. Typing or pasting it into a browser address bar explicitly opens it in that browser and does not reliably test App Links.

Add External Dependency Manager for Unity if the project does not already include it, then resolve Android dependencies:

```json
"com.google.external-dependency-manager": "https://github.com/googlesamples/unity-jar-resolver.git?path=upm#v1.2.188"
```

```text
Assets > External Dependency Manager > Android Resolver > Force Resolve
```

The updated SDK resolves `androidx.browser:browser:1.9.0`. Authentication and logout use a Custom Tab configured to return verified redirects to the application. Payment uses an Auth Tab when supported. The SDK gives it the callback host and path, so reaching that callback closes the browser surface and returns to the game without an `intent://` timer or confirmation dialog. The callback query string does not affect matching. Unsupported browsers fall back to a Custom Tab.

## 3. Update lifecycle handling

Call `HApps.ConfigureMobile(...)` once. A second call now throws `InvalidOperationException`. To replace the configuration, call `HApps.Shutdown()` and start a new SDK lifecycle.

`HApps.Mobile.CurrentSession` returns a snapshot of the current session. Subscribe to `SessionChanged` when the game must react to login, refresh or logout:

```csharp
var mobile = HApps.Mobile;
mobile.SessionChanged += HandleSessionChanged;

void HandleSessionChanged(MobileSession previous, MobileSession current)
{
    if (previous?.PublicId != current?.PublicId)
        ReloadPlayer(current?.PublicId);
}
```

Unsubscribe from the same provider instance before shutdown. A refresh can raise the event without changing `PublicId`, so reload player data only when the ID changes.

## 4. Handle cancellation and mobile errors

Existing method signatures remain available. New overloads accept `CancellationToken` for mobile session, login, logout, payment, update and attribution operations, and for awaited WebGL operations.

Mobile HTTP failures now throw `HAppsMobileException`:

```csharp
try
{
    await HApps.Mobile.CheckForUpdateAsync(versionCode, cancellationToken);
}
catch (HAppsMobileException error)
{
    Debug.LogError($"HApps error: {error.Code}; request: {error.RequestId}");
}
catch (OperationCanceledException)
{
    // The caller cancelled the wait.
}
```

Use `StatusCode`, `Code`, `RequestId` and `IsRetryable` to select UI and retry behavior. Cancellation stops the client wait and active request where possible; it cannot undo a request already processed by a server or close an external browser window.

Only one operation of the same interactive type may run at once. A concurrent login, mobile order creation or same-type WebGL request throws `InvalidOperationException` and leaves the first operation active.

## 5. Review payment calls

`CreatePaymentAsync` validates and copies its request before asynchronous work. The following fields are required:

| Field | Constraint |
| --- | --- |
| `RequestId` | maximum 128 characters |
| `ProductId` | maximum 128 characters |
| `Price` | `0.01`–`99999999.99` |
| `Currency` | maximum 10 characters |
| `Description` | maximum 500 characters |

Continue to persist and reuse `RequestId` when retrying the same uncertain purchase. Product fulfillment still requires confirmation by the game backend.

WebGL payment callbacks are accepted only when their `orderId` matches the active request. Old or unrelated callbacks are ignored.

## 6. Update the optional AppsFlyer package

Run Android dependency resolution again after updating:

```text
Assets > External Dependency Manager > Android Resolver > Force Resolve
```

The package no longer includes AppsFlyer Purchase Connector. It includes only the dependencies needed for install attribution.

`HApps.Mobile.CurrentAttribution` now exposes:

- `CustomData`: the original AppsFlyer `custom_data` string;
- `QueryParams`: the same JSON with `referrer` or `referer` removed;
- `Referer`: the extracted referrer value.

The primary attribution fields remain `HaffPid`, `UtmCampaign` and `HaffCid`, mapped from AppsFlyer `media_source`, `campaign` and `af_sub1`. See the [AppsFlyer attribution data contract](../../../Integrations/com.happs.sdk.appsflyer/README.md#attribution-data) for the complete public field list.

These parsed fields are local SDK fields. The attribution request sent to HApps remains unchanged and sends the original `custom_data`.

For a valid JSON object, `QueryParams` excludes both `referrer` and `referer`; `Referer` receives the extracted string. Missing custom data produces empty fields. A non-object value remains unchanged in `QueryParams` and leaves `Referer` empty.

The adapter also adds lifecycle controls:

```csharp
HAppsAppsFlyer.StopTracking();
HAppsAppsFlyer.StartTracking();
HAppsAppsFlyer.Shutdown();
```

`HApps.Shutdown()` releases the attached adapter automatically. With `UseExistingSdk = true`, the game remains responsible for starting and stopping its own AppsFlyer SDK instance.

## 7. Remove the legacy package

Install the SDK through Unity Package Manager. Remove an old imported `HAppsSDK.unitypackage` before updating; the legacy package must not be mixed with the UPM package.

## 8. Verify the update

1. Confirm the project compiles without `RedirectUri` or `PostLogoutRedirectUri`.
2. Confirm the final APK contains the HTTPS callback intent filter.
3. Confirm `assetlinks.json` contains the APK package name and signing-certificate SHA-256.
4. Open the callback from another application and confirm the game starts.
5. Complete login through the SDK Custom Tab and confirm that it returns to the game.
6. Create a payment and confirm that the browser surface closes and the game resumes after checkout reaches the callback.
7. If AppsFlyer is installed, confirm attribution initializes and `CurrentAttribution` exposes the expected custom data.

See [Mobile Integration](MOBILE_INTEGRATION.md) for the complete Android setup.
