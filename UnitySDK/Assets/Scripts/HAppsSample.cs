using System;
using System.Threading.Tasks;
using HAppsSDK;
using UnityEngine;

public sealed class HAppsSample : MonoBehaviour
{
    [Header("Standalone")]
    [SerializeField] private string serverUrl = "https://your-backend.example/api";
    [SerializeField] private string launchToken = "";

    [Header("Payments")]
    [SerializeField] private string orderId = "";

    [Header("Debug UI")]
    [SerializeField] private bool showDebugGui = true;
    [SerializeField] private bool adultMode = true;
    [SerializeField] private bool theaterModeEnabled = true;

    private string _lastStatus = "Idle";
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;

    public async void PortalConnect()
    {
        try
        {
            var connected = await HApps.Web.Connect();
            LogStatus($"Connect: {connected}");
        }
        catch (Exception ex)
        {
            LogError($"Connect failed: {ex}");
        }
    }

    public async void PortalLogin()
    {
        try
        {
            var authOk = await HApps.Web.OpenPortalAuthPopup();
            LogStatus($"Portal auth: {authOk}");
        }
        catch (Exception ex)
        {
            LogError($"Portal auth failed: {ex}");
        }
    }

    public async void StandaloneLogin()
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            LogError("serverUrl is empty");
            return;
        }

        if (string.IsNullOrWhiteSpace(launchToken))
        {
            LogStatus("launchToken is empty");
        }

        try
        {
            var url = BuildIdpUrl(serverUrl, launchToken);
            var data = await HApps.Web.OpenIdpAuthPopup(url);

            LogStatus($"IDP data: {data.flow} {data.ticket}");
        }
        catch (Exception ex)
        {
            LogError($"Standalone login failed: {ex}");
        }
    }

    public async void RequestProfile()
    {
        try
        {
            var profile = await HApps.Web.GetProfile();
            LogStatus($"Profile: {(profile != null ? profile.ToString() : "null")}");
        }
        catch (Exception ex)
        {
            LogError($"GetProfile failed: {ex}");
        }
    }

    public async void StartPayment()
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            LogError("orderId is empty");
            return;
        }

        try
        {
            var payment = await HApps.Web.MakePayment(orderId);
            LogStatus($"Payment: {payment}");
        }
        catch (Exception ex)
        {
            LogError($"MakePayment failed: {ex}");
        }
    }

    public void LogEnvironment()
    {
        LogStatus($"IsPortalSite={HApps.Web.IsPortalSite()}, IsReady={HApps.Web.IsReady()}");
    }

    public void ShutdownSdk()
    {
        HApps.Shutdown();
        LogStatus("SDK shutdown");
    }

    public void ShowAgeVerification()
    {
        HApps.Web.OpenAgeVerification(adultMode);
        LogStatus($"Age verification requested: adultMode={adultMode}");
    }

    public void ApplyTheaterMode()
    {
        HApps.Web.SetTheaterMode(theaterModeEnabled);
        LogStatus($"Theater mode requested: enabled={theaterModeEnabled}");
    }

    private void OnGUI()
    {
        if (!showDebugGui)
            return;

        const float width = 320f;
        const float lineHeight = 32f;
        const float gap = 8f;

        var areaHeight = 520f;
        GUILayout.BeginArea(new Rect(16f, 16f, width, areaHeight), GUI.skin.box);
        GUILayout.Label("HApps Sample", GetTitleStyle());
        GUILayout.Space(gap);

        GUILayout.Label("Environment", GetSectionStyle());
        if (GUILayout.Button("Log Environment", GUILayout.Height(lineHeight)))
            LogEnvironment();

        if (GUILayout.Button("Shutdown SDK", GUILayout.Height(lineHeight)))
            ShutdownSdk();

        GUILayout.Space(gap);
        GUILayout.Label("Portal Flow", GetSectionStyle());
        if (GUILayout.Button("Portal Connect", GUILayout.Height(lineHeight)))
            PortalConnect();

        if (GUILayout.Button("Portal Login", GUILayout.Height(lineHeight)))
            PortalLogin();

        if (GUILayout.Button("Request Profile", GUILayout.Height(lineHeight)))
            RequestProfile();

        adultMode = GUILayout.Toggle(adultMode, "Adult mode");
        if (GUILayout.Button("Show Age Verification", GUILayout.Height(lineHeight)))
            ShowAgeVerification();

        theaterModeEnabled = GUILayout.Toggle(theaterModeEnabled, "Theater mode enabled");
        if (GUILayout.Button("Apply Theater Mode", GUILayout.Height(lineHeight)))
            ApplyTheaterMode();

        GUILayout.Space(gap);
        GUILayout.Label("Standalone Flow", GetSectionStyle());
        if (GUILayout.Button("Standalone Login", GUILayout.Height(lineHeight)))
            StandaloneLogin();

        GUILayout.Space(gap);
        GUILayout.Label("Payments", GetSectionStyle());
        if (GUILayout.Button("Start Payment", GUILayout.Height(lineHeight)))
            StartPayment();

        GUILayout.Space(gap);
        GUILayout.Label($"Status: {_lastStatus}");
        GUILayout.EndArea();
    }

    private static string BuildIdpUrl(string baseServerUrl, string token)
    {
        var trimmed = baseServerUrl.TrimEnd('/');
        var encodedToken = Uri.EscapeDataString(token ?? string.Empty);
        return $"{trimmed}/auth/idp?token={encodedToken}";
    }

    private void LogStatus(string message)
    {
        _lastStatus = message;
        Debug.Log($"[HAppsSample] {message}");
    }

    private void LogError(string message)
    {
        _lastStatus = $"Error: {message}";
        Debug.LogError($"[HAppsSample] {message}");
    }

    private GUIStyle GetTitleStyle()
    {
        if (_titleStyle != null)
            return _titleStyle;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold
        };

        return _titleStyle;
    }

    private GUIStyle GetSectionStyle()
    {
        if (_sectionStyle != null)
            return _sectionStyle;

        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold
        };

        return _sectionStyle;
    }
}
