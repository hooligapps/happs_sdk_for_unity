# Mobile attribution API (SDK 3.2.0)

The backend needs two changes: return the optional analytics-provider configuration from session initialization and accept attribution through a separate endpoint. This repository implements the Unity client; deploy backend support before enabling the adapter.

## 1. Return analytics configuration

`POST /api/v1/mobile/session/init` keeps its existing request and authentication unchanged. Add the optional `analyticProvider` and `analyticKey` fields to its response:

```json
{
  "accessToken": "mobile-session-token",
  "expiresIn": 3600,
  "publicId": "player-id",
  "verified": false,
  "analyticProvider": "appsflyer",
  "analyticKey": "configured-appsflyer-dev-key"
}
```

Resolve both values from the configuration of the verified mobile client. Return them for anonymous sessions as well. If analytics is not configured, omit the fields or return null/empty. For AppsFlyer, `analyticKey` is the client SDK Dev Key, not a server Reporting API credential.

The SDK exposes the values as `MobileSession.AnalyticProvider` and `MobileSession.AnalyticKey`. The key is kept in memory, not logged or persisted. The optional AppsFlyer adapter starts once the provider is `appsflyer`, the key is nonblank, and `StartTracking()` has been requested. Missing configuration does not block the game. A later session refresh can supply it; changes after native AppsFlyer startup do not automatically restart or stop that SDK.

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
  "haff_pid": "partner",
  "utm_campaign": "summer",
  "haff_cid": "affiliate-click-id",
  "custom_data": "{\"link_id\":\"partner-main\",\"game\":\"sample-game\"}",
  "status": "non-organic",
  "observedAt": 1700000000
}
```

Use the existing mobile-session bearer authentication, including anonymous sessions. Derive the game, device and current player from the validated session. No extra signature, payload hash, nonce or outer attribution object is sent. No new authentication scheme is required.

Field rules:

- `provider`: `appsflyer` for this integration.
- `providerInstallId`: required, nonblank AppsFlyer installation ID.
- `haff_pid`, `utm_campaign`, `haff_cid`: primary Portal Affiliates fields mapped from AppsFlyer `media_source`, `campaign`, and `af_sub1`.
- `custom_data`: the unchanged string returned by AppsFlyer for the same-name custom attribution-link parameter. The server can parse it as JSON when needed.
- `status`: `organic` or `non-organic`. The client keeps `pending` locally and does not send it to this endpoint. Missing source/campaign does not imply organic.
- `observedAt`: positive Unix timestamp in seconds when the client observed the result.
- Each attribution string: maximum 1024 UTF-8 bytes.
- `custom_data`: maximum 16384 UTF-8 bytes after URL decoding by AppsFlyer.

Save the attribution against the installation and its current device/player association, then return HTTP 200:

```json
{ "ok": true }
```

Return the same response for an already saved duplicate. Empty responses/204 and `ok: false` are not acknowledgements in the current SDK.

Use `(clientId, provider, providerInstallId)` to identify the installation. Repeated submission must not create another installation. Keep the first acquisition separate from later corrections if your analytics needs that distinction. On login, maintain the player association through your existing device/player relationship. HApps DeviceId can change on logout; it is not the lifetime AppsFlyer installation ID.

Return the existing mobile API error format. For an expired/invalid session use the same recoverable 401 codes as other mobile endpoints: `mobile_session_expired` or `invalid_mobile_session`. The SDK renews the session and retries once. Other failures remain pending for adapter retries. These are client-reported analytics fields, not independently verified AppsFlyer evidence.

## Unity API

```csharp
await HApps.Mobile.InitSessionAsync();
await HApps.Mobile.SendAttributionAsync(data);
```

The adapter queues data with `SetAttribution(data)` and submits with `FlushAttributionAsync()`. Both operate on Unity's main thread. Pending data is available through `HApps.Mobile.CurrentAttribution`, and flush does not send it. Flush requires an existing session and does nothing after logout. It uses the existing token, refreshing it when necessary. An acknowledgement applies only to the snapshot sent. Duplicate successful snapshots for the same device do not produce another HTTP request. Logout cancels queued work and preserves attribution for a subsequent session. Attribution is never added to the session/init request.
