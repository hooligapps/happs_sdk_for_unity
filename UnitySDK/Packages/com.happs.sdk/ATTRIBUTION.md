# Mobile attribution API (SDK 3.2.0)

The backend needs two changes: return the optional AppsFlyer Dev Key from session initialization and accept attribution through a separate endpoint. This repository implements the Unity client; deploy backend support before enabling the adapter.

## 1. Return AppsFlyer configuration

`POST /api/v1/mobile/session/init` keeps its existing request and authentication unchanged. Add the optional `appsFlyerKey` field to its response:

```json
{
  "accessToken": "mobile-session-token",
  "expiresIn": 3600,
  "publicId": "player-id",
  "verified": false,
  "appsFlyerKey": "configured-appsflyer-dev-key"
}
```

Resolve the key from the configuration of the verified mobile client. Return it for anonymous sessions as well. If AppsFlyer is not configured, omit the field or return null/empty. This is the client SDK Dev Key, not a server Reporting API credential.

The SDK exposes it as `MobileSession.AppsFlyerKey`. It is kept in memory, not logged or persisted. The optional adapter starts once both a session with a nonblank key and an explicit `StartTracking()` request are available. Missing keys do not block the game. A later session refresh can supply a key; changes after native AppsFlyer startup do not automatically restart or stop that SDK.

## 2. Accept attribution

```http
POST /api/v1/mobile/attribution
Authorization: Bearer <mobile-session-access-token>
Content-Type: application/json
```

The body contains the attribution fields directly:

```json
{
  "provider": "appsflyer",
  "providerInstallId": "appsflyer-install-id",
  "mediaSource": "partner",
  "campaign": "summer",
  "campaignId": "42",
  "status": "non-organic",
  "observedAt": 1700000000
}
```

Use the existing mobile-session bearer authentication, including anonymous sessions. Derive the game, device and current player from the validated session. No extra signature, payload hash, nonce or outer attribution object is sent. No new authentication scheme is required.

Field rules:

- `provider`: `appsflyer` for this integration.
- `providerInstallId`: required, nonblank AppsFlyer installation ID.
- `mediaSource`, `campaign`, `campaignId`: optional strings; may be null/empty.
- `status`: `pending`, `organic` or `non-organic`. Pending means the installation ID is known but conversion data has not arrived. Missing source/campaign does not imply organic.
- `observedAt`: positive Unix timestamp in seconds when the client observed the result.
- Each attribution string: maximum 1024 UTF-8 bytes.

Save the attribution against the installation and its current device/player association, then return HTTP 200:

```json
{ "ok": true }
```

Return the same response for an already saved duplicate. Empty responses/204 and `ok: false` are not acknowledgements in the current SDK.

Use `(clientId, provider, providerInstallId)` to identify the installation. Repeated submission must not create another installation. A pending record can be completed by resolved attribution; do not overwrite a resolved source with pending. Keep the first acquisition separate from later corrections if your analytics needs that distinction. On login, maintain the player association through your existing device/player relationship. HApps DeviceId can change on logout; it is not the lifetime AppsFlyer installation ID.

Return the existing mobile API error format. For an expired/invalid session use the same recoverable 401 codes as other mobile endpoints: `mobile_session_expired` or `invalid_mobile_session`. The SDK renews the session and retries once. Other failures remain pending for adapter retries. These are client-reported analytics fields, not independently verified AppsFlyer evidence.

## Unity API

```csharp
await HApps.Mobile.InitSessionAsync();
await HApps.Mobile.SendAttributionAsync(data);
```

The adapter queues data with `SetAttribution(data)` and submits with `FlushAttributionAsync()`. Both operate on Unity's main thread. Flush requires an existing session and does nothing after logout. It uses the existing token, refreshing it when necessary. An acknowledgement applies only to the snapshot sent; a callback arriving during HTTP remains pending. Duplicate successful snapshots for the same device do not produce another HTTP request. Logout cancels queued work and preserves attribution for a subsequent session. Attribution is never added to the session/init request.
