# WebGL Migration: SDK 3.4.0 to 3.5.0

SDK 3.5.0 synchronizes the Unity Web API with HApps JS SDK 1.1.2.

## Update age verification calls

The portal no longer accepts `adultMode`.

Before:

```csharp
HApps.Web.OpenAgeVerification(adultMode);
```

After:

```csharp
HApps.Web.OpenAgeVerification();
```

Subscribe to the completion event when the game needs confirmation:

```csharp
HApps.Web.AgeVerificationCompleted += confirmed =>
    Debug.Log($"Age verification completed: {confirmed}");
```

## User and IDP popup data

- `UserData.id` exposes the required portal user ID. `userId` remains available for compatible integrations.
- The `age_verification_complete` payload uses `confirmed` so it is not confused with the email-verification field `UserData.verified`.
- `OpenIdpAuthPopup(url, callbackOrigin)` supports popup callbacks whose trusted origin differs from the initial URL origin.
- `AuthPopupData.payloadJson` contains the optional arbitrary popup payload as raw JSON. It is `null` when the popup does not send a payload.

The browser integration remains on HApps JS SDK `1.1.2`.
