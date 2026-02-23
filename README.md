<p align="center">
  <a href="https://horizon.pm">
    <img src="https://horizon.pm/media/images/og-image.png" alt="horizOn - Game Backend & Live-Ops Dashboard" />
  </a>
</p>

# horizOn Cloud SDK for Unity

[![Unity 2023.3+](https://img.shields.io/badge/Unity-2023.3%2B_(Unity_6)-blue?logo=unity&logoColor=white)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-1.0.0-orange)](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/releases)

Official Unity SDK for **horizOn** Backend-as-a-Service by [ProjectMakers](https://projectmakers.de).

## Features

| Feature | Manager | Description |
|---------|---------|-------------|
| 🔐 **Authentication** | `UserManager` | Anonymous, email, and Google sign-in |
| 🏆 **Leaderboards** | `LeaderboardManager` | Global rankings and scores |
| ☁️ **Cloud Save** | `CloudSaveManager` | Persist game data across devices |
| ⚙️ **Remote Config** | `RemoteConfigManager` | Dynamic settings without redeploying |
| 📰 **News** | `NewsManager` | In-game announcements |
| 🎁 **Gift Codes** | `GiftCodeManager` | Promotional code redemption |
| 💬 **Feedback** | `FeedbackManager` | Bug reports and feature requests |
| 📊 **User Logs** | `UserLogManager` | Server-side logging |
| 💥 **Crash Reporting** | `CrashManager` | Automatic crash capture, exception tracking, breadcrumbs |

## Requirements

- Unity 2023.3 or later (Unity 6)
- horizOn API key ([Get one at horizon.pm](https://horizon.pm))

## Installation

### Option 1: Unity Package (Recommended)

1. Download the latest `.unitypackage` from [Releases](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/releases)
2. Import via **Assets > Import Package > Custom Package**
3. Import all files when prompted

### Option 2: Manual Installation

1. Download the latest release from [Releases](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/releases)
2. Copy the `Assets/Plugins/ProjectMakers/horizOn` folder into your project's `Assets/Plugins/` directory

## Quick Start

> **[Quickstart Guide on horizon.pm](https://horizon.pm/quickstart#unity)** - Interactive setup guide with step-by-step instructions.

See also **[QUICKSTART.md](Assets/Plugins/ProjectMakers/horizOn/QUICKSTART.md)** for offline setup instructions.

### 1. Import Configuration

1. Get your API key from [horizon.pm](https://horizon.pm)
2. Download `horizOn_config.json` from SDK Settings
3. Import via **Window > horizOn > Config Importer**

### 2. Connect and Authenticate

```csharp
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

async void Start()
{
    HorizonApp.Initialize();
    await new HorizonServer().Connect();

    await UserManager.Instance.SignUpAnonymous("Player1");
    Debug.Log($"Welcome, {UserManager.Instance.CurrentUser.DisplayName}!");
}
```

### 3. Use SDK Features

```csharp
// Submit a score
await LeaderboardManager.Instance.SubmitScore(1000);

// Get top 10 players
var top = await LeaderboardManager.Instance.GetTop(10);

// Save game data
await CloudSaveManager.Instance.SaveObject(new GameData { Level = 5, Coins = 1000 });

// Load game data
var data = await CloudSaveManager.Instance.LoadObject<GameData>();
```

## API Reference

### Connection

```csharp
using PM.horizOn.Cloud.Core;

// Initialize and connect
HorizonApp.Initialize();
var server = new HorizonServer();
await server.Connect();

// Check status
HorizonApp.IsConnected;    // Returns true if connected
HorizonApp.ActiveHost;     // Returns current server URL
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

### Leaderboards

```csharp
// Submit score (only updates if higher)
await LeaderboardManager.Instance.SubmitScore(12500);

// Get top players
var top = await LeaderboardManager.Instance.GetTop(10);

// Get your rank
var rank = await LeaderboardManager.Instance.GetRank();

// Get players around your rank
var around = await LeaderboardManager.Instance.GetAround(5);
```

### Cloud Saves

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

### Remote Config

```csharp
// Type-safe getters with defaults
string version = await RemoteConfigManager.Instance.GetConfig("game_version");
int maxLives = await RemoteConfigManager.Instance.GetInt("max_lives", 3);
float difficulty = await RemoteConfigManager.Instance.GetFloat("difficulty", 1.0f);
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

### User Logs

```csharp
await UserLogManager.Instance.Info("Tutorial completed");
await UserLogManager.Instance.Warn("Low memory detected");
await UserLogManager.Instance.Error("Save failed", errorCode: "SAVE_001");
```

### Crash Reporting

Track crashes, non-fatal exceptions, and breadcrumbs to monitor game stability. The `CrashManager` automatically captures unhandled exceptions and Unity error logs when capture is active.

```csharp
// Start automatic crash capture (call once on game start)
// Hooks into Application.logMessageReceived and AppDomain.UnhandledException
CrashManager.Instance.StartCapture();

// Record breadcrumbs for context leading up to issues
CrashManager.Instance.RecordBreadcrumb("navigation", "Entered level 5");
CrashManager.Instance.RecordBreadcrumb("user_action", "Opened inventory");
CrashManager.Instance.Log("Player picked up item");

// Set custom metadata included in all reports
CrashManager.Instance.SetCustomKey("level", "5");
CrashManager.Instance.SetCustomKey("build", "1.2.3");

// Override user ID (defaults to authenticated user)
CrashManager.Instance.SetUserId(userId);

// Manually record a non-fatal exception
try
{
    // risky operation
}
catch (Exception e)
{
    CrashManager.Instance.RecordException(e);
}

// Record with extra metadata
CrashManager.Instance.RecordException(e, new Dictionary<string, string>
{
    { "texture_name", "player_sprite.png" }
});

// Stop capture when done
CrashManager.Instance.StopCapture();
```

#### Automatic Capture Behavior

When `StartCapture()` is called, the SDK automatically:

| Unity Log Type | Crash Report Type | Description |
|----------------|-------------------|-------------|
| `LogType.Exception` | `CRASH` | Unhandled exceptions |
| `LogType.Error` | `NON_FATAL` | Unity error logs |
| `AppDomain.UnhandledException` | `CRASH` | CLR-level unhandled exceptions |

#### Limits

| Parameter | Limit |
|-----------|-------|
| Reports per minute | 5 |
| Reports per session | 20 |
| Breadcrumbs (ring buffer) | 50 |
| Custom keys | 10 |

## Events

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

### Event Categories

| Range | Category | Key Events |
|-------|----------|------------|
| 0-99 | Connection | `ServerConnected`, `ServerDisconnected` |
| 100-199 | Auth | `UserSignInSuccess`, `UserSignInFailed`, `UserSignedOut` |
| 200-399 | Data | `CloudSaveSaved`, `CloudSaveLoaded`, `ScoreSubmitted` |
| 400-499 | Features | `CrashReported` (410), `CrashReportFailed` (411), `CrashSessionRegistered` (412) |
| 500-599 | Network | `RequestFailed`, `RateLimited` |

## Configuration Options

Edit the config asset at `Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Resources/HorizonConfig.asset` or import via **Window > horizOn > Config Importer**:

| Option | Default | Description |
|--------|---------|-------------|
| API Key | - | Your horizOn API key |
| Hosts | `["https://horizon.pm"]` | Backend server URL(s). Single host skips ping; multiple hosts use latency-based selection. |
| Connection Timeout | 10 | HTTP request timeout in seconds |
| Max Retries | 3 | Retry count for failed requests |
| Retry Delay | 1.0 | Delay between retries in seconds |
| Log Level | INFO | DEBUG, INFO, WARNING, ERROR, NONE |

## Rate Limiting

**Limit**: 10 requests per minute per client.

| Do | Don't |
|----|-------|
| Load all configs at startup | Fetch configs repeatedly |
| Cache leaderboard data | Refresh every frame |
| Save on level complete | Save on every action |
| Submit scores on improvement | Submit every score |
| Start crash capture once | Start/stop capture repeatedly |

### Efficient Startup Pattern

```csharp
async void Start()
{
    HorizonApp.Initialize();
    await new HorizonServer().Connect();

    // Startup loads (4 requests)
    await UserManager.Instance.CheckAuth();
    await RemoteConfigManager.Instance.GetAllConfigs();
    await NewsManager.Instance.LoadNews();
    CrashManager.Instance.StartCapture(); // registers session

    // 6 requests remaining for gameplay
}
```

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

## Self-Hosted Option

The horizOn SDKs work with both the **managed horizOn BaaS** and the **free, open-source [horizOn Simple Server](https://github.com/ProjectMakersDE/horizOn-simpleServer)**.

Simple Server is a lightweight PHP backend with no dependencies — perfect as a starting point if you want full control over your infrastructure. It supports core features like leaderboards, cloud saves, remote config, news, gift codes, feedback, and crash reporting.

To connect to your own server, pass your server URL when creating `HorizonServer`:

```csharp
var server = new HorizonServer("https://your-server.example.com");
await server.Connect();
```

> **Note:** Simple Server is a starting point, not a full replacement. For the complete experience with dashboard, user authentication, multi-region deployment, and more, use [horizOn BaaS](https://horizon.pm).

## Project Structure

```
Assets/Plugins/ProjectMakers/horizOn/
├── CloudSDK/
│   ├── Core/        # HorizonApp, HorizonServer, HorizonConfig
│   ├── Manager/     # Feature managers (incl. CrashManager)
│   ├── Service/     # EventService, NetworkService, LogService
│   ├── Objects/     # Data models, requests, responses
│   └── Resources/   # HorizonConfig.asset
├── Documentation/   # API reference
├── QUICKSTART.md    # Setup guide
└── README.md        # This file
```

## Documentation

- **[Quickstart Guide](https://horizon.pm/quickstart#unity)** - Interactive setup
- **[QUICKSTART.md](Assets/Plugins/ProjectMakers/horizOn/QUICKSTART.md)** - Offline setup guide
- **[API Reference](Assets/Plugins/ProjectMakers/horizOn/Documentation/UNITY_SDK_API_REFERENCE.md)** - Complete API reference
- **[horizOn Docs](https://horizon.pm/docs)** - Full documentation

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Configuration not found | Import via **Window > horizOn > Config Importer** |
| No active host | Call `server.Connect()` before API requests |
| Invalid API Key | Verify key at [horizon.pm](https://horizon.pm) |
| Rate limited (429) | Implement caching, reduce API calls |

## Support

- 📖 **Documentation**: [docs.horizon.pm](https://docs.horizon.pm)
- 💬 **Discord**: [discord.gg/horizOn](https://discord.gg/JFmaXtguku)
- 🐛 **Issues**: [GitHub Issues](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues)

## License

MIT License - Copyright (c) [ProjectMakers](https://projectmakers.de)

See [LICENSE](LICENSE) for details.
