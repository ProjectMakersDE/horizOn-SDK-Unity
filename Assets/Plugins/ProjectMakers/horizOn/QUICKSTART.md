# horizOn SDK - Quick Start Guide

Get your Unity game connected to horizOn Cloud in minutes.

## Prerequisites

- **Unity 2023.3+** (Unity 6)
- **Universal Render Pipeline (URP)**
- A horizOn account at [horizon.pm](https://horizon.pm)

---

## Step 1: Dashboard Setup

1. Go to [horizon.pm](https://horizon.pm)
2. Login or create a new account

![Dashboard Login](Documentation/images/dashboard-login.png)

---

## Step 2: Generate API Key

1. Navigate to **"API Keys"** in the Dashboard sidebar
2. Click **"+ Create Key"**
3. Enter a **clear project name** (this name is displayed in user emails, registration confirmations, etc.)
4. Click **Create**
5. **Copy and save your API key** securely (starts with `horizon_`)

![API Keys Page](Documentation/images/dashboard-api-keys.png)

> **Important**: Store your API key securely. You won't be able to see it again after leaving this page.

---

## Step 3: Download Configuration

1. Go to **"SDK Settings"** in the Dashboard sidebar
2. Click **"Download horizOn_config.json"**
3. Open the downloaded JSON file in a text editor
4. Add your API key to the `apiKey` field:

```json
{
  "apiKey": "horizon_YOUR_API_KEY_HERE",
  "backendDomains": [
    "https://horizon.pm"
  ]
}
```

![SDK Settings Page](Documentation/images/dashboard-sdk-settings.png)

---

## Step 4: Import to Unity

1. Open your Unity project
2. Click **Window > horizOn > Config Importer**
3. Click **"Browse"** and select your `horizOn_config.json` file
4. Click **"Import Configuration"**

![Unity Window Menu](Documentation/images/unity-window-menu.png)

![Config Importer](Documentation/images/unity-config-importer.png)

---

## Step 5: Verify Setup

Your configuration is now saved to:
```
Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Resources/horizOn/HorizonConfig.asset
```

You can view and modify settings by selecting this asset in the Unity Project window.

![HorizonConfig Asset](Documentation/images/unity-horizon-config.png)

---

## Step 6: Test the SDK

1. Click **Window > horizOn > SDK Example** to open the test window
2. Test authentication, cloud saves, leaderboards, and more

![SDK Example Window](Documentation/images/unity-sdk-example.png)

---

## Basic Code Example

Add this script to test SDK functionality:

```csharp
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using UnityEngine;

public class HorizonTest : MonoBehaviour
{
    async void Start()
    {
        // Step 1: Initialize SDK
        HorizonApp.Initialize();

        // Step 2: Connect to server
        var server = new HorizonServer();
        bool connected = await server.Connect();

        if (!connected)
        {
            Debug.LogError("Failed to connect to horizOn server");
            return;
        }

        Debug.Log($"Connected to: {server.ActiveHost}");

        // Step 3: Sign in or sign up
        bool signedIn = await UserManager.Instance.SignUpAnonymous("TestPlayer");

        if (signedIn)
        {
            Debug.Log($"Signed in as: {UserManager.Instance.CurrentUser.DisplayName}");
        }
    }
}
```

---

## Multiple Configurations

You can create separate configurations for different environments:

1. **Production Config**: Use your production API key
2. **Development Config**: Use a separate development API key

To switch configurations:
1. Import a new `config.json` file
2. Or manually edit `HorizonConfig.asset`

---

## Rate Limiting

> **Important**: Each client is limited to **10 requests per minute**.

Design your API calls efficiently:

| Do | Don't |
|----|-------|
| Cache data locally | Make repeated identical requests |
| Load all configs at startup | Fetch individual configs repeatedly |
| Submit scores only on improvements | Submit scores every frame |
| Use `useCache: true` parameters | Always bypass cache |

### Example: Efficient Initialization

```csharp
async void Start()
{
    HorizonApp.Initialize();
    var server = new HorizonServer();
    await server.Connect();

    // Load everything needed at startup (3 requests)
    await UserManager.Instance.CheckAuth();
    await RemoteConfigManager.Instance.GetAllConfigs();
    await NewsManager.Instance.LoadNews();

    // Remaining 7 requests available for gameplay actions
}
```

---

## Next Steps

- Read the [README.md](README.md) for feature overview
- Check [Documentation/UNITY_SDK_API_REFERENCE.md](Documentation/UNITY_SDK_API_REFERENCE.md) for complete API details
- Explore the SDK Example window for interactive testing

---

## Troubleshooting

### "Configuration not found" Error
- Ensure you've imported the configuration using **Window > horizOn > Config Importer**
- Verify `HorizonConfig.asset` exists in `CloudSDK/Resources/horizOn/`

### "No active host" Error
- Call `await server.Connect()` before making API requests
- Check that your configuration has valid host URLs

### "Invalid API Key" Error
- Verify your API key in the horizOn Dashboard
- Ensure the key starts with `horizon_`
- Re-import your configuration file

### Rate Limit (429) Errors
- Wait before retrying (check `Retry-After` header)
- Review your code for unnecessary API calls
- Implement caching strategies

---

## Support

- **Dashboard**: [horizon.pm](https://horizon.pm)
- **Documentation**: See `Documentation/` folder

---

**Made with love by ProjectMakers**
