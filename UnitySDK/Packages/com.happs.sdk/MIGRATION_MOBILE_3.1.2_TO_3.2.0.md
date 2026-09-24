# Mobile Migration: SDK 3.1.2 to 3.2.0

SDK 3.2.0 adds optional Android install attribution. Existing authentication, update and payment integrations remain compatible. AppsFlyer is a separate package.

## 1. Choose the integration

For an existing mobile project without attribution, update only the core package. No application code or Android manifest change is required.

For AppsFlyer attribution, ask HApps to enable AppsFlyer for the mobile client, then install both HApps packages and initialize the adapter as described below.

## 2. Update package references

Use the same release tag or commit for both HApps packages. Replace `<happs-ref>` with `v3.2.0` after that release is published, or with the supplied commit while integrating the unreleased version.

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#<happs-ref>",
    "com.happs.sdk.appsflyer": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/Integrations/com.happs.sdk.appsflyer#<happs-ref>",
    "com.google.external-dependency-manager": "https://github.com/googlesamples/unity-jar-resolver.git?path=upm#v1.2.188"
  }
}
```

The package includes AppsFlyer Unity SDK 6.18.1. Remove any other AppsFlyer Unity plugin before installing it.

Projects using assembly definitions must reference both `HAppsSDK` and `HAppsSDK.AppsFlyer` from the assembly that starts mobile integration.

## 3. Request AppsFlyer configuration

Send HApps the mobile `ClientId` and AppsFlyer Dev Key. The game backend does not need changes.

## 4. Initialize AppsFlyer

Initialize the adapter once, on Unity's main thread, after `HApps.ConfigureMobile(...)`:

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
    DebugLogging = Debug.isDebugBuild
});
HAppsAppsFlyer.StartTracking();
#endif

MobileSession session = await HApps.Mobile.InitSessionAsync();
```

The adapter waits for the HApps mobile session before starting AppsFlyer.

The current local attribution state is available through `HApps.Mobile.CurrentAttribution`. It can be `null` before AppsFlyer provides an installation ID.

Do not initialize or start the bundled AppsFlyer SDK separately. If the game already owns an AppsFlyer integration, follow the `UseExistingSdk` instructions in the [adapter README](../../../Integrations/com.happs.sdk.appsflyer/README.md) instead.

## 5. Advertising ID behavior

GAID collection is enabled by default.

Disable advertising identifiers when required by the game's consent or privacy implementation:

```csharp
HAppsAppsFlyer.Initialize(new HAppsAppsFlyerOptions
{
    CollectAdvertisingIdentifiers = false
});
```

With collection disabled, the device cannot be registered for AppsFlyer Live Events by AID.

## 6. Resolve Android dependencies

In Unity run:

```text
Assets > External Dependency Manager > Android Resolver > Force Resolve
```

Then rebuild the APK. The resolved Android build must include:

```text
com.appsflyer:af-android-sdk:6.18.1
com.google.android.gms:play-services-ads-identifier:18.2.0
```

No manifest changes are required. Android manifest merging adds `ACCESS_NETWORK_STATE` and `com.google.android.gms.permission.AD_ID`. Existing HApps intent filters remain unchanged.

## 7. Verify the upgrade

1. Run Android dependency resolution and build a development APK.
2. Install and launch the APK.
3. Confirm the logs contain `Starting AppsFlyer SDK` and `AppsFlyer SDK started`.
4. Confirm the AppsFlyer request contains `advertising_id` and does not contain `ad_ids_disabled: true`.
5. Inspect `HApps.Mobile.CurrentAttribution?.Status`. A `pending` value is local and is not sent to HApps.
