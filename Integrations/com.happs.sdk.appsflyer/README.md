# HApps AppsFlyer integration 0.2.1

Optional Android attribution for HApps SDK 3.4.0. The package includes AppsFlyer Unity SDK **6.18.1**; remove any other AppsFlyer Unity plugin before installing it. AppsFlyer 7 is not supported.

Projects upgrading from HApps SDK 3.1.2 should follow [Mobile Migration: SDK 3.1.2 to 3.2.0](../../UnitySDK/Packages/com.happs.sdk/MIGRATION_MOBILE_3.1.2_TO_3.2.0.md).

Projects upgrading from the `v3.2.0` release should follow [Updating from HApps SDK 3.2.0](../../UnitySDK/Packages/com.happs.sdk/MIGRATION_FROM_3.2.0.md).

## Installation

Add these dependencies to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.4.0",
    "com.happs.sdk.appsflyer": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/Integrations/com.happs.sdk.appsflyer#v3.4.0",
    "com.google.external-dependency-manager": "https://github.com/googlesamples/unity-jar-resolver.git?path=upm#v1.2.188"
  }
}
```

Run `Assets > External Dependency Manager > Android Resolver > Force Resolve` before building. Assemblies that call the adapter must reference `HAppsSDK` and `HAppsSDK.AppsFlyer`.

## HApps configuration

Send HApps the game's mobile `ClientId` and AppsFlyer Dev Key. The game backend does not need changes.

## Start

Call on Unity's main thread, once per application lifetime, after configuring HApps:

```csharp
using HAppsSDK;
using HAppsSDK.Attribution;

HApps.ConfigureMobile(new HAppsMobileAuthOptions
{
    PortalUrl = "https://portal.example.com",
    ClientId = "my-game-mobile",
    CallbackUri = "https://links.example.com/games/sample-game/callback"
});

#if UNITY_ANDROID && !UNITY_EDITOR
HAppsAppsFlyer.Initialize(new HAppsAppsFlyerOptions
{
    DebugLogging = true
});
// Request tracking after any required consent. The adapter waits for HApps configuration.
HAppsAppsFlyer.StartTracking();
#endif

var session = await HApps.Mobile.InitSessionAsync();
```

The adapter receives the Dev Key from HApps and waits for the mobile session before starting AppsFlyer.

`DebugLogging` controls the official AppsFlyer SDK debug output. Disable it in production builds.

GAID collection is enabled by default. Set `CollectAdvertisingIdentifiers = false` to disable advertising identifiers.

No AppsFlyer manifest entries are required in the game. The Android manifest merger adds `ACCESS_NETWORK_STATE` and `com.google.android.gms.permission.AD_ID` from the included libraries.

The adapter is Android-only. Do not add an AppsFlyer prefab or another initialization script.
AppsFlyer Purchase Connector is not included because the adapter only provides install attribution.

## Existing AppsFlyer integration

If the game already initializes AppsFlyer, set `UseExistingSdk = true`, initialize the HApps adapter first and forward the conversion-data callback:

```csharp
public void onConversionDataSuccess(string json)
{
    HAppsAppsFlyer.RecordConversionData(json);
    // Your existing conversion handling.
}
```

Call `HAppsAppsFlyer.StartTracking()` after starting the existing AppsFlyer instance. Set `ManageCustomerUserId = false` if the game manages AppsFlyer's Customer User ID.

## Lifecycle

`StopTracking()` pauses adapter submission and owned native tracking; `StartTracking()` resumes it. `Shutdown()` releases the adapter and permits reinitialization. Disposing the mobile provider through `HApps.Shutdown()` also releases its adapter. An externally owned AppsFlyer instance is never stopped by the adapter.

## Behavior

- Starts for anonymous and authorized mobile sessions.
- Maps `media_source` to `haff_pid`, `campaign` to `utm_campaign`, and `af_sub1` to `haff_cid`.
- Forwards `custom_data` unchanged to HApps. It also exposes the JSON without `referrer` as `QueryParams` and the extracted URL as `Referer` in `CurrentAttribution`.
- Keeps `pending` locally until AppsFlyer returns conversion data. Only `organic` and `non-organic` are sent to HApps.
- Tracks installation attribution only. Deep-link navigation and re-engagement are not included.

When `custom_data` is a JSON object, the adapter removes `referrer` and `referer` from the local `QueryParams` value and exposes the URL through `Referer`; `referrer` takes precedence when both keys exist. Missing custom data produces empty local fields. If the value is not a JSON object, `QueryParams` contains the original string and `Referer` is empty. The original `CustomData` value is never modified.

## Attribution data

`HApps.Mobile.CurrentAttribution` returns a snapshot of `MobileAttributionData`:

| Field | Value |
| --- | --- |
| `Provider` | `appsflyer` |
| `ProviderInstallId` | AppsFlyer installation ID |
| `HaffPid` | AppsFlyer `media_source` |
| `UtmCampaign` | AppsFlyer `campaign` |
| `HaffCid` | AppsFlyer `af_sub1` |
| `CustomData` | Original AppsFlyer `custom_data` string |
| `QueryParams` | `custom_data` JSON without `referrer` and `referer` |
| `Referer` | Value extracted from `referrer`, or from `referer` when `referrer` is absent |
| `Status` | `pending`, `organic`, or `non-organic` |
| `ObservedAt` | Unix timestamp in seconds when the result was received |

`QueryParams` and `Referer` are local fields for the game. The SDK sends the original `CustomData` value to HApps.

Read the current local state through `HApps.Mobile.CurrentAttribution`:

```csharp
MobileAttributionData attribution = HApps.Mobile.CurrentAttribution;
Debug.Log(attribution?.Status ?? "not available");
```

## Verify

Follow the [migration verification steps](../../UnitySDK/Packages/com.happs.sdk/MIGRATION_MOBILE_3.1.2_TO_3.2.0.md#7-verify-the-upgrade).
