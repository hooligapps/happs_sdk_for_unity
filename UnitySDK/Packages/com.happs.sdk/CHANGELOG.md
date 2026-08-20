# Changelog

## 3.0.1

- Keep compatibility with the existing HApps JS SDK 1.0.3 contract
- Add a WebGL migration guide from Unity SDK 2.0.6
- Document the JS 1.0.3 embedded `ready` and theater-mode limitations
- Complete portal authentication immediately when the connected profile is already verified
- Complete WebGL payment failures without waiting indefinitely
- Add typed profile errors
- Add timeouts to interactive WebGL operations
- Dispose the WebGL bridge object and reject operations after shutdown
- Disable debug logging by default and redact authentication secrets from logs
- Store Android mobile tokens with AES-GCM using an Android Keystore-backed key
- Mark plaintext PlayerPrefs token storage as unsafe legacy behavior
- Add a configurable timeout to every mobile HTTP request
- Always clear local credentials during logout, including remote logout failures
- Require an exact mobile redirect endpoint match before accepting an OIDC callback
- Serialize mobile session mutations and share concurrent refresh operations
- Cancel login and reject stale state updates after logout or provider disposal

## 3.0.0

- Remove flat Web shortcuts from `HApps`; use `HApps.Web.*` explicitly
- Replace `HApps.Provider.Signature` with `HApps.Web.Signature`
- Document the current mobile session, login, logout, and payment flows
- Add `HApps.Web.OpenAgeVerification(bool adultMode = true)` for portal age verification UI
- Add the `HApps.Web.SetTheaterMode(bool enabled)` API surface for portal theater mode
- Add `HApps.SetDebugLogging(bool enabled)` for toggling SDK debug and warning logs

## 2.0.5

- Add `HApps.Web.AuthCompleted` event for subscribing to incoming `auth_complete`

## 2.0.5-preview.1

- Add best-effort `window.focus()` restore after payment completion for WebGL host pages that pause on blur

## 2.0.4

- Add `HApps.Web.IsReady()` for synchronous browser SDK readiness checks

## 2.0.3

- Rename Unity-side initialization flow to `Connect()`
- Clarify portal auth flow and signature-based backend auth in documentation

## 1.0.0

- Initial embedded Unity package version of HApps SDK
- WebGL runtime bridge for profile, auth popup, portal auth, and payments
- Operation cleanup fixes for timeout and synchronous startup failures
