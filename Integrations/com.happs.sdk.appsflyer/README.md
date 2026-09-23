# HApps AppsFlyer integration 0.1.0

Optional Android integration for HApps SDK 3.2.0. The package already contains the official AppsFlyer Unity SDK **6.18.1**, so a game must not install another copy. AppsFlyer 7 has a different API and is not supported by this adapter. No `Distribution`/`setOutOfStore` value is set: the same APK can be distributed through Google Play and your website.

## Installation

This package lives outside the sample project's `Packages` directory intentionally. Unity automatically loads embedded packages in that directory; the base SDK sample must remain usable without AppsFlyer.

Add these dependencies to the consuming game's `Packages/manifest.json`. Replace `<happs-commit>` with the commit containing this implementation; it is not available in the old `v3.1.2` release. Once released, use the corresponding release tag for both HApps packages.

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#<happs-commit>",
    "com.happs.sdk.appsflyer": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/Integrations/com.happs.sdk.appsflyer#<happs-commit>",
    "com.google.external-dependency-manager": "https://github.com/googlesamples/unity-jar-resolver.git?path=upm#v1.2.188"
  }
}
```

For local development, reference the HApps packages with `file:` paths. A game using only `com.happs.sdk` needs neither the adapter nor EDM4U.

EDM4U is build tooling used by the bundled SDK to resolve its Android Maven libraries. It does not install another copy of the AppsFlyer Unity SDK. Resolve Android dependencies before building. If your scripts use assembly definitions, reference `HAppsSDK` and `HAppsSDK.AppsFlyer`. The bundled AppsFlyer sources retain their MIT license in [`ThirdParty/AppsFlyer/LICENSE`](ThirdParty/AppsFlyer/LICENSE). See the official [installation guide](https://dev.appsflyer.com/hc/docs/installation) for Android build requirements.

## Backend prerequisite

Deploy the [attribution API contract](../../UnitySDK/Packages/com.happs.sdk/ATTRIBUTION.md) before enabling the adapter. The server must return `analyticProvider: "appsflyer"` and `analyticKey` from `session/init`, and implement `POST /api/v1/mobile/attribution`. The session request and its signature remain unchanged. Without this configuration, the adapter stays idle and the game can continue.

## Start

Call on Unity's main thread, once per application lifetime, after configuring HApps:

```csharp
using HAppsSDK;
using HAppsSDK.Attribution;

HApps.ConfigureMobile(new HAppsMobileAuthOptions
{
    PortalUrl = "https://portal.igra.rocks",
    ClientId = "my-game-mobile",
    RedirectUri = "com.example.game://auth/callback",
    PostLogoutRedirectUri = "com.example.game://logout"
});

#if UNITY_ANDROID && !UNITY_EDITOR
HAppsAppsFlyer.Initialize(new HAppsAppsFlyerOptions
{
    DebugLogging = true
});
// Request tracking after any required consent. The adapter waits for session/init and its key.
HAppsAppsFlyer.StartTracking();
#endif

var session = await HApps.Mobile.InitSessionAsync();
```

The Dev Key is configured on the backend per mobile client, not in the game. `session.AnalyticProvider` and `session.AnalyticKey` expose the generic analytics configuration. The adapter starts once when `StartTracking()` has been requested, the provider is `appsflyer`, and the key is nonblank. Missing configuration is not an error; a later refresh can supply it. Once the native SDK starts, changing/removing the configuration in a later session response does not reinitialize or stop it; native stopping/consent remains application-owned. The key is kept in memory and is not logged or persisted by HApps.

`DebugLogging` controls the official AppsFlyer SDK debug output. Disable it in production builds.

Advertising identifier collection is disabled by default, so the adapter does not require Google Play Services' `AdvertisingIdClient`. Set `CollectAdvertisingIdentifiers = true` only when GAID/OAID/AAID collection is required; the game must then provide the corresponding platform libraries.

The adapter is Android-only; initialization in the Editor explicitly throws. No AppsFlyer prefab or second init/start script is needed. Native callbacks are preserved for IL2CPP.

## Existing AppsFlyer integration

Set `UseExistingSdk = true` to leave init/start and native callback ownership with the game. Initialize the HApps adapter before forwarding callbacks, start the game's AppsFlyer instance once, then call `HAppsAppsFlyer.StartTracking()` to enable synchronization once a HApps session exists. In this mode the game owns native configuration; the adapter does not require or apply the server key. Forward the complete **conversion-data** callback:

```csharp
public void onConversionDataSuccess(string json)
{
    HAppsAppsFlyer.RecordConversionData(json);
    // Your existing conversion handling.
}
```

Use `ManageCustomerUserId = false` if the game already manages AppsFlyer's Customer User ID. Otherwise HApps manages it: current PublicId when a session exists, AppsFlyer installation ID when logged out. Identity changes are reconciled on the next poll (up to one second); the adapter does not emit login or purchase events. Code emitting identity-sensitive events immediately after login/logout should set the appropriate Customer User ID itself with `ManageCustomerUserId = false`.

## Behavior

- Starts after HApps session initialization (including an anonymous session); account login is not required. Failure or absence of conversion data does not turn an installation into organic traffic.
- Maps the required Portal Affiliates fields from AppsFlyer: `media_source` to `HaffPid`, `campaign` to `UtmCampaign`, and `af_sub1` to `HaffCid`. The value of the `custom_data` attribution-link parameter is forwarded unchanged as a string; the SDK does not parse its JSON. Explicit organic results are accepted without campaign fields. Unknown/malformed results are ignored with sanitized logs.
- Stores AppsFlyer ID with `pending` status until conversion data becomes available. This ID can be linked to HApps before the campaign is known.
- Persists normalized attribution and the `custom_data` object in PlayerPrefs, scoped by PortalUrl + ClientId. No credentials or full callback payloads are stored/logged. PlayerPrefs is untrusted client storage.
- Restores attribution only if its AppsFlyer installation ID matches the current native ID. Logout keeps attribution; switching environments discards the old binding.
- `session/init` never carries attribution. `FlushAttributionAsync` sends the pending snapshot through the dedicated `/attribution` API with the existing mobile-session Bearer token. If another snapshot arrives during that request, the next periodic check sends it. Successful duplicate flushes do not make HTTP requests. Manual integrations can call `HApps.Mobile.SendAttributionAsync(data)` after session initialization.
- Checks binding once per second, pending synchronization every 30 seconds, and retries failures with delays increasing from 2 to 60 seconds. Failed data remains available for subsequent attribution retries and application launches. An expired token is renewed through the existing session flow; a recoverable 401 is retried once.
- Does not register a device just to send attribution. A queued flush rechecks the session after acquiring the session lock and is cancelled by logout/disposal.
- `HApps.Shutdown()` ends this adapter's binding; it does not stop the native AppsFlyer SDK. Configure once for the app lifetime; do not shut down and reinitialize HApps during scene changes. The application owns AppsFlyer consent/revocation and native SDK stopping.
- Direct/deferred deep-link navigation and re-engagement are outside this package's first version. App-open attribution callbacks never overwrite installation attribution. Existing auth/payment deep links continue to use HApps' listener.

## Verify on a device

Configure the Android app and attribution links in AppsFlyer, including the website/APK download destination. Test a fresh install from a link, an organic install, delayed launch/network changes, guest → login → logout, and offline callback/retry. Verify the AppsFlyer dashboard and backend link by game + AppsFlyer ID. The SDK records first launch, not the APK download itself. Out-of-store matching remains subject to AppsFlyer's attribution coverage.

Compilation checks do not validate Android Keystore signing, native dependency resolution, or live AppsFlyer matching.

The mobile attribution endpoint receives `haff_pid`, `utm_campaign`, and `haff_cid` as primary fields. The complete AppsFlyer `custom_data` value is sent under the same `custom_data` name as a string; its JSON keys must already use the server's internal names.

Example attribution-link parameters before URL encoding:

```text
pid=demo-partner-alpha
c=demo-campaign
af_sub1=5b9cc1d5-d722-463f-9288-b505a79526c7
custom_data={"link_id":"demo-link-00","game":"passion-industry","offer_id":"demo-offer-private"}
```
