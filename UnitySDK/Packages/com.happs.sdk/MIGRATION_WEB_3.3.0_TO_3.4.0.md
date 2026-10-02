# WebGL Migration: SDK 3.3.0 to 3.4.0

SDK 3.4.0 uses one `WebAuthResult` type for awaited and event-driven portal authentication.

## Update awaited portal authentication

`OpenPortalAuthPopup()` no longer returns `bool`.

Before:

```csharp
var authenticated = await HApps.Web.OpenPortalAuthPopup();
if (!authenticated)
    return;
```

After:

```csharp
var result = await HApps.Web.OpenPortalAuthPopup();
if (!result.IsSuccess)
    return;

Debug.Log($"Portal auth: {result.Action}, {result.User?.userId}");
```

## Update auth event handlers

Before:

```csharp
private void HandleAuthCompleted(
    UserData user,
    SignatureData signature,
    AuthAction action)
{
}
```

After:

```csharp
private void HandleAuthCompleted(WebAuthResult result)
{
    Debug.Log($"Portal auth: {result.Action}, {result.User?.userId}");
}
```

`WebAuthResult` exposes:

- `IsSuccess`: whether portal authentication produced a usable result;
- `User`: the authenticated portal user when available;
- `Signature`: the signature to send to the game backend when available;
- `Action`: `SignUp`, `Linked`, `Login`, or `Unknown`.

When the connected user is already verified, `OpenPortalAuthPopup()` completes locally without a new `auth_complete` event. It returns a successful result with `Action == AuthAction.Unknown`.

The browser integration remains on HApps JS SDK `1.1.2`.
