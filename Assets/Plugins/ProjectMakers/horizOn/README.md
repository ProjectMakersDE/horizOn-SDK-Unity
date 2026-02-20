# horizOn Cloud SDK for Unity

A complete backend solution for Unity games with user authentication, cloud saves, leaderboards, remote configuration, news, gift codes, and feedback.

**SDK Version**: v1.2.0 | **Unity Version**: 2023.3+ (Unity 6) | **Namespace**: `PM.horizOn.Cloud`

---

## Quick Start

See **[QUICKSTART.md](QUICKSTART.md)** for step-by-step setup instructions.

**TL;DR:**
1. Get API key from [horizon.pm](https://horizon.pm)
2. Download `horizOn_config.json` from SDK Settings
3. Import via **Window > horizOn > Config Importer**
4. Test via **Window > horizOn > SDK Example**

**Full API reference: [horizon.pm/docs](https://horizon.pm/docs)**

---

## Features

| Feature | Manager | Description |
|---------|---------|-------------|
| **Authentication** | `UserManager` | Anonymous, email, and Google sign-in |
| **Cloud Save** | `CloudSaveManager` | Persist game data across devices |
| **Leaderboards** | `LeaderboardManager` | Global rankings and scores |
| **Remote Config** | `RemoteConfigManager` | Dynamic settings without redeploying |
| **News** | `NewsManager` | In-game announcements |
| **Gift Codes** | `GiftCodeManager` | Promotional code redemption |
| **Feedback** | `FeedbackManager` | Bug reports and feature requests |
| **User Logs** | `UserLogManager` | Server-side logging (Pro tier) |

---

## Basic Usage

### Initialize and Connect

```csharp
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

async void Start()
{
    // Initialize SDK
    HorizonApp.Initialize();

    // Connect to server
    var server = new HorizonServer();
    await server.Connect();
}
```

### Authentication

```csharp
// Anonymous sign-up (auto-caches token for session restore)
await UserManager.Instance.SignUpAnonymous("PlayerName");

// Email sign-up
await UserManager.Instance.SignUpEmail("user@example.com", "password", "DisplayName");

// Email sign-in
await UserManager.Instance.SignInEmail("user@example.com", "password");

// Check authentication state
if (UserManager.Instance.IsSignedIn)
{
    var user = UserManager.Instance.CurrentUser;
    Debug.Log($"Welcome, {user.DisplayName}!");
}

// Sign out
UserManager.Instance.SignOut();
```

### Cloud Save

```csharp
// Define your save structure
[System.Serializable]
public class GameData
{
    public int Level;
    public int Coins;
}

// Save
var data = new GameData { Level = 5, Coins = 1000 };
await CloudSaveManager.Instance.SaveObject(data);

// Load
var loaded = await CloudSaveManager.Instance.LoadObject<GameData>();
```

### Leaderboards

```csharp
// Submit score (only updates if higher)
await LeaderboardManager.Instance.SubmitScore(12500);

// Get top players
var top = await LeaderboardManager.Instance.GetTop(10);

// Get your rank
var rank = await LeaderboardManager.Instance.GetRank();
```

### Remote Config

```csharp
// Type-safe getters with defaults
int maxLives = await RemoteConfigManager.Instance.GetInt("max_lives", 3);
bool eventActive = await RemoteConfigManager.Instance.GetBool("holiday_event", false);

// Get all configs at once
var configs = await RemoteConfigManager.Instance.GetAllConfigs();
```

### News

```csharp
var news = await NewsManager.Instance.LoadNews(limit: 10);
foreach (var item in news)
{
    Debug.Log($"{item.Title}: {item.Message}");
}
```

### Gift Codes

```csharp
// Validate first (optional)
bool? valid = await GiftCodeManager.Instance.Validate("PROMO2024");

// Redeem
var result = await GiftCodeManager.Instance.Redeem("PROMO2024");
if (result?.Success == true)
{
    // Parse result.GiftData for rewards
}
```

### Feedback

```csharp
// Bug report with auto device info
await FeedbackManager.Instance.ReportBug(
    title: "Crash on level 5",
    message: "Game crashes when opening inventory"
);

// Feature request
await FeedbackManager.Instance.RequestFeature(
    title: "Dark mode",
    message: "Please add dark mode option"
);
```

### User Logs (Pro Tier)

```csharp
await UserLogManager.Instance.Info("Tutorial completed");
await UserLogManager.Instance.Error("Save failed", errorCode: "SAVE_001");
```

---

## Rate Limiting

**Limit**: 10 requests/minute per client

### Best Practices

| Do | Don't |
|----|-------|
| Load all configs at startup | Fetch configs repeatedly |
| Cache leaderboard data | Refresh every frame |
| Save on level complete | Save on every action |
| Submit scores on improvement | Submit every score |

### Efficient Startup Pattern

```csharp
async void Start()
{
    HorizonApp.Initialize();
    await new HorizonServer().Connect();

    // Startup loads (3 requests)
    await UserManager.Instance.CheckAuth();
    await RemoteConfigManager.Instance.GetAllConfigs();
    await NewsManager.Instance.LoadNews();

    // 7 requests remaining for gameplay
}
```

---

## Error Handling

```csharp
// Check return values
bool success = await UserManager.Instance.SignInEmail(email, password);
if (!success)
{
    HorizonApp.Log.Error("Sign-in failed");
}

// Cloud save with fallback
var data = await CloudSaveManager.Instance.LoadObject<GameData>();
if (data == null)
{
    data = new GameData(); // Use defaults
}
```

### Common HTTP Status Codes

| Code | Meaning | Action |
|------|---------|--------|
| 400 | Bad Request | Check parameters |
| 401 | Unauthorized | Re-authenticate |
| 403 | Forbidden | Check tier/permissions |
| 429 | Rate Limited | Wait and retry |

---

## Event System

```csharp
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Data;

// Subscribe to events
HorizonApp.Events.Subscribe<UserData>(EventKeys.UserSignInSuccess, OnUserSignedIn);

void OnUserSignedIn(UserData user)
{
    Debug.Log($"Welcome back, {user.DisplayName}!");
}
```

Event categories: Connection (0-99), Auth (100-199), Data (200-399), Feature (400-499), Network (500-599)

---

## Tier Limits

| Feature | FREE | BASIC | PRO | ENTERPRISE |
|---------|------|-------|-----|------------|
| Cloud Save | 1 KB | 5 KB | 20 KB | 250 KB |
| User Logs | No | Yes | Yes | Yes |
| Rate Limit | 10/min | 10/min | 10/min | 10/min |

---

## Project Structure

```
Assets/Plugins/ProjectMakers/horizOn/
├── CloudSDK/
│   ├── Core/        # HorizonApp, HorizonServer, HorizonConfig
│   ├── Manager/     # Feature managers
│   ├── Service/     # EventService, NetworkService, LogService
│   ├── Objects/     # Data models, requests, responses
│   └── Resources/   # HorizonConfig.asset
├── Documentation/   # API reference
├── QUICKSTART.md    # Setup guide
└── README.md        # This file
```

---

## Documentation

- **[QUICKSTART.md](QUICKSTART.md)** - Step-by-step setup guide
- **[Documentation/UNITY_SDK_API_REFERENCE.md](Documentation/UNITY_SDK_API_REFERENCE.md)** - Complete API reference

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Configuration not found | Import via **Window > horizOn > Config Importer** |
| No active host | Call `server.Connect()` before API requests |
| Invalid API Key | Verify key at [horizon.pm](https://horizon.pm) |
| Rate limited (429) | Implement caching, reduce API calls |

---

## Support

- **Dashboard**: [horizon.pm](https://horizon.pm)
- **Issues**: [GitHub](https://github.com/ProjectMakersDE/horizOn-SDK-Unity)

---

**Made with love by ProjectMakers**
