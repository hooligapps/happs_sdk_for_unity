using System;
using System.Collections.Generic;
using HAppsSDK;
using UnityEngine;
using UnityEngine.Networking;

public sealed class HAppsMobileSample : MonoBehaviour
{
    [Header("Server Flow")]
    [SerializeField] private string configEndpoint = "https://portal.igra.rocks/sandbox/mobile/config";
    [SerializeField] private string gameLoginEndpoint = "https://portal.igra.rocks/sandbox/mobile/game-login";
    [SerializeField] private string clientId = "lustage-mobile";

    [Header("Debug UI")]
    [SerializeField] private bool showDebugGui = true;

    private string _lastStatus = "Idle";
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _logStyle;
    private GUIStyle _statusStyle;
    private GUIStyle _buttonStyle;
    private bool _isConfigured;
    private Vector2 _logScroll;
    private readonly List<string> _logLines = new();
    private const int MAX_LOG_LINES = 40;

    private void OnEnable()
    {
        HApps.AuthCompleted += HandleWebAuthCompleted;
    }

    private void OnDisable()
    {
        HApps.AuthCompleted -= HandleWebAuthCompleted;
    }

    public async void FetchConfigAndConfigure()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                LogError("clientId is empty");
                return;
            }

            var url = $"{configEndpoint}?clientId={UnityWebRequest.EscapeURL(clientId)}";
            LogStatus($"Fetching config: {url}");
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"Config request failed: {request.error}");
                return;
            }

            var config = JsonUtility.FromJson<MobileConfigResponse>(request.downloadHandler.text);
            if (config == null || string.IsNullOrWhiteSpace(config.authority))
            {
                LogError("Config response is invalid");
                return;
            }

            LogStatus($"Config response: authority={config.authority}, clientId={config.clientId}, redirectUri={config.redirectUri}, logoutRedirectUri={config.postLogoutRedirectUri}, scope={config.scope}");

            HApps.ConfigureMobile(new HAppsMobileAuthOptions
            {
                Authority = config.authority,
                ClientId = config.clientId,
                RedirectUri = config.redirectUri,
                PostLogoutRedirectUri = config.postLogoutRedirectUri,
                Scope = config.scope
            });

            clientId = config.clientId;
            _isConfigured = true;
            LogStatus($"Configured: {config.clientId}");
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
            LogStatus("Starting mobile login");
            var result = await HApps.Mobile.LoginAsync();
            if (!result.IsSuccess)
            {
                LogError($"Login failed: {result.Error}");
                return;
            }

            LogStatus($"Login callback received: accessTokenLength={result.AccessToken?.Length ?? 0}, refreshTokenLength={result.RefreshToken?.Length ?? 0}, scope={result.Scope}");
            LogStatus($"POST game-login: {gameLoginEndpoint}");
            var authorizationHeader = $"Bearer {result.AccessToken}";
            LogStatus($"game-login Authorization: {authorizationHeader}");
            using var request = new UnityWebRequest(gameLoginEndpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", authorizationHeader);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogError($"game-login failed: {request.error}");
                return;
            }

            LogStatus($"game-login: {request.downloadHandler.text}");
        }
        catch (Exception ex)
        {
            LogError($"Login failed: {ex}");
        }
    }

    public async void Logout()
    {
        if (!EnsureConfigured())
            return;

        try
        {
            LogStatus("Starting logout");
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

    public void DumpCurrentState()
    {
        LogStatus($"State: configured={_isConfigured}, clientId={clientId}, configEndpoint={configEndpoint}, gameLoginEndpoint={gameLoginEndpoint}");
    }

    public void ClearLog()
    {
        _logLines.Clear();
        _lastStatus = "Idle";
        Debug.Log("[HAppsMobileSample] Log cleared");
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

        var scale = Mathf.Max(1.6f, Screen.dpi > 0f ? Screen.dpi / 150f : 1.8f);
        var margin = 24f * scale;
        var width = Mathf.Min(Screen.width - margin * 2f, 900f * scale);
        var lineHeight = 44f * scale;
        var gap = 10f * scale;
        var areaHeight = Screen.height - margin * 2f;
        var logHeight = Mathf.Max(320f * scale, areaHeight * 0.38f);

        GUILayout.BeginArea(new Rect(margin, margin, width, areaHeight), GUI.skin.box);
        GUILayout.Label("HApps Mobile Sample", GetTitleStyle());
        GUILayout.Space(gap);

        GUILayout.Label("Mobile Login Flow", GetSectionStyle());

        if (GUILayout.Button("Fetch Config", GetButtonStyle(), GUILayout.Height(lineHeight)))
            FetchConfigAndConfigure();

        if (GUILayout.Button("Login", GetButtonStyle(), GUILayout.Height(lineHeight)))
            Login();

        if (GUILayout.Button("Logout", GetButtonStyle(), GUILayout.Height(lineHeight)))
            Logout();

        if (GUILayout.Button("Dump State", GetButtonStyle(), GUILayout.Height(lineHeight)))
            DumpCurrentState();

        if (GUILayout.Button("Clear Log", GetButtonStyle(), GUILayout.Height(lineHeight)))
            ClearLog();

        if (GUILayout.Button("Shutdown SDK", GetButtonStyle(), GUILayout.Height(lineHeight)))
            ShutdownSdk();

        GUILayout.Space(gap);
        GUILayout.Label($"Status: {_lastStatus}", GetStatusStyle());
        GUILayout.Space(gap);
        GUILayout.Label("Log", GetSectionStyle());
        _logScroll = GUILayout.BeginScrollView(_logScroll, GUI.skin.box, GUILayout.Height(logHeight));
        foreach (var line in _logLines)
            GUILayout.Label(line, GetLogStyle());
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void LogStatus(string message)
    {
        _lastStatus = message;
        AppendLog(message);
        Debug.Log($"[HAppsMobileSample] {message}");
    }

    private void LogError(string message)
    {
        _lastStatus = $"Error: {message}";
        AppendLog($"ERROR: {message}");
        Debug.LogError($"[HAppsMobileSample] {message}");
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        _logLines.Add(line);

        while (_logLines.Count > MAX_LOG_LINES)
            _logLines.RemoveAt(0);

        _logScroll.y = float.MaxValue;
    }

    private GUIStyle GetTitleStyle()
    {
        if (_titleStyle != null)
            return _titleStyle;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 28
        };

        return _titleStyle;
    }

    private GUIStyle GetSectionStyle()
    {
        if (_sectionStyle != null)
            return _sectionStyle;

        _sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 22
        };

        return _sectionStyle;
    }

    private GUIStyle GetLogStyle()
    {
        if (_logStyle != null)
            return _logStyle;

        _logStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            richText = false,
            fontSize = 20
        };

        return _logStyle;
    }

    private GUIStyle GetStatusStyle()
    {
        if (_statusStyle != null)
            return _statusStyle;

        _statusStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };

        return _statusStyle;
    }

    private GUIStyle GetButtonStyle()
    {
        if (_buttonStyle != null)
            return _buttonStyle;

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };

        return _buttonStyle;
    }

    [Serializable]
    private sealed class MobileConfigResponse
    {
        public string authority;
        public string clientId;
        public string redirectUri;
        public string postLogoutRedirectUri;
        public string scope;
    }
}
