using System;
using System.Collections.Generic;
using HAppsSDK;
using UnityEngine;

public sealed class HAppsMobileSample : MonoBehaviour
{
    [Header("Server Flow")]
    [SerializeField] private string clientId = "lustage-mobile";
    [SerializeField] private string initSessionEndpoint = "https://portal.igra.rocks/api/v1/mobile/session/init";
    [SerializeField] private string refreshSessionEndpoint = "https://portal.igra.rocks/api/v1/mobile/session/refresh";
    [SerializeField] private string createPaymentEndpoint = "https://portal.igra.rocks/api/v1/mobile/payments";

    [Header("Payment Test Data")]
    [SerializeField] private string productId = "test-product";
    [SerializeField] private string price = "1.99";
    [SerializeField] private string currency = "USD";
    [SerializeField] private string description = "Test payment";
    [SerializeField] private string requestId = "req-001";

    [Header("Debug UI")]
    [SerializeField] private bool showDebugGui = true;

    private string _lastStatus = "Idle";
    private GUIStyle _titleStyle;
    private GUIStyle _sectionStyle;
    private GUIStyle _logStyle;
    private GUIStyle _statusStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _separatorStyle;
    private bool _isConfigured;
    private bool _isInitSessionInFlight;
    private Vector2 _logScroll;
    private bool _isTouchScrollingLog;
    private bool _isMouseScrollingLog;
    private float _lastTouchY;
    private readonly List<string> _logLines = new();
    private const int MAX_LOG_LINES = 40;
    private string _lastOrderId;

    private void OnEnable()
    {
        HApps.Web.AuthCompleted += HandleWebAuthCompleted;
        Application.logMessageReceived += HandleUnityLog;
    }

    private void OnDisable()
    {
        HApps.Web.AuthCompleted -= HandleWebAuthCompleted;
        Application.logMessageReceived -= HandleUnityLog;
    }

    private void Start()
    {
        if (!_isConfigured)
            ConfigureLocal();
    }

    private void ConfigureLocal()
    {
        ResetScrollInteraction();

        try
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                LogError("clientId is empty");
                return;
            }

            HApps.ConfigureMobile(new HAppsMobileAuthOptions
            {
                Authority = "https://portal.igra.rocks/idp/oidc",
                ClientId = clientId,
                RedirectUri = "com.hooligapps.lustage://auth/callback",
                PostLogoutRedirectUri = "com.hooligapps.lustage://logout",
                Scope = "openid email offline_access",
                InitSessionUrl = initSessionEndpoint,
                RefreshSessionUrl = refreshSessionEndpoint,
                CreatePaymentUrl = createPaymentEndpoint
            });

            _isConfigured = true;
            LogStatus($"Configured locally: {clientId}");
            StartInitSession();
        }
        catch (Exception ex)
        {
            LogError($"Local configure failed: {ex}");
        }
    }

    public async void Login()
    {
        if (!EnsureConfigured())
            return;

        ResetScrollInteraction();
        try
        {
            AddLogSeparator("LOGIN");
            LogStatus("Starting mobile login");
            var result = await HApps.Mobile.LoginAsync();
            if (!result.IsSuccess)
            {
                LogError($"Login failed: {result.Error}");
                return;
            }

            LogStatus($"Login callback received: accessTokenLength={result.AccessToken?.Length ?? 0}, refreshTokenLength={result.RefreshToken?.Length ?? 0}, scope={result.Scope}");
        }
        catch (Exception ex)
        {
            LogError($"Login failed: {ex}");
        }
    }

    private async void StartInitSession()
    {
        if (!_isConfigured || _isInitSessionInFlight)
            return;

        ResetScrollInteraction();
        _isInitSessionInFlight = true;
        try
        {
            AddLogSeparator("INIT SESSION");
            LogStatus("Starting initSession");
            var session = await HApps.Mobile.InitSessionAsync();
            LogStatus($"initSession: publicId={session.PublicId}, verified={session.Verified}, accessTokenLength={session.AccessToken?.Length ?? 0}, refreshTokenLength={session.RefreshToken?.Length ?? 0}");
        }
        catch (Exception ex)
        {
            LogError($"initSession failed: {ex}");
        }
        finally
        {
            _isInitSessionInFlight = false;
        }
    }

    private async void RefreshSession()
    {
        if (!EnsureConfigured())
            return;

        ResetScrollInteraction();
        try
        {
            AddLogSeparator("REFRESH SESSION");
            LogStatus("Starting refreshSession");
            var session = await HApps.Mobile.RefreshSessionAsync();
            LogStatus($"refreshSession: publicId={session.PublicId}, verified={session.Verified}, accessTokenLength={session.AccessToken?.Length ?? 0}, refreshTokenLength={session.RefreshToken?.Length ?? 0}");
        }
        catch (Exception ex)
        {
            LogError($"refreshSession failed: {ex}");
        }
    }

    public async void CreatePayment()
    {
        if (!EnsureConfigured())
            return;

        ResetScrollInteraction();
        try
        {
            if (!decimal.TryParse(price, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice))
            {
                LogError($"Invalid price: {price}");
                return;
            }

            requestId = $"req-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            AddLogSeparator("CREATE PAYMENT");
            LogStatus($"Starting createPayment: productId={productId}, price={parsedPrice}, currency={currency}, requestId={requestId}");
            var result = await HApps.Mobile.CreatePaymentAsync(new MobileCreatePaymentRequest
            {
                ProductId = productId,
                Price = parsedPrice,
                Currency = currency,
                Description = description,
                RequestId = requestId
            });

            _lastOrderId = result.OrderId;
            LogStatus($"createPayment: orderId={result.OrderId}, paymentUrl={result.PaymentUrl}");
        }
        catch (Exception ex)
        {
            LogError($"createPayment failed: {ex}");
        }
    }

    public async void Logout()
    {
        if (!EnsureConfigured())
            return;

        ResetScrollInteraction();
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

    public void ClearLog()
    {
        ResetScrollInteraction();
        _logLines.Clear();
        _lastStatus = "Idle";
        Debug.Log("[HAppsMobileSample] Log cleared");
    }

    private bool EnsureConfigured()
    {
        if (_isConfigured)
            return true;

        ConfigureLocal();
        return _isConfigured;
    }

    private void HandleWebAuthCompleted(UserData user, SignatureData signature)
    {
        Debug.Log($"[HAppsMobileSample] Web auth event: {user?.userId}, {signature?.signature}");
    }

    private void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        if (!condition.StartsWith("[HApps", StringComparison.Ordinal))
            return;

        AppendExternalLog(condition);
    }

    private void OnGUI()
    {
        if (!showDebugGui)
            return;

        var scale = Mathf.Max(1.6f, Screen.dpi > 0f ? Screen.dpi / 150f : 1.8f);
        var margin = 24f * scale;
        var width = Mathf.Min(Screen.width - margin * 2f, 900f * scale);
        var lineHeight = 48f * scale;
        var gap = 10f * scale;
        var areaHeight = Screen.height - margin * 2f;
        var logHeight = Mathf.Max(380f * scale, areaHeight * 0.42f);

        GUILayout.BeginArea(new Rect(margin, margin, width, areaHeight), GUI.skin.box);
        GUILayout.Label("HApps Mobile Sample", GetTitleStyle());
        GUILayout.Space(gap);

        GUILayout.Label("Portal Session Flow", GetSectionStyle());

        DrawButtonRow(lineHeight,
            ("Login", Login),
            ("Create Payment", CreatePayment));

        if (GUILayout.Button("Logout", GetButtonStyle(), GUILayout.Height(lineHeight)))
            Logout();

        if (GUILayout.Button("Clear Log", GetButtonStyle(), GUILayout.Height(lineHeight)))
            ClearLog();

        GUILayout.Space(gap);
        GUILayout.Label($"Status: {_lastStatus}", GetStatusStyle());
        GUILayout.Space(gap);
        GUILayout.Label("Log", GetSectionStyle());
        var logRect = GUILayoutUtility.GetRect(width - 40f * scale, logHeight, GUILayout.Height(logHeight));
        var absoluteLogRect = new Rect(margin + logRect.x, margin + logRect.y, logRect.width, logRect.height);
        DrawLogScrollView(logRect, absoluteLogRect);
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
        _logLines.Insert(0, line);

        while (_logLines.Count > MAX_LOG_LINES)
            _logLines.RemoveAt(_logLines.Count - 1);

        _logScroll.y = 0f;
    }

    private void AddLogSeparator(string title)
    {
        _logLines.Insert(0, $"---- {title} ----");

        while (_logLines.Count > MAX_LOG_LINES)
            _logLines.RemoveAt(_logLines.Count - 1);

        _logScroll.y = 0f;
    }

    private void AppendExternalLog(string message)
    {
        _logLines.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");

        while (_logLines.Count > MAX_LOG_LINES)
            _logLines.RemoveAt(_logLines.Count - 1);

        _logScroll.y = 0f;
    }

    private void ScrollToBottom()
    {
        ResetScrollInteraction();
        _logScroll.y = 0f;
    }

    private void DrawButtonRow(float lineHeight, (string label, Action action) left, (string label, Action action) right)
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button(left.label, GetButtonStyle(), GUILayout.Height(lineHeight), GUILayout.ExpandWidth(true)))
            left.action?.Invoke();

        if (GUILayout.Button(right.label, GetButtonStyle(), GUILayout.Height(lineHeight), GUILayout.ExpandWidth(true)))
            right.action?.Invoke();

        GUILayout.EndHorizontal();
    }

    private void ResetScrollInteraction()
    {
        _isTouchScrollingLog = false;
        _isMouseScrollingLog = false;
        _lastTouchY = 0f;
    }

    private void DrawLogScrollView(Rect logRect, Rect absoluteLogRect)
    {
        var totalHeight = 8f;
        var contentWidth = Mathf.Max(0f, logRect.width - 24f);

        foreach (var line in _logLines)
        {
            var style = line.StartsWith("---- ") ? GetSeparatorStyle() : GetLogStyle();
            totalHeight += style.CalcHeight(new GUIContent(line), contentWidth) + 6f;
        }

        var contentHeight = Mathf.Max(logRect.height, totalHeight);
        var maxScrollY = Mathf.Max(0f, contentHeight - logRect.height);
        if (_logScroll.y > maxScrollY)
            _logScroll.y = maxScrollY;

        HandleContentScroll(logRect, absoluteLogRect, maxScrollY);

        GUI.Box(logRect, GUIContent.none);
        GUI.BeginGroup(logRect);
        GUI.BeginGroup(new Rect(0f, -_logScroll.y, logRect.width, contentHeight));

        var y = 6f;
        foreach (var line in _logLines)
        {
            var lineStyle = line.StartsWith("---- ") ? GetSeparatorStyle() : GetLogStyle();
            var height = lineStyle.CalcHeight(new GUIContent(line), contentWidth - 8f);
            GUI.Label(new Rect(4f, y, contentWidth - 8f, height), line, lineStyle);
            y += height + 6f;
        }

        GUI.EndGroup();
        GUI.EndGroup();
    }

    private void HandleContentScroll(Rect localLogRect, Rect absoluteLogRect, float maxScrollY)
    {
        var currentEvent = Event.current;
        if (currentEvent != null)
        {
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (localLogRect.Contains(currentEvent.mousePosition))
                    {
                        _isMouseScrollingLog = true;
                        _lastTouchY = currentEvent.mousePosition.y;
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isMouseScrollingLog)
                    {
                        var deltaY = currentEvent.mousePosition.y - _lastTouchY;
                        _logScroll.y = Mathf.Clamp(_logScroll.y - deltaY, 0f, maxScrollY);
                        _lastTouchY = currentEvent.mousePosition.y;
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    _isMouseScrollingLog = false;
                    break;

                case EventType.MouseLeaveWindow:
                case EventType.Ignore:
                case EventType.Used:
                    _isMouseScrollingLog = false;
                    break;
            }
        }

        if (Input.touchCount <= 0)
        {
            _isTouchScrollingLog = false;
            return;
        }

        var touch = Input.GetTouch(0);
        var touchPosition = new Vector2(touch.position.x, Screen.height - touch.position.y);

        switch (touch.phase)
        {
            case TouchPhase.Began:
                if (absoluteLogRect.Contains(touchPosition))
                {
                    _isTouchScrollingLog = true;
                    _lastTouchY = touchPosition.y;
                }
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (_isTouchScrollingLog)
                {
                    var deltaY = touchPosition.y - _lastTouchY;
                    _logScroll.y = Mathf.Clamp(_logScroll.y - deltaY, 0f, maxScrollY);
                    _lastTouchY = touchPosition.y;
                }
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                _isTouchScrollingLog = false;
                _lastTouchY = 0f;
                break;
        }
    }

    private GUIStyle GetTitleStyle()
    {
        if (_titleStyle != null)
            return _titleStyle;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 30
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
            fontSize = 24
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
            fontSize = 22
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
            fontSize = 22,
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
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };

        return _buttonStyle;
    }

    private GUIStyle GetSeparatorStyle()
    {
        if (_separatorStyle != null)
            return _separatorStyle;

        _separatorStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        return _separatorStyle;
    }
}
