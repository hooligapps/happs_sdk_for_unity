# HApps Web SDK

## Full Integration Guide (Two Integration Flows)

This document describes the complete integration of **HApps Web SDK**.

There are **two independent integration flows**:

1.  🔐 Standalone Login Popup Flow (for standalone web games)
2.  🌐 Embedded Platform Flow (iframe + initialization + platform
    script)

These flows solve different problems and may be used separately.

------------------------------------------------------------------------

# FLOW 1 --- Standalone Login Popup (No Platform Required)

This flow is used when:

-   The game runs as standalone WebGL
-   No platform iframe is involved
-   Login is triggered by a button inside the game

In this flow, Unity directly opens backend auth popup.

------------------------------------------------------------------------

## 1. Unity Usage

``` csharp
var url = $"{Env.ServerUrl}/api/auth/idp?token={launchToken}";
var ticket = await HApps.OpenAuthPopup(url);

if (!string.IsNullOrEmpty(ticket))
{
    var resp = await Gateway.Post("/api/auth/idp/finish", new { ticket });
}
```

No SDK initialization is required for this flow.

Only `OpenAuthPopup()` is used.

------------------------------------------------------------------------

## 2. Backend Responsibilities

Required endpoints:

GET /api/auth/idp GET /api/auth/idp/callback POST /api/auth/idp/finish

Backend must:

1.  Generate OIDC URL (PKCE + state + nonce)
2.  Store state in Redis (TTL \~300s)
3.  Redirect to provider
4.  On callback:
    -   Validate state
    -   Exchange code (PKCE)
    -   Retrieve user info
    -   Issue short-lived ticket (TTL \~60s)
5.  On /finish:
    -   Consume ticket (getDel)
    -   Verify heroId
    -   Issue real JWT auth token

Redis keys:

auth_state:{state} auth_ticket:{ticket}

Tickets must be single-use.

------------------------------------------------------------------------

# FLOW 2 --- Embedded Platform Flow (iframe Integration)

This flow is used when:

-   Game is embedded inside platform portal
-   Platform controls login
-   Game runs inside iframe

This flow REQUIRES initialization and platform JS script.

------------------------------------------------------------------------

## 1. index.html Setup (WebGL Template)

The following script must be included in `index.html`:

``` html
<script src="hooligapps.js"></script>
<script>
    HooligappsIntegration.init({
        platformOrigin: "https://portal.domain",
        unityObjectName: "HAppsJSBridge",
        unityMethodName: "OnHooligappsMessage"
    });
</script>
```

After Unity loads:

``` javascript
createUnityInstance(canvas, config).then(instance => {
    window.unityInstance = instance;
    HooligappsIntegration.setUnityInstance(instance);
});
```

------------------------------------------------------------------------

## 2. Unity Initialization

In embedded mode, SDK must be initialized:

``` csharp
await HApps.Initialize();
```

The platform sends:

``` javascript
postMessage({ type: "platform_launch", token })
```

JS bridge forwards init message to Unity.

------------------------------------------------------------------------

## 3. Profile

``` csharp
var profile = await HApps.RequestProfile();
```

User data is provided by platform.

------------------------------------------------------------------------

## 4. Payments

``` csharp
var payment = await HApps.RequestPayment(new PaymentItem
{
    itemId = "coins_pack",
    quantity = 1,
    price = 9.99f
});

if (payment.IsSuccess)
{
    GrantRewards();
}
```

Payment lifecycle:

Unity → JS → Portal → Checkout → Portal → JS → Unity

Backend MUST verify payment before granting rewards.

------------------------------------------------------------------------

# Backend Requirements (Embedded Flow)

In addition to standalone endpoints, backend must support:

POST /api/sign (SSO login using launch token)

Backend must:

1.  Validate launch token with platform
2.  Create or load user
3.  Return:

{ userData: { id }, signatureData: { signature } }

Signature may be used for payment authorization.

------------------------------------------------------------------------

# Security Requirements (Both Flows)

-   Always validate event.origin in JS
-   Never use "\*" in postMessage
-   Use PKCE for OIDC
-   Validate state and nonce
-   Use short Redis TTL
-   Tickets must be single-use
-   Never trust client payment result
-   Always verify server-side

------------------------------------------------------------------------

# Summary

Flow 1 (Standalone): - Uses popup only - No SDK initialization
required - No platform script required

Flow 2 (Embedded): - Requires hooligapps.js - Requires Initialize() -
Supports profile + payment - Used inside iframe

------------------------------------------------------------------------

# Version

HApps Web SDK -- Integration Guide v2.0
