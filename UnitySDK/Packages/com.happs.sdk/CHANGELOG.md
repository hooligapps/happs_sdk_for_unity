# Changelog

## 3.0.0

- Remove flat Web shortcuts from `HApps`; use `HApps.Web.*` explicitly
- Replace `HApps.Provider.Signature` with `HApps.Web.Signature`
- Document the current mobile session, login, logout, and payment flows
- Add `HApps.Web.OpenAgeVerification(bool adultMode = true)` for portal age verification UI
- Add `HApps.Web.SetTheaterMode(bool enabled)` for portal theater mode toggle
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
