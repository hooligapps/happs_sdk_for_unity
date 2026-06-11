using System;
using System.Threading.Tasks;
using HAppsSDK;
using UnityEngine;

public sealed class HAppsMobileSample : MonoBehaviour
{
    [Header("OIDC")]
    [SerializeField] private string authority = "https://auth.example.com";
    [SerializeField] private string clientId = "unity-mobile-client";
    [SerializeField] private string redirectUri = "mygame://auth/callback";
    [SerializeField] private string postLogoutRedirectUri = "mygame://auth/logout";
    [SerializeField] private string scope = "openid profile offline_access";

    [Header("Debug UI")]
    [SerializeField] private bool showDebugGui = true;

    private string _lastStatus = "Idle";
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;
    private bool _isConfigured;

    private void OnEnable()
    {
        HApps.AuthCompleted += HandleWebAuthCompleted;
    }

    private void OnDisable()
    {
        HApps.AuthCompleted -= HandleWebAuthCompleted;
    }

    public void Configure()
    {
        try
        {
            HApps.ConfigureMobile(new HAppsMobileAuthOptions
            {
                Authority = authority,
                ClientId = clientId,
                RedirectUri = redirectUri,
                PostLogoutRedirectUri = postLogoutRedirectUri,
                Scope = scope
            });

            _isConfigured = true;
            LogStatus("Mobile auth configured");
        }
        catch (Exception ex)
        {
            LogError($"Configure failed: {ex}");
        }
    }

    public async void Login()
    {
        if (!EnsureConfigured())
            return;

        try
        {
            var result = await HApps.Mobile.LoginAsync();
            LogStatus($"Login: success={result.IsSuccess}, user={result.User?.userId}, error={result.Error}");
        }
        catch (Exception ex)
        {
            LogError($"Login failed: {ex}");
        }
    }

    public async void RequestProfile()
    {
        if (!EnsureConfigured())
            return;

        try
        {
            var profile = await HApps.Mobile.GetProfile();
            LogStatus($"Profile: {(profile != null ? profile.ToString() : "null")}");
        }
        catch (Exception ex)
        {
            LogError($"GetProfile failed: {ex}");
        }
    }

    public async void RefreshSession()
    {
        if (!EnsureConfigured())
            return;

        try
        {
            await HApps.Mobile.RefreshSessionAsync();
            LogStatus("Refresh session: done");
        }
        catch (Exception ex)
        {
            LogError($"Refresh failed: {ex}");
        }
    }

    public async void Logout()
    {
        if (!EnsureConfigured())
            return;

        try
        {
            await HApps.Mobile.LogoutAsync();
            LogStatus("Logout: done");
        }
        catch (Exception ex)
        {
            LogError($"Logout failed: {ex}");
        }
    }

    public void ShutdownSdk()
    {
        HApps.Shutdown();
        _isConfigured = false;
        LogStatus("SDK shutdown");
    }

    private bool EnsureConfigured()
    {
        if (_isConfigured)
            return true;

        LogError("Call Configure first");
        return false;
    }

    private void HandleWebAuthCompleted(UserData user, SignatureData signature)
    {
        Debug.Log($"[HAppsMobileSample] Web auth event: {user?.userId}, {signature?.signature}");
    }

    private void OnGUI()
    {
        if (!showDebugGui)
            return;

        const float width = 360f;
        const float lineHeight = 32f;
        const float gap = 8f;

        GUILayout.BeginArea(new Rect(16f, 16f, width, 360f), GUI.skin.box);
        GUILayout.Label("HApps Mobile Sample", GetTitleStyle());
        GUILayout.Space(gap);

        GUILayout.Label("OIDC Flow", GetSectionStyle());

        if (GUILayout.Button("Configure", GUILayout.Height(lineHeight)))
            Configure();

        if (GUILayout.Button("Login", GUILayout.Height(lineHeight)))
            Login();

        if (GUILayout.Button("Get Profile", GUILayout.Height(lineHeight)))
            RequestProfile();

        if (GUILayout.Button("Refresh Session", GUILayout.Height(lineHeight)))
            RefreshSession();

        if (GUILayout.Button("Logout", GUILayout.Height(lineHeight)))
            Logout();

        if (GUILayout.Button("Shutdown SDK", GUILayout.Height(lineHeight)))
            ShutdownSdk();

        GUILayout.Space(gap);
        GUILayout.Label($"Status: {_lastStatus}");
        GUILayout.EndArea();
    }

    private void LogStatus(string message)
    {
        _lastStatus = message;
        Debug.Log($"[HAppsMobileSample] {message}");
    }

    private void LogError(string message)
    {
        _lastStatus = $"Error: {message}";
        Debug.LogError($"[HAppsMobileSample] {message}");
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
