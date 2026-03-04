using UnityEngine;
using HAppsSDK;

public class GameManager : MonoBehaviour
{
    private async void Start()
    {
        HApps.OnUserChanged += OnUserChanged;
        HApps.OnVisibilityChanged += OnVisibilityChanged;

        var ready = await HApps.Initialize();
        Debug.Log($"[Game] HApps initialized: {ready}, user: {HApps.CurrentUser}");

        if (HApps.IsLoggedIn)
        {
            Debug.Log($"[Game] Logged in as {HApps.CurrentUser}");
        }
    }

    private void OnUserChanged(UserData user)
    {
        Debug.Log($"[Game] User changed: {user}");
    }

    private void OnVisibilityChanged(bool visible)
    {
        Debug.Log($"[Game] Visibility: {visible}");
        AudioListener.pause = !visible;
    }

    private void OnDestroy()
    {
        HApps.OnUserChanged -= OnUserChanged;
        HApps.OnVisibilityChanged -= OnVisibilityChanged;
    }
}
