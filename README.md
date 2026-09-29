<p align="center">
  <a href="https://horizon.pm">
    <img src="https://horizon.pm/media/images/og-image.png" alt="horizOn - Game Backend & Live-Ops Dashboard" />
  </a>
</p>

# horizOn Cloud SDK for Unity

[![Unity 6](https://img.shields.io/badge/Unity-6000.0%2B-blue?logo=unity&logoColor=white)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)
[![Version](https://img.shields.io/badge/version-1.8.5-orange)](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/releases)

Official Unity SDK for **horizOn** Backend-as-a-Service by [ProjectMakers](https://projectmakers.de).

## Features

| Feature | Manager | Description |
|---------|---------|-------------|
| 🔐 **Authentication** | `UserManager` | Anonymous, email, Google and Apple sign-in |
| 🏆 **Leaderboards** | `LeaderboardManager` | Global rankings and scores |
| ☁️ **Cloud Save** | `CloudSaveManager` | Persist game data across devices |
| ⚙️ **Remote Config** | `RemoteConfigManager` | Dynamic settings without redeploying |
| 🌍 **Localization** | `LocalizationManager` | Multi-language strings fetched at runtime (15 languages) |
| 📰 **News** | `NewsManager` | In-game announcements |
| 🎁 **Gift Codes** | `GiftCodeManager` | Promotional code redemption, cosmetic unlocks |
| 🪪 **Player Profile** | `PlayerProfileManager` | Avatar, frame and badges shown on leaderboards |
| 🛡️ **Validated Actions** | `ValidatedActionsManager` | Server-checked runs: single-use tickets, server seed, rules before any score is written, server-owned currency and loot, input log evidence |
| 💬 **Feedback** | `FeedbackManager` | Bug reports and feature requests |
| 📊 **User Logs** | `UserLogManager` | Server-side logging |
| 💥 **Crash Reporting** | `CrashManager` | Automatic crash capture, exception tracking, breadcrumbs |
| ✉️ **Email Sending** | `EmailSendingManager` | Transactional emails with templates, scheduling, multi-language |

## Requirements

- Unity 6 (6000.0) or later
- horizOn API key ([Get one at horizon.pm](https://horizon.pm))

## Installation

### Option 1: Package Manager via Git URL (Recommended)

1. In Unity, open **Window > Package Manager**
2. Click the **+** button and choose **Add package from git URL**
3. Enter `https://github.com/ProjectMakersDE/horizOn-SDK-Unity.git` and click **Add**

To pin a specific release, append the matching tag from the
[Releases](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/releases) page, e.g.
`https://github.com/ProjectMakersDE/horizOn-SDK-Unity.git#vX.Y.Z`.

The optional **Examples** and **Example UI** samples can be imported afterwards from
**Package Manager > horizOn Cloud SDK > Samples**.

### Option 2: Unity Package (.unitypackage)

1. Download the latest `.unitypackage` from [Releases](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/releases)
2. Import via **Assets > Import Package > Custom Package**
3. Import all files when prompted

### Option 3: Manual Installation

1. Download or clone this repository
2. Copy the whole repository into your project's `Packages/` directory, or copy the `CloudSDK` folder into `Assets/`

## Quick Start

> **[Quickstart Guide on horizon.pm](https://horizon.pm/quickstart#unity)** - Interactive setup guide with step-by-step instructions.

See also **[QUICKSTART.md](QUICKSTART.md)** for offline setup instructions.

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

## Hello horizOn

`HelloHorizon` is the smallest end-to-end entry point for the SDK. It initializes,
connects, signs up an anonymous user, submits a score, and shows the resulting rank.

1. Import the horizOn SDK package.
2. Import the **Examples** sample via **Package Manager > horizOn Cloud SDK > Samples**.
3. Set your API key via **Window > horizOn > Config Importer**.
4. Add the `HelloHorizon` component to an empty GameObject in a scene and press Play.

Script: `HelloHorizon.cs` (under `Assets/Samples/horizOn Cloud SDK/<version>/Examples/HelloHorizon/` after importing the sample).
Optionally assign a UI Text element to mirror the result on screen.

## Examples

Per-feature minimal example scripts ship as the **Examples** sample. Import it via
**Package Manager > horizOn Cloud SDK > Samples**, then find them under
`Assets/Samples/horizOn Cloud SDK/<version>/Examples/Features/`. Each is a small,
copy-paste friendly MonoBehaviour with a header comment and try/catch error handling.
Attach one to an empty GameObject and press Play.

| Feature | Example script |
|---------|----------------|
| Authentication | `AuthExample.cs` |
| Leaderboards | `LeaderboardExample.cs` |
| Cloud Save | `CloudSaveExample.cs` |
| Crash Reporting | `CrashReportingExample.cs` |
| User Logs | `UserLogsExample.cs` |
| Remote Config | `RemoteConfigExample.cs` |
| Localization | `LocalizationExample.cs` |
| News | `NewsExample.cs` |
| Email Sending | `EmailSendingExample.cs` |
| Gift Codes | `GiftCodesExample.cs` |
| Player Profile | `PlayerProfileExample.cs` |
| Validated Actions | `ValidatedActionsExample.cs`, `ValidatedStateExample.cs` (server-owned values) |
| Feedback | `FeedbackExample.cs` |

For a full guided tour of every feature in one window, import the **Example UI** sample.

## API Reference

### Connection

```csharp
using PM.horizOn.Cloud.Core;

// Initialize and connect
HorizonApp.Initialize();
var server = new HorizonServer();
await server.Connect();

// Check status (properties live on the HorizonServer instance)
server.IsConnected;    // Returns true if connected
server.ActiveHost;     // Returns current server URL
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

### Sign in with Apple

App-Store-ready authentication that respects user privacy. On iOS the SDK opens the
native ASAuthorizationController sheet via the bundled AuthenticationServices plugin.
On Android, Standalone (Windows / Linux / macOS) and the Editor it falls back to a
system-browser OAuth redirect using the Apple Services ID configured in
`HorizonConfig`.

```csharp
// One-call end-to-end flow: native sheet on iOS, web fallback elsewhere.
// Internally calls SignInApple, falls through to SignUpApple on USER_NOT_FOUND.
bool signedIn = await UserManager.Instance.SignInWithApple();

// Low-level overloads if your game already has its own Apple plugin and
// has the identityToken in hand.
await UserManager.Instance.SignUpApple(identityToken, firstName, lastName);
await UserManager.Instance.SignInApple(identityToken);

// Listen for results via the event bus, exactly like Google sign-in.
HorizonApp.Events.Subscribe<UserData>(EventKeys.UserSignInSuccess, user =>
{
    Debug.Log($"Apple user {user.UserId} signed in (relay email: {user.IsPrivateRelayEmail})");
});
HorizonApp.Events.Subscribe<string>(EventKeys.UserSignInFailed, code =>
{
    // Canonical codes: INVALID_APPLE_TOKEN, APPLE_NOT_CONFIGURED,
    // APPLE_EMAIL_CONFLICT, USER_NOT_FOUND, USER_ALREADY_EXISTS, NETWORK_ERROR.
    Debug.LogError($"Apple sign-in failed: {code}");
});
```

For the non-iOS fallback you must set `AppleServicesId` and `AppleRedirectUri` on
`HorizonConfig`, register the redirect URI on the Apple Developer Portal, and
forward the `id_token` from your custom redirect handler back into the SDK via
`HorizonAppleSignInBridge.DeliverWebToken(identityToken, firstName, lastName)`.

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

// Every entry (and the rank) carries the player's profile, never null
foreach (var entry in top)
{
    if (entry.profile.HasAvatar) { /* show entry.profile.avatarId */ }
}
```

Use `boardKey:` as a named argument for multi-board leaderboards:
`SubmitScore(12500, boardKey: "weekly")`. The `metadata` parameter of `SubmitScore`
is deprecated and ignored: the server never stored score metadata, so the SDK does
not send it. It will be removed in the next major version.

When `SubmitScore` returns `false`, `LeaderboardManager.Instance.LastErrorCode` tells why.
`VALIDATED_SUBMIT_REQUIRED` means the board only accepts validated runs (see Validated
Actions); it is not retried. `ListBoards()` returns `validatedOnly` for every board.

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

### Localization

```csharp
// Pick a language (one of the 15 supported codes); defaults to the device
// language, or "en" when that is not supported. Changing it clears the cache.
LocalizationManager.Instance.SetLanguage("de");

// Single key in the current language; returns null when the key is missing
string play = await LocalizationManager.Instance.GetLocalization("menu.play");

// Override the language per call
string playEn = await LocalizationManager.Instance.GetLocalization("menu.play", "en");

// Get every translation for a language at once
var translations = await LocalizationManager.Instance.GetAllLocalizations();

// List the languages the backend has translations for
string[] languages = await LocalizationManager.Instance.GetAvailableLanguages();
```

### News

```csharp
var news = await NewsManager.Instance.LoadNews(limit: 10);
foreach (var item in news)
{
    Debug.Log($"{item.title}: {item.message}");
}
```

### Gift Codes

```csharp
// Validate first (optional)
bool? valid = await GiftCodeManager.Instance.Validate("PROMO2024");

// Redeem
var result = await GiftCodeManager.Instance.Redeem("PROMO2024");
if (result?.success == true)
{
    // Parse result.giftData for rewards
    // result.grantedUnlocks lists cosmetics the code unlocked (see Player Profile)
}
```

### Player Profile

Leaderboards show an avatar, an optional frame and up to 3 badges next to name and
score. You maintain a cosmetics catalog per API key in the horizOn Dashboard (ID,
type `avatar` / `frame` / `badge`, `locked`). Free entries can be picked by every
player, locked ones only after an unlock (gift code with `grants`, or the Dashboard).
The server stores IDs only; your game maps them to its own sprites. Both calls need a
signed-in player.

```csharp
// Profile, unlocks and the full catalog in one call
var profile = await PlayerProfileManager.Instance.GetProfile();
var avatars = profile.GetCosmetics("avatar");        // build your picker
bool canUse = profile.IsAvailable("frame.gold");     // free or unlocked

// PUT replaces the whole profile: pass the current values for slots you keep.
// null or "" clears a slot, null or an empty list clears the badges (max 3).
var updated = await PlayerProfileManager.Instance.SetProfile(
    "avatar.zombie_07", profile.profile.frameId, new[] { "badge.supporter" });
if (updated == null)
{
    // SESSION_REQUIRED, COSMETIC_LOCKED, COSMETIC_NOT_FOUND, INVALID_BADGES, ...
    Debug.Log(PlayerProfileManager.Instance.LastErrorCode);
}
```

`CurrentProfile` holds the last result (null after sign-out). After a gift code redemption
with a non-empty `grantedUnlocks`, the SDK drops it so the next `GetProfile()` shows the
unlock. Error codes are listed in `PlayerProfileErrorCodes`.

### Validated Actions

The server checks a run before it writes anything. Start a run to get a single-use ticket
and a server seed, play deterministically with that seed while you record the player's
inputs, then submit the score with the input log. The SDK sends the SHA-256 of the log; the
server checks the ticket, measures the duration itself and applies the rules of the API key
(score limits, minimum duration, score per second, stage rules). Rules never reach the
client. Leaderboards set to "Validated submissions only" accept scores only this way.
Every call needs a signed-in player. Cloud only: a self-hosted simpleServer answers
`NOT_SUPPORTED`.

```csharp
var run = await ValidatedActionsManager.Instance.StartRun("weekly");
if (run == null)
{
    Debug.Log(ValidatedActionsManager.Instance.LastErrorCode); // RUN_RATE_LIMITED, ...
    return;
}
var random = new System.Random(run.seed);   // deterministic gameplay
// ... play, record the inputs into byte[] inputLog ...

var result = await ValidatedActionsManager.Instance.SubmitValidated(18250, inputLog);
if (result == null)
{
    // DURATION_TOO_SHORT, SCORE_ABOVE_MAX, TICKET_EXPIRED, SESSION_REQUIRED, ...
    Debug.Log(ValidatedActionsManager.Instance.LastErrorCode);
}
else
{
    Debug.Log($"Rank {result.rank}, best {result.bestScore}");
}
```

A ticket is single use: after a success, a 422 rejection (except `LEADERBOARD_MISMATCH`)
and `SCORE_LIMIT_REACHED` the SDK drops `CurrentRun`. After a network error, 401, 404, 429
or 5xx the run stays and you may call `SubmitValidated` again. `RUN_RATE_LIMITED` and
`RUN_CAPACITY_REACHED` are not retried automatically. Use `SubmitValidatedWithHash(score,
hash)` when you hash the log yourself (`ValidatedActionsManager.ComputeInputLogHash(bytes)`),
and `DiscardRun()` when the player quits. Error codes are listed in
`ValidatedActionsErrorCodes`. A player banned from the board gets `PLAYER_BANNED` (403) from
`SubmitValidated` and from `LeaderboardManager.SubmitScore`; the validated run is kept (the ban
is checked before the ticket is used), but the same board refuses it again, so call
`DiscardRun()`.

#### Evidence (input log upload)

When a run becomes a new top entry (the board's "Evidence top N") or carries a soft flag, the
server asks for its input log: `result.evidence.required` is true, with `runId`, `uploadBefore`
(24 h) and `maxBytes` (32,768). After `SubmitValidated` the SDK uploads the raw log on its own in
the background (`AutoUploadEvidence`, default true). After `SubmitValidatedWithHash`, or with
auto upload off, upload the exact bytes you hashed yourself:

```csharp
if (result.evidence.required)
{
    bool stored = await ValidatedActionsManager.Instance.UploadEvidence(result.evidence.runId, inputLog);
    if (!stored && ValidatedActionsManager.IsEvidenceRetryable(ValidatedActionsManager.Instance.LastErrorCode))
    {
        // EVIDENCE_HASH_MISMATCH (send the right bytes) or NETWORK_ERROR: try again later
    }
}

HorizonApp.Events.Subscribe<ValidatedEvidenceFailure>(EventKeys.ValidatedEvidenceUploadFailed,
    failure => Debug.Log($"{failure.code}, retryable {failure.retryable}"));
```

The upload never changes the submit result: the run stays accepted. Outcomes arrive as
`EventKeys.ValidatedEvidenceUploaded` (423) and `EventKeys.ValidatedEvidenceUploadFailed` (424);
`LastEvidenceErrorCode` holds the code of the last failed upload. The automatic upload never
sets `LastErrorCode`. Codes: `EVIDENCE_HASH_MISMATCH` (422, the request stays open, retry with the
exact bytes), `EVIDENCE_INVALID_ENCODING` (400), `EVIDENCE_NOT_REQUESTED` (404),
`EVIDENCE_ALREADY_UPLOADED` (409), `EVIDENCE_EXPIRED` (410), `EVIDENCE_TOO_LARGE` (413, or local
when the log exceeds `maxBytes`); only the hash mismatch and `NETWORK_ERROR` are worth a retry.
Local codes: `SESSION_REQUIRED`, `INVALID_RUN_ID`, `EMPTY_INPUT_LOG`.

#### Server-owned values (player state)

Currency and loot counters defined under `values` in the rules of the API key are written only
by the server. A run reports what it earned (positive amount) or spent (negative amount) with
`earned`; the server checks the per-run limits and the balance, then credits with the daily cap
and the maximum balance applied. There is no method that writes the state.

```csharp
// Read on start (every key of the rules, sorted, balance 0 when never earned)
PlayerState state = await ValidatedActionsManager.Instance.GetState();
long gold = state?.GetBalance("gold") ?? 0;

// Earn or spend inside a validated run
var result = await ValidatedActionsManager.Instance.SubmitValidated(score, inputLog,
    earned: new[] { new EarnedValue("gold", 250), new EarnedValue("chest.gold", -1) });
if (result != null)
{
    PlayerStateValue credit = result.state.GetValue("gold");
    Debug.Log($"+{credit.credited} of {credit.requested}, balance {credit.balance}");
    bool chestPaid = result.state.GetValue("chest.gold").IsFullyCredited; // grant only when true
}
```

`CurrentState` holds the last known state (from `GetState` or the last accepted submit that
carried a state; `null` after sign-out); both publish `EventKeys.ValidatedStateLoaded` (308).
`credited` lower than `requested` means a cap clamped a credit; a spend is either fully applied or
0, so grant a purchase only when `IsFullyCredited`. Value rejections (`UNKNOWN_VALUE_KEY`,
`DUPLICATE_VALUE_KEY`, `EARNED_ABOVE_MAX`, `EARNED_BELOW_MIN`, `INSUFFICIENT_BALANCE`) are 422
and use up the ticket. Send `earned` only when the rules define values: an unknown key rejects
the run.

**Cloud save as a mirror.** `PlayerState` is `[Serializable]`, so you may keep a copy in your
cloud save for display and offline start. Copy `CurrentState` into the save after each accepted
run, overwrite the copy with `GetState()` on start (never the other way round), never send a
value from the save back as a balance, and send values earned offline as `earned` of the next
validated run. See `ValidatedStateExample.cs`.

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

### Email Sending

**Email Sending** lets your game send transactional emails to registered players. Create multi-language HTML templates with variable placeholders in the horizOn Dashboard, then trigger immediate or scheduled email delivery from your game using the SDK. Emails are sent through your own SMTP server -- horizOn handles the queue, rendering, and scheduling while you keep full control over branding and deliverability.

```csharp
// Send immediate email
var response = await EmailSendingManager.Instance.SendEmail(
    userId: "user-uuid",
    templateSlug: "welcome",
    variables: new Dictionary<string, string> { { "username", "John" } },
    language: "en"
);
Debug.Log($"Email queued: {response.id}");

// Schedule email for tomorrow
var scheduled = await EmailSendingManager.Instance.SendEmail(
    userId: "user-uuid",
    templateSlug: "reminder",
    variables: new Dictionary<string, string> { { "eventName", "Tournament" } },
    language: "en",
    scheduledAt: DateTime.UtcNow.AddDays(1)
);

// Check status
var status = await EmailSendingManager.Instance.GetEmailStatus(response.id);
Debug.Log($"Status: {status.status}");

// Cancel scheduled email
var cancel = await EmailSendingManager.Instance.CancelEmail(scheduled.id);
Debug.Log(cancel.message);
```

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
| 200-399 | Data | `CloudSaveSaved`, `CloudSaveLoaded`, `ScoreSubmitted`, `PlayerProfileChanged` (204), `PlayerProfileLoaded` (307), `ValidatedStateLoaded` (308) |
| 400-499 | Features | `EmailSent` (404), `EmailCancelled` (405), `CrashReported` (410), `ValidatedRunStarted` (420), `ValidatedRunSubmitted` (421), `ValidatedRunRejected` (422), `ValidatedEvidenceUploaded` (423), `ValidatedEvidenceUploadFailed` (424) |
| 500-599 | Network | `RequestFailed`, `RateLimited` |

## Configuration Options

Import via **Window > horizOn > Config Importer** (it creates the config asset under `Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Resources/horizOn/HorizonConfig.asset` in your project, so it persists even though the SDK itself is installed read-only via the Package Manager):

| Option | Default | Description |
|--------|---------|-------------|
| API Key | - | Your horizOn API key |
| Hosts | `["https://horizon.pm"]` | Backend server URL(s). Single host skips ping; multiple hosts use latency-based selection. |
| Connection Timeout | 10 | HTTP request timeout in seconds |
| Max Retries | 3 | Retry count for failed requests |
| Retry Delay | 1.0 | Delay between retries in seconds |
| Log Level | INFO | DEBUG, INFO, WARNING, ERROR, NONE |

### Keep Your API Key Out of Version Control

The generated config asset contains your API key (obfuscated, not encrypted). Add it to your project's `.gitignore` and let every team member import their own config via the Config Importer:

```gitignore
# horizOn SDK config (contains your API key)
Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Resources/horizOn/HorizonConfig.asset
Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Resources/horizOn/HorizonConfig.asset.meta
```

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
| 409 | Conflict | For example `UNLOCK_LIMIT_REACHED` (gift code grants) |
| 422 | Unprocessable | Validated run rejected by a rule or ticket check (`LastErrorCode`) |
| 429 | Rate Limited | Wait and retry (`RUN_RATE_LIMITED` / `RUN_CAPACITY_REACHED` are not retried by the SDK) |

Player profile calls return `null` on failure and set `PlayerProfileManager.Instance.LastErrorCode`
to the server's stable `code` (constants in `PlayerProfileErrorCodes`). Validated Actions do the
same with `ValidatedActionsManager.Instance.LastErrorCode` (constants in `ValidatedActionsErrorCodes`).

## Self-Hosted Option

The horizOn SDKs work with both the **managed horizOn BaaS** and the **free, open-source [horizOn Simple Server](https://github.com/ProjectMakersDE/horizOn-simpleServer)**.

Simple Server is a lightweight PHP backend with no dependencies — perfect as a starting point if you want full control over your infrastructure. It supports core features like leaderboards, cloud saves, remote config, news, gift codes, feedback, and crash reporting.

To connect to your own server, pass your server URL when creating `HorizonServer`:

```csharp
var server = new HorizonServer("https://your-server.example.com");
await server.Connect();
```

Validated Actions is cloud only: Simple Server has no such endpoints, so `StartRun`,
`SubmitValidated` and `GetState` fail with `LastErrorCode = "NOT_SUPPORTED"` there.

> **Note:** Simple Server is a starting point, not a full replacement. For the complete experience with dashboard, user authentication, multi-region deployment, and more, use [horizOn BaaS](https://horizon.pm).

## Project Structure

```
horizOn-SDK-Unity/        # the package (repo root)
├── package.json          # UPM manifest
├── CloudSDK/             # runtime + editor code (assembly definitions)
│   ├── Core/             # HorizonApp, HorizonServer, HorizonConfig
│   ├── Manager/          # Feature managers (incl. CrashManager)
│   ├── Service/          # EventService, NetworkService, LogService
│   ├── Objects/          # Data models, requests, responses
│   └── Editor/           # Config Importer, iOS build post-processor
├── Documentation~/       # API reference (hidden from the Unity importer)
├── Samples~/             # Examples + Example UI (import via Package Manager)
├── QUICKSTART.md
├── CHANGELOG.md
├── LICENSE.md
└── README.md             # This file
```

## Documentation

- **[Quickstart Guide](https://horizon.pm/quickstart#unity)** - Interactive setup
- **[QUICKSTART.md](QUICKSTART.md)** - Offline setup guide
- **[API Reference](Documentation~/UNITY_SDK_API_REFERENCE.md)** - Complete API reference
- **[horizOn Docs](https://horizon.pm/docs)** - Full documentation

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Configuration not found | Import via **Window > horizOn > Config Importer** |
| No active host | Call `server.Connect()` before API requests |
| Invalid API Key | Verify key at [horizon.pm](https://horizon.pm) |
| Rate limited (429) | Implement caching, reduce API calls |

## Support

- 📖 **Documentation**: [horizon.pm/quickstart](https://horizon.pm/quickstart)
- 💬 **Discord**: [discord.gg/horizOn](https://discord.gg/JFmaXtguku)
- 🐛 **Issues**: [GitHub Issues](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues)

## License

MIT License - Copyright (c) [ProjectMakers](https://projectmakers.de)

See [LICENSE.md](LICENSE.md) for details.
