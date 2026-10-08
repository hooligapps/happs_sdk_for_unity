# HApps Android Auth Tab 0.1.2

Optional Android Auth Tab support for HApps SDK. Install this package only when the project's Android build toolchain can consume `androidx.browser:browser:1.9.0`.

## Compatibility

| Unity | Package | Payment browser |
| --- | --- | --- |
| Unity 2022.3 default Android toolchain | Do not install | Custom Tab fallback from core SDK |
| Unity 6 with AGP 8.1 or newer | Install | Auth Tab with automatic callback capture |

Unity 2022.3 uses Gradle 7.2 and Android Gradle Plugin 7.1.2 by default. That toolchain cannot dex AndroidX Browser 1.9.0 reliably. The core HApps package therefore resolves Browser 1.8.0.

## Installation

Use the same git release tag for both packages:

```json
{
  "dependencies": {
    "com.happs.sdk": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/UnitySDK/Packages/com.happs.sdk#v3.5.0",
    "com.happs.sdk.browser-auth-tab": "https://github.com/hooligapps/happs_sdk_for_unity.git?path=/Integrations/com.happs.sdk.browser-auth-tab#v3.5.0",
    "com.google.external-dependency-manager": "https://github.com/googlesamples/unity-jar-resolver.git?path=upm#v1.2.188"
  }
}
```

Run `Assets > External Dependency Manager > Android Resolver > Force Resolve` after installation.

This package contains no runtime code. It raises the AndroidX Browser dependency from 1.8.0 to 1.9.0; the existing HApps runtime detects `AuthTabIntent` and uses it automatically. Removing the package restores the Custom Tab fallback.
