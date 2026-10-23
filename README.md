<p align="center">
  <a href="https://horizon.pm">
    <img src="https://horizon.pm/media/images/og-image.png" alt="horizOn - Game Backend & Live-Ops Dashboard" />
  </a>
</p>

# horizOn Cloud SDK for Unity

[![Unity 6](https://img.shields.io/badge/Unity-6000.0%2B-blue?logo=unity&logoColor=white)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)
[![Version](https://img.shields.io/badge/version-1.12.0-orange)](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/releases)

Official Unity SDK for **horizOn** Backend-as-a-Service by [ProjectMakers](https://projectmakers.de).

## Features

| Feature | Manager | Description |
|---------|---------|-------------|
| 🔐 **Authentication** | `UserManager` | Anonymous, email, Google and Apple sign-in |
| 🏆 **Leaderboards** | `LeaderboardManager` | Global rankings and scores |
| ☁️ **Cloud Save** | `CloudSaveManager` | Persist game data across devices |
| ⏳ **Matches** | `MatchesManager` | Explicit asynchronous turns, immutable reports and original ranked-run recovery |
| 🧩 **Shared Objects** | `SharedObjectsManager` | Opaque bytes, indexed bounded queries and explicit original-revision writes |
| 🎒 **Items** | `ItemsManager` | Bounded catalog, unique inventory and explicit revision-fenced equip/adjust/discard |
| 🤝 **Friends** | `FriendsManager` | Exact share-code lookup, revision-fenced requests/friends/blocks and public profile projections |
| 📬 **Inbox** | `InboxManager` | Explicit snapshots, read/remove, atomic claim/replay and authoritative value-cache updates |
| 🔄 **Realtime** | `RealtimeManager` | Optional Cloud invalidations for inbox, matches and chat; HTTP content reload |
| ⚙️ **Remote Config** | `RemoteConfigManager` | Dynamic settings without redeploying |
| 🌍 **Localization** | `LocalizationManager` | Multi-language strings fetched at runtime (15 languages) |
| 📰 **News** | `NewsManager` | In-game announcements |
| 🎁 **Gift Codes** | `GiftCodeManager` | Promotional code redemption, cosmetic unlocks, authoritative player values |
| 🪪 **Player Profile** | `PlayerProfileManager` | Avatar, frame and badges shown on leaderboards |
| 📈 **Statistics** | `StatisticsManager` | Batched sum/max/min/last counters with optional UTC daily/weekly resets |
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
2. Ensure **Unity UI** (`com.unity.ugui`, 2.0.0) is installed through Package Manager. The included HelloHorizon sample uses it. The Asset Store package declares this dependency during import.
3. Import via **Assets > Import Package > Custom Package**
4. Import all files when prompted

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
| Statistics | `StatisticsExample.cs`, `StatisticsSampleStore.cs` |
| Inbox | `InboxExample.cs` (explicit screen reads and player claims) |
| Achievements | `AchievementsExample.cs` (progress, manual unlock, pending handoff and saved Inbox claim) |
| Friends | `FriendsExample.cs` (explicit lookup, saved original actions and reconciliation) |
| Matches | `MatchesExample.cs`, `MatchesSampleStore.cs` (two-player original intents, reports and guarded ranked runs) |
| Items | `ItemsExample.cs`, `ItemsSampleStore.cs` (durable original purchase/CAS and GET-only recovery) |
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

Google sign-in/sign-up accepts an authorization code obtained by your game's
platform-specific Google integration. Configure the OAuth client and redirect URI
with Google and horizOn; the SDK does not include Google's sign-in plugin.
Real Google/Apple provider sign-in on target devices is not established by the
SDK's native transport tests.

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

The SDK implements a native iOS path using ASAuthorizationController through the
bundled AuthenticationServices plugin. Your iOS app still needs the Sign in with
Apple capability, provisioning and Apple Developer/project configuration. Actual
Apple sign-in on an iOS device and App Store acceptance have not been verified.

On Android, Standalone (Windows / Linux / macOS) and the Editor, the SDK can open
the Apple Services ID authorization URL. Your game must receive the OAuth redirect
and hand its identity token back to the SDK; opening the browser does not complete
sign-in. This platform integration and real provider sign-in require separate
acceptance on each target platform.

```csharp
// Native iOS path; outside iOS requires your own redirect/token bridge (below).
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

Sign in before saving or loading. JSON and binary operations send the current player's
Bearer session. A missing or mismatched session is rejected locally. Binary loading uses
`POST /api/v1/app/cloud-save/load` with a JSON `userId` and `Accept: application/octet-stream`.

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

**Several devices.** `Save` and `SaveBytes` overwrite whatever is stored, so two devices of the
same player overwrite each other silently. Load a snapshot with its revision and save with that
revision instead. The server only writes while the save is still at that revision. Otherwise the
result is a conflict and nothing is written:

```csharp
var snapshot = await CloudSaveManager.Instance.LoadSnapshot();   // LoadBytesSnapshot() for bytes
if (!snapshot.IsSuccess || !snapshot.HasRevision)
{
    return; // load failed, or the server sent no revision: the state is unknown, not empty
}

var data = snapshot.Found ? JsonUtility.FromJson<GameData>(snapshot.Data) : new GameData();
data.Coins += 50;

CloudSaveWriteResult result = await CloudSaveManager.Instance.Save(JsonUtility.ToJson(data), snapshot.Revision.Value);
if (result.IsConflict)
{
    // Another device saved first. Load a new snapshot, merge or let the player choose, save again.
}
else if (result.IsSuccess)
{
    Debug.Log($"Saved at revision {result.Revision}"); // pass it to the next Save
}
```

Revision `0` means an empty slot. A missing revision (`HasRevision == false`, for example on a
self-hosted simpleServer) means unknown, not empty. A conflict (HTTP 409) is never retried or
overwritten automatically.

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

Prepare once, persist the complete intent before sending, and retry that saved intent after a timeout or restart. `GiftCodesExample` includes a scoped durable sample store and separate explicit redeem/retry actions.

```csharp
var intent = await GiftCodeManager.Instance.PrepareRedemption("PROMO2024");
// Save intent durably for this project/player BEFORE calling RedeemPrepared.
var result = await GiftCodeManager.Instance.RedeemPrepared(intent);
// On uncertainty, load and resend this same intent. Never prepare a replacement automatically.
```

Counts/revisions remain canonical Int64 decimal strings. Replay returns current `playerValues`, `alreadyApplied/replayed = true`, and no historical `giftData` or grants. Replace balances from the authoritative snapshot; never add grants locally. V1 `Redeem(code[, id])` returns `UPGRADE_REQUIRED`. `MIGRATION_REQUIRED`, `COUNT_AHEAD`, `CAMPAIGN_CHANGED`, `COUNTER_EXHAUSTED` and other domain errors require explicit reconciliation. Changing a visible code cannot retarget a saved campaign ID. Sign-out/project changes fence late responses and cannot move a pending intent to another player.

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

#### Run start context and sus runs

`StartRun` takes an optional `ValidatedRunContext`: what the run starts from. All fields are
optional; empty fields are not sent, and without any field the request stays the old one.

```csharp
var context = new ValidatedRunContext(
    Application.version,                    // gameVersion, at most 64 printable ASCII characters
    contentVersion: "levels-7",
    simulationVersion: "sim-3",
    replayFormatVersion: "inputs-v1",
    contentDigest: ValidatedActionsManager.ComputeInputLogHash(levelBytes), // SHA-256, 64 hex
    initialState: initialStateBytes);       // raw bytes, sent as base64
var run = await ValidatedActionsManager.Instance.StartRun("weekly", context);

// Or set the versions once; used whenever StartRun gets no context (no merge with a passed one)
ValidatedActionsManager.Instance.DefaultRunContext = new ValidatedRunContext(Application.version);
```

The server binds the context to the run together with what it fixes itself (rule version,
cloud save, server-owned values, seed, start time). A `contentDigest` that is not 64 hex
characters fails locally with `INVALID_CONTENT_DIGEST`; the server answers
`INITIAL_STATE_TOO_LARGE` (413, above the game's evidence size limit) or
`INITIAL_STATE_INVALID_ENCODING` (400).

`result.sus` is true when an accepted run crossed a soft threshold of the rules. The score
counts; the server keeps the run with its start context for a review and asks for the input
log through `result.evidence`, which the SDK uploads on its own after `SubmitValidated`, like
a top N record. The reasons stay on the server. Older servers do not send the field (`false`).

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

### Statistics

Define `kills` (sum) and `best_score` (max) for the project in the Dashboard. Sign in, then load
an explicit snapshot when the game needs it. Nothing is fetched automatically during sign-in.

```csharp
using PM.horizOn.Cloud.Objects.Network.Requests;

var statistics = StatisticsManager.Instance;
var snapshot = await statistics.GetStatistics(); // values, definitions, revision and server time
if (snapshot == null) return;
string operationId = statistics.CreateOperationId(); // create once and persist with this batch
var batch = new[] { new StatisticUpdate("kills", 3), new StatisticUpdate("best_score", 250) };
var result = await statistics.UpdateStatistics(batch, operationId);
if (result == null) Debug.LogWarning(statistics.LastErrorCode);
```

After a timeout, keep the original batch and ID, refresh the snapshot and retry that same ID.
The SDK never invents a replacement ID. A retained receipt replays safely; an old or evicted
unknown ID is rejected with `OPERATION_EXPIRED`, so the game must reconcile rather than report
the session again. At most eight receipts and a 24-hour acceptance window are retained;
delayed out-of-order IDs can expire sooner. Values use exact `long` integers within
±9,007,199,254,740,991. `hasValue` distinguishes an unset counter from zero.

`CurrentStatistics` and `CurrentDefinitions` cache the last accepted response and clear on
session/host change or `ClearCache()`. Late responses cannot overwrite a newer revision or a
new session. Optional `maxPerUpdate` uses `HasMaxPerUpdate` (zero means absent in the Unity
model). See `StatisticsExample.cs` for a pending batch persisted across restarts and the
[API reference](Documentation~/UNITY_SDK_API_REFERENCE.md#statistics) for all members/errors.
Revision and snapshot-server-time watermarks survive catalog changes. `ClearCache()` also
invalidates pending conditional reads, so a late response cannot create an empty catalog.

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

The recipient must already be an active, email-verified user with a nonempty email
in the API key's project; anonymous users are not eligible. Set **Recipient User Id**
on the [minimal example](Samples~/Examples/Features/EmailSendingExample.cs) to that
user's ID and configure the template first. The example does not create or sign in
a replacement user. A queued/status response is separate from external SMTP delivery.

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

**Limit**: counted per account, shared by all players of all your games. On the Free plan the soft limit is 10 and the hard limit 100 requests per minute. Paid plans have higher limits, see [horizon.pm/pricing](https://horizon.pm/pricing).

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

    // The per-minute limit counts for your whole account, not per player
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
- 💬 **Discord**: [discord.gg/JFmaXtguku](https://discord.gg/JFmaXtguku)
- 🐛 **Issues**: [GitHub Issues](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues)

## License

MIT License - Copyright (c) [ProjectMakers](https://projectmakers.de)

See [LICENSE.md](LICENSE.md) for details.

## Realtime invalidations (Cloud)

After sign-in, subscribe with `RealtimeManager.Instance.Subscribe("inbox:" + UserManager.Instance.CurrentUser.UserId)`. Handle `ResyncRequired` and `EventReceived` by loading current authorized content through the feature HTTP API. `PollingRequired` is the offline fallback signal; wait at least its `pollAfterSeconds` and never poll chat. Call `Unsubscribe(topic)` when the screen closes.

The manager reconnects with jitter, reauthorizes topics and requests a fresh snapshot after reconnect. Events contain IDs and revisions only, not message or save contents. Unity native and WebGL transports share this contract. simpleServer reports `NOT_SUPPORTED`; realtime is Cloud only.

## Player values

`PlayerValuesManager.Instance` reads server-owned balances and active offers. All four routes require the matching signed-in player's ID and session. `GetState(userId)`, `Purchase(userId,offerKey,operationId)` and `Adjust(userId,key,longAmount,reason,operationId)` return `PlayerValuesResult` with `IsSuccess`, `State`, `ErrorCode`, `Error`; `GetOffers(userId)` returns `PlayerValuesOffersResult` with `Data`. Business errors can carry an up-to-date `State`: render it even when `IsSuccess` is false. `CurrentState` and `CurrentOffers` are defensive copies; `ClearCache()` invalidates in-flight responses. Session/project/host changes discard old state. Revisions and server-clock watermarks survive catalogue/cache invalidation.

```csharp
using PM.horizOn.Cloud.Manager;
var userId = UserManager.Instance.CurrentUser.UserId;
var values = PlayerValuesManager.Instance;
var loaded = await values.GetState(userId);
if (!loaded.IsSuccess) return;
var operationId = values.CreateOperationId(); // persist with the immutable purchase intent
var result = await values.Purchase(userId, "energy-pack", operationId);
if (result.State != null) foreach (var balance in result.State.values) UnityEngine.Debug.Log(balance.balance);
if (!result.IsSuccess) UnityEngine.Debug.Log(result.ErrorCode);
```

Generate one operation ID per logical action and reuse that exact ID and payload after a timeout. Never automatically create a replacement after `OPERATION_EXPIRED`; refresh state and reconcile the intended action first. `CreateOperationId()` uses learned server time plus elapsed monotonic time, falling back to local UTC before the first state/offer response (`HasServerClock` is false). No purchases or adjustments are automatically polled. Amounts are signed `long` integers within +/-9,007,199,254,740,991; unsafe or fractional response tokens are rejected before Unity's JSON conversion. A `dailyCap` of -1 represents JSON null, while zero is a real zero cap (`HasDailyCap`). Offer `purchasesPerDay == -1` means unlimited. Realtime is optional; fetch HTTP state after validated actions or another balance-changing feature.

## Inbox (TASK-1006)

Use `InboxManager.Instance.GetInbox(userId, since: null)`, `MarkRead(userId, entryId)`, `Remove(userId, entryId)` and `Claim(userId, entryId)`. They require the matching current player session and project key. Snapshot results expose `IsSuccess`, `Data`, `ErrorCode`, `Error`; claim `Data` exposes `entryId`, `replayed`, `inbox`, `playerValues` and returned `items` references (`instanceId`, `itemKey`). Entry dictionaries `parameters` and snapshot `counts` preserve escaped string values; attachment amounts retain safe64-bit integers without first rounding through a double. Unknown attachment kinds or malformed numbers fail with INVALID_RESPONSE.

`limits` reports the current configurable `targeted`, `broadcasts`, `freeTexts`, `freeTextBytes` (UTF-8) and `freeTextRetentionDays`. These govern new writes; a retained mailbox remains readable after a limit is lowered. Revisions are safe integers and may jump. Keep entry IDs opaque: a later event from the same source has a distinct ID, and read/remove/claim must use the selected original ID.

For known system messages, `entry.TryGetSource(out source)` exposes typed Friends, Groups, Match and Market references for navigation. The referenced feature API owns its current state and actions. Custom/legacy messages still expose their original type, reference and parameters. `entry.TryGetFreeText(out text)` returns the separate plain text form; disable rich text/markup in your UI. Other entries use `textKey` and localization parameters. There is no trusted publication route in the client SDK.

`CurrentInbox` returns a defensive copy. Unchanged HTTP snapshots keep cached entries and update polling metadata. Changed snapshots replace the entire cache, including removals. One active refresh and one coalesced follow-up preserve expiry ordering even when revision numbers are equal. Mutations invalidate in-flight reads. Session/project/host and cache epochs reject late answers; revision/server-time watermarks survive ClearCache. A conditional response without cached entries triggers one bounded full reload. JSON payloads use the shared 32 MiB technical frame and typed field/depth validation; current operator quotas are not generic array ceilings. Inbox payload contents are not copied into raw-response logs.

Claims reuse the original entry ID after an uncertain response. They never choose rewards or add returned amounts locally. The SDK accepts the complete authorized Player Values snapshot through `PlayerValuesManager.AcceptServerState(userId,state)` and rejects stale state rather than replacing a newer balance. INBOX_FULL/value-limit/UNCLAIMED_REWARD errors remain visible and do not destroy pending attachments. The sample exposes only explicit player-triggered claims.

For a new selection use `InboxExample.ClaimNewEntry(entryId, persistOriginalIntent)`: it checks `entry.CanClaim` in the authorized SDK snapshot before calling your synchronous persistence callback. Value rewards and `attachment.itemReturn` (`listingId`, `instanceId`, `itemKey`) are claimable, including an item return with no values. After an item return, refresh the inventory; the descriptor and claim references do not contain a complete item. The callback must durably save or verify the exact entry ID under the current project/player, refuse a conflicting pending intent and return true only on success. A failed write or an identity/cache change sends no claim. Use `ClaimEntry(savedOriginalEntryId)` only for an already saved intent; it remains replayable when that entry is absent or already claimed. Never generate a replacement ID or overwrite a pending claim. Informational entries do not create reward intents.

There is no automatic polling or socket connection. A screen waits at least `pollAfterSeconds` between lifecycle-driven refreshes. Optional realtime `inbox:<userId>` and `inbox:project.<projectApiKeyId>` metadata calls ClearCache, followed by an explicit HTTP refresh. InboxExample demonstrates lifecycle cleanup without closing another feature's shared realtime manager.

PlayerValuesExample now binds pending storage to project/player across regional hosts and migrates original configured-host keys without changing the command. Conflicts and damaged data block replacement. Because portable PlayerPrefs cannot enumerate unknown historical hosts, an existing player with no known pending intent requires game-owned reconciliation before this automatic sample creates another purchase. New players get one demo intent. Cache reads never delete an invalid old anonymous token, and restore failure never creates a replacement identity in these value-bearing samples.

The statistics sample scopes saved batches to the project API key and player. An older projectless pending batch is preserved and blocks a new batch until game-specific reconciliation. Its identity recovery and signup marker are saved before authentication, so a transient restore error or lost SDK token cache cannot silently create another guest.

## Achievements (TASK-1008)

`AchievementsManager.Instance` provides these explicit player-session calls:

| Method | Result |
|---|---|
| `GetAchievements(userId, definitionVersion: null)` | `AchievementsResult` |
| `Unlock(userId, originalKey)` | `AchievementUnlockResult` |
| `DeliverRewards(userId, originalKeys)` | `AchievementDeliveryResult` |

Each result exposes `IsSuccess`, `Data`, `ErrorCode`, `Error`. Snapshot `Data` contains `userId`, `revision`, opaque `definitionVersion`, `definitions`, `progress`, `limits` and `serverTime`. Unlock `Data` contains `achievements`, `newlyUnlocked`, `replayed` and `deliveryResults`; delivery `Data` contains `achievements` and per-key `results`. Namespaces follow the other managers and response DTOs.

Use the currently authenticated player's `UserManager.Instance.CurrentUser.UserId`; all calls attach that player's Bearer session and current project key. No call signs up, polls or opens a socket. `CurrentAchievements` returns a defensive copy; `ClearCache()` invalidates on matching own-player `achievements:<userId>` realtime metadata or reconnect, followed by an explicit HTTP refresh. Host/project/session changes, including switching away and back, invalidate cached metadata and late results. Revision/time watermarks survive a materialized-cache clear. Calls are serialized.

The version is an opaque64-character token. Only a matching metadata cache is sent conditionally: `definitions:null` retains that exact cache, while `definitions:[]` replaces it. An unknown caller version causes a full request; an unexpected null payload permits one bounded full reload. Hidden locked metadata is absent and becomes visible after its unlock. Versions are never ordered numerically or lexically.

Fresh achievements atomically grant rewards with the durable unlock. Compact progress uses `rewardState:"complete"` and `unlockedAt:null`; it creates no new pending reward or Inbox entry. `DeliverRewards` is a compatibility/status call for one to eight original keys, not a second grant. Retained legacy `pendingReward`/`inboxEntryId` fields describe old accepted rewards only; unresolved originals require explicit source migration (`MIGRATION_REQUIRED`), never reconstruction from current definitions or automatic regrant. Repeat an uncertain manual unlock with the same key, and read current Player Values instead of adding reward deltas locally.

`AchievementsExample` exposes explicit progress, manual unlock and legacy reward-status actions. `RefreshAfterStatistics(report)` reacts only to a matching successful, non-replayed report with nonempty `newlyUnlocked`. Saved legacy Inbox claims retain their original project/player/entry ID after uncertainty; they are not part of a fresh compact unlock. Exact safe64 reward/threshold lexemes are validated before conversion.

## Friends (TASK-1009)

Use `FriendsManager.Instance` with the currently signed-in player's `UserManager.Instance.CurrentUser.UserId`. Every call sends the matching Bearer session and current project key. Results expose `IsSuccess`, `Data`, `ErrorCode`, `Error`.

| Method | Result |
|---|---|
| `GetFriends(userId)` | `FriendsResult` |
| `FindPlayer(userId, exactUuidOrHCode)` | `FriendLookupResult` |
| `SendRequest(userId, targetUserId, expectedRevision)` | `FriendsResult` |
| `AcceptRequest(userId, targetUserId, expectedRevision, originalRequestId)` | `FriendsResult` |
| `DeclineRequest(userId, targetUserId, expectedRevision, originalRequestId)` | `FriendsResult` |
| `CancelRequest(userId, targetUserId, expectedRevision, originalRequestId)` | `FriendsResult` |
| `RemoveFriend(userId, targetUserId, expectedRevision)` | `FriendsResult` |
| `BlockPlayer(userId, targetUserId, expectedRevision)` | `FriendsResult` |
| `UnblockPlayer(userId, targetUserId, expectedRevision)` | `FriendsResult` |

Snapshot `Data` includes camelCase `userId`, `revision`, `friendCode`, `friends`, `incoming`, `outgoing`, `blocked`, `limits`, `serverTime`. Find returns only `userId`, `friendCode`, `display`. Search accepts an exact UUID or canonical `H-` share code; it never trims identifiers or searches names/emails/substrings. UUID bytes in share codes use network order, matching the server.

Friends and requests expose only public `display.name` and `display.profile` (`avatarId`, `frameId`, `badges`). Missing names/cosmetic IDs remain null. A friend's nullable `lastActiveAt` is approximate activity, **not an online flag**; show unknown when absent. Blocked rows contain only `userId` and `blockedAt`. Private/login fields and complete social responses are excluded from raw response logs.

Before a mutation, durably store its action, target UUID, original `expectedRevision` (including zero), and the original incoming/outgoing `requestId` for accept/decline/cancel under the matching project/player. Each mutation makes exactly one transport attempt, including global429,5xx and timeout responses. A failed or uncertain result keeps that original intent. Explicitly refresh and reconcile `REVISION_CONFLICT`; never automatically insert the new revision, substitute a different request ID or resend the action after refresh. `FriendsExample.ApplySavedAction` accepts this already stored command and never creates another identity.

`CurrentFriends` is a defensive copy. Calls are serialized; current host/project/session, network-instance generation and cache epoch fence late responses, including switches away and back. `ClearCache()` preserves revision/server-time watermarks. Own-player `friends:<userId>` realtime metadata can invalidate the cache; the game explicitly reloads by HTTP. There is no automatic polling, realtime connection or signup. Lists retained after a downgrade remain readable/removable even if their size exceeds the returned current `limits.maxFriends`; capacity decisions belong to the server. `NetworkService.ContextGeneration` is a read-only scope counter available to game UI callbacks, including the shipped example.

## Groups (TASK-1010, Develop)

`GroupsManager.Instance` uses the current player's `UserManager.Instance.CurrentUser.UserId`, project key and matching Bearer session. Each method returns `GroupsResult<T>` with `IsSuccess`, `Data`, `ErrorCode`, `Error`; `LastErrorCode` is also available. No signup, polling, pagination loop or socket is started.

| Method | Data |
|---|---|
| `GetGroups(userId)` | `MyGroupsView` |
| `GetGroup(userId, groupId)` | `GroupView` |
| `GetMembers(userId, groupId, afterUserId: null, limit: 50)` | `GroupMembersView` |
| `GetPending(userId, groupId)` | `GroupPendingSnapshot` |
| `CreateGroup(userId, expectedPlayerRevision, name, description: "", joinMode: "open", data: null)` | `GroupMutationView` |
| `UpdateGroup(userId, groupId, expectedRevision, name, description, joinMode, data)` | `GroupMutationView` |
| `JoinGroup`, `RequestJoin`, `LeaveGroup`, `DeleteGroup` `(userId, groupId, expectedRevision)` | `GroupMutationView` |
| `InvitePlayer`, `RemoveMember`, `TransferLeadership` `(userId, groupId, expectedRevision, targetUserId)` | `GroupMutationView` |
| `AcceptInvite`, `DeclineInvite`, `CancelRequest` `(userId, groupId, expectedRevision, requestId)` | `GroupMutationView` |
| `CancelInvite`, `AcceptRequest`, `DeclineRequest` `(userId, groupId, expectedRevision, targetUserId, requestId)` | `GroupMutationView` |
| `SetMemberRole(userId, groupId, expectedRevision, targetUserId, role)` | `GroupMutationView` |

Data is `GroupJsonData.FromJson("{...}")`, an immutable original JSON object; `data.Json` preserves arbitrary keys, null and finite numeric lexemes, including integers beyond double precision. Omitted private `GroupView.data` and `myRole` remain null. Pass a received wrapper unchanged when editing other metadata. Creation defaults to `{}`; Update requires a data object. Current name/description/data-byte/data-depth policies are Admin System Config values. Former 120/1024/2048-byte and depth 16 defaults are not decoder or write ceilings. The Groups decoder uses an iterative grammar with a separate 1024-level technical nesting bound; it preserves retained metadata after policy reductions. Nullable public profile names/cosmetic IDs remain null. All DTO/cache copies are separate except immutable JSON wrappers.

All 16 mutations (including PUT) send the frozen original input exactly once, even after429/5xx/timeout. Keep the caller-reviewed revision and exact pending request UUID; no client group UUID or operation ID is generated for creation. On uncertainty, retain the durable original intent and explicitly read/reconcile before another decision. Never replace its revision/request ID automatically. Snapshot/member/pending replies carry their own revision; explicitly restart member pages when it changes. Lists retained after a plan downgrade stay readable; current server limits govern additions. Calls are serialized and host/project/session/generation/epoch fenced. `CurrentGroups` is a defensive own-player snapshot; `ClearCache()` preserves bounded revision/time history. Hosted `groups:{groupId}` carries optional invalidation metadata; explicitly read HTTP after invalidation/reconnect. simpleServer supports REST without hosted realtime.

`GroupsExample` demonstrates explicit two-player UI actions: Refresh, ReviewGroup, one member page, create, invite/accept, officer/leadership transfer and leave/delete. Configure its stable project UUID. `GroupsSampleStore` flushes and verifies original project/player-scoped input before every send and blocks replacement after uncertain results. A saved confirmed result is presented for explicit acknowledgement without another HTTP mutation. `AcknowledgeSavedSuccess` archives the whole original/result directory with one rename after the game applies the result. A persistent exclusive OS handle releases ownership when the process exits; the lock file itself is never deleted or treated as an owner marker. These are process-death recovery guarantees, not a filesystem/power-loss durability guarantee.

A `SendLease` retains exclusive project/player ownership across preparation, the HTTP await and result persistence; competing replay and ACK attempts fail before another mutation. A fully flushed, canonical matching `result.*.writing` is recovered before send. Partial, mismatched or conflicting temporary results preserve the original and block replacement for explicit reconciliation.

New journals use `groups-PROJECT-PLAYER.json.journal/intent.json` and `result.json`. Existing flat `.json`/`.success` originals migrate only after a complete flushed journal is atomically published; exact legacy copies remain inside it for inspection. An old interrupted two-file ACK can recover its original only from the exact matching owned archive hash/scope. Interrupted migration resumes only for identical copies; missing/conflicting originals or results remain blocked for game-owned reconciliation. No migration changes a revision, request ID, command hash or player identity. Production games own archive retention/transactional persistence. WebGL must supply a game-owned durable browser store; this filesystem sample fails closed there. New pending commands must select a current matching request; saved original replay never substitutes it. Failed restoration never creates a replacement identity.


## Items (TASK-1011, Develop extension)

Use `ItemsManager.Instance` with the current signed-in `UserManager.Instance.CurrentUser.UserId`.
Every result has `IsSuccess`, `Data`, `ErrorCode`, `Error`; `LastErrorCode` is also available.
There is no signup, background poller, automatic page loop, local grant or automatic revision replacement.

| Method | Data |
|---|---|
| `GetInventory(userId)` | `InventorySnapshot` |
| `GetCatalog(userId, afterKey: null, limit: 50, catalogRevision: null)` | `ItemCatalogPage` |
| `AdjustProperties(userId, instanceId, expectedRevision, IList<ItemPropertyDelta> changes)` | `InventorySnapshot` |
| `SetEquipped(userId, instanceId, expectedRevision, equipped)` | `InventorySnapshot` |
| `DiscardItem(userId, instanceId, expectedRevision)` | `InventorySnapshot` |

`ItemsResult<T>` and the DTOs are in `PM.horizOn.Cloud.Objects.Network.Responses`. Snapshot fields
are camelCase: `userId`, `revision`, `instances`, `limits`, `serverTime`; an instance has
`instanceId`, `itemKey`, `properties`, `equipped`, `createdAt`. Catalog pages have `revision`,
`definitions`, `nextAfterKey` (null at the end), `total`, `limits`, `serverTime`. Explicitly pass
the first page's revision to subsequent pages; a conflict requires a deliberate restart. Catalog
includes archived templates so old instances stay interpretable; never treat `active:false` as deletion.

Flat property objects become `ItemProperty[]`: `key`, `type` (`ItemPropertyType.Integer`,
`Boolean`, `String`) and the corresponding `integerValue` (`long`), `booleanValue`, `stringValue`.
Strings preserve Unicode and embedded U+0000; keys retain the server's strict ASCII grammar.
Original integer tokens are validated before conversion; fractions, exponents, negative zero,
unsafe integers and decoded duplicate keys fail. Canonical UTF-8 property bytes obey the server's
256-byte limit. No floating-point dictionary projection or native JSON string clone is used for
Items DTOs. `CurrentInventory` and `CurrentCatalogPage` are defensive typed copies.

All three inventory writes send the frozen original body exactly once, including global429,
5xx and timeout cases. Keep the caller-reviewed `instanceId`, revision (including zero) and deltas
or equipment choice. On uncertainty, explicitly read/reconcile; never automatically substitute
the new revision or another instance. Server snapshots replace matching state. Requests serialize,
and actual network instance/host/project/player/session/generation plus cache epoch fence late
responses and away/back changes. `ClearCache()` preserves separate inventory/catalog revision and
server-time watermarks. Current tier limits do not delete retained downgraded state.

`limits.catalog`, `limits.inventory` and `limits.receipts` carry current positive Int32
operator configuration. The SDK accepts inventories above the old 150-instance default and
retains all server-provided instances after limits are lowered. Catalog `total` is independent
of the current quota, bounded by the permanent UInt16 slot format (65,536 templates); each page
still contains at most 50 definitions. Compact server storage does not change public instance
properties, original revision-fenced writes or property deltas.

Player Values balance/offer lists follow current server configuration without tariff clamps.
Retired offer-day quotas may be omitted/null and stay unlimited (`purchasesPerDay == -1`).
Current balances and earned totals remain authoritative after a lower daily cap; compact replay
never reconstructs historical deltas or item grants.

Player Values DTOs add `offer.itemRewards` (`ItemReward[]{itemKey,quantity}`) and
`state.operation.items` (`ItemReference[]{instanceId,itemKey}`), defaulting to empty arrays for
legacy payloads. A fresh purchase returns immediate references; compact replay returns current
state and empty `applied`/`items`, including after offer changes or discarded items. Persist one
original purchase `operationId` before HTTP, reuse it
on uncertain retry and explicitly GET inventory after success. Never synthesize an item, add
local balance deltas or buy again to recover a failed inventory refresh.

Import `ItemsExample` and `ItemsSampleStore` from the Examples sample. Configure the stable
project UUID; use an existing matching session. Explicitly call `ReadOneCatalogPage`,
`ReviewOffers`, then `SavePurchaseAfterReview` for an active configured item offer. The reviewed
page must contain its active item templates. After success, `SaveEquipAfterReview`,
`SaveAdjustAfterReview` and the deliberately destructive `SaveDiscardAfterReview` illustrate
explicit inventory CAS. No action runs automatically in Start/Update. New adjustments select a
current numeric property and reviewed rule before persisting an intent.

The sample writes a hashed project-key/player-scoped original command using CreateNew, durable
flush and readback before HTTP; regional hosts are callback fences, not journal identities. An
uncertain/conflicting mutation remains saved after Refresh. `ApplySavedIntent` preserves its
original body. A persistent OS-exclusive `SendLease` spans preparation, awaited HTTP and result
persistence, so another actor or process cannot send the same journal concurrently. Fully flushed
unpublished result files recover before HTTP; partial/conflicting files block replacement sends. If an uncertain purchase's offer changed or disappeared,
`ApplySavedIntent(confirmOfferChanged:true)` is an explicit game/player confirmation to repeat
the original ID, allowing the server to replay the retained receipt before today's offer check.
It never creates a replacement ID. A matching confirmed purchase receipt is flushed **before**
GET inventory; after GET failure or actor recreation, the saved success path performs GET only.
`AcknowledgeSavedSuccess` is explicit after reviewing that exact original result and, for purchases,
inventory recovery. The same exclusive lease archives the complete original/result directory in
one rename without another network operation; stale actors cannot acknowledge a newer journal.
After a lost inventory mutation response, explicitly Refresh, inspect the saved original against
current state, call `ReconcileSavedMutationAfterReview`, then acknowledge. Its journal labels this
as a player reconciliation, never server-confirmed success. The reviewed inventory revision must
be strictly greater than the original CAS revision, permanently fencing a delayed write; equal or
older snapshots cannot release it. This path cannot close an uncertain purchase. Production games own transactional persistence and
archive retention. WebGL games must supply a game-owned durable browser journal; this filesystem
sample refuses new writes before HTTP on WebGL. Restore failures must retain identity and pending
commands; this example never signs up a replacement player.

Sample journals use a persistent exclusive lock handle, released by process termination, rather than
a stale CreateNew lock marker. ACK renames the complete journal directory atomically, keeping
original and result together. A confirmed changed-offer purchase with zero item references is
still recorded under its original operation ID; inventory recovery stays GET-only.


### Shared Objects

`SharedObjectsManager.Instance` exposes explicit collection pages, owner usage, point reads,
typed queries and single-attempt `CreateObject`, `UpdateObject`, `DeleteObject` with the caller's
original `If-Match` revision. All calls use the signed-in matching player and project key.
`SharedObjectsResult<T>` carries `IsSuccess`, `Data`, `ErrorCode`, `Error`, an opaque `Identity`
and `RetryAfterSeconds`. `GetCached<T>(identity)` returns a deep copy. Repeated reads respect
`pollAfterSeconds` (collection pages default120s); no timer or automatic next page is installed.
An explicit continuation page remains separate. `ClearCache()` invalidates content and queued
responses while preserving bounded revision/server-time/cadence fences for the current identity.

Objects expose both exact `dataBase64` and decoded opaque `byte[] data`. Search values are typed
`SharedScalar` (safe integer or Unicode string), positions use safe64-bit integers; null partitions,
expiry, empty bytes and embedded NUL are preserved. Never deserialize object bytes as trusted game
state without your game's own format checks. Query cursors stay opaque and never grant access.

`Samples~/Examples/Features/SharedObjectsExample.cs` is an executable explicit-action recipe:
collection/rectangle/opponent reads, reviewed create/update, distinct confirmed delete, and frozen
opponent bytes/digest passed to Validated Actions. Its stable project-key/player journal retains
one complete original across regional host changes. An OS-exclusive handle owns preparation,
HTTP and atomic result persistence. A sent CREATE is **never resent**, even after an uncertain
response or recreation. `GetRecoveryUsage` and restricted own `QueryRecoveryObjects` are explicit
read-only reconciliation helpers; a complete consistent owner snapshot must permanently fence
the original usage revision before acknowledgement. Acknowledgement archives original and result
together; partial/corrupt/conflicting journals block replacements. Production games own archive
retention and UI review. Browser games must supply durable browser storage: the filesystem sample
fails before writes in a WebGL player. An unknown defense start remains guarded durably; after a
process restart that lost its VA ticket, use game-owned restoration/reconciliation instead of
starting another run. No automatic restore, replacement guest, reward or trusted Inbox send occurs.

Validated Actions generic/defense starts and future ranked starts share one start/submit lease.
`HasPendingOperation` includes synchronous result callbacks. An existing `CurrentRun` must be
explicitly discarded before a different logical run starts. Discard invalidates a late reply;
it never releases an in-flight HTTP owner. Run adoption is fenced by actual network generation,
host, project, player and session, including A→B→A transitions.


## Matches (TASK-1013, Develop)

Use `MatchesManager.Instance` with the existing signed-in
`UserManager.Instance.CurrentUser.UserId`. Restore a stored anonymous identity before offering
signup; failed restore must never replace that player. The manager requires the matching Bearer
session and project key. It creates no identity, poller, automatic page, timeout action or report.

All methods return `Task<MatchesResult<T>>` (`PM.horizOn.Cloud.Objects.Network.Responses`), with
`IsSuccess`, `Data`, `ErrorCode`, `Error`, actual `HttpStatusCode` (zero for local failures), and an
opaque `Identity`/`RetryAfterSeconds` for read cadence. Switch on error codes, never message text.

| Method | Typed data |
| --- | --- |
| `GetMatches(userId, recovery: false)` | `MyMatches` (`revision`, `active`, `invites`, `recent`, `limits`) |
| `GetLimits(userId)` | `MatchLimitsView` (`config`, `limits`, `capabilities`) |
| `FindOpenMatches(userId, minStrength, maxStrength, limit: 20, cursor: null)` | `MatchPage` |
| `GetMatch(userId, matchId, recovery: false)` | `MatchRead` (`match`, `serverTime`, `pollAfterSeconds`) |
| `CreateMatch(userId, expectedPlayerRevision, MatchCreateInput)` | `MatchCreated` (`match`, `playerRevision`) |
| `JoinMatch/DeclineInvite/AdvanceTimeout/LeaveMatch/CancelMatch(userId, matchId, expectedRevision)` | `MatchRead` |
| `StartMatch(userId, matchId, expectedRevision, turnOrder)` | `MatchRead` |
| `SubmitTurn(userId, matchId, expectedRevision, byte[] data, finish: false)` | `MatchRead` |
| `ReportResult(userId, matchId, finalStateRevision, byte[] result, winnerIds)` | `MatchReported` (`match`, `replayed`) |
| `StartRankedRun(userId, matchId, finalStateRevision)` | Existing `ValidatedRun` |

First explicitly read the player ledger and a selected Match, then persist the full original
action before sending. Creation uses the original **player ledger** revision; the seven ordinary
actions use the original **Match** revision. Every Matches write makes one transport attempt,
including global429 and503. On uncertainty keep the same request, bytes and revision. Reports
keep the frozen final-state revision, result bytes and winner IDs even after later Match revisions.
The SDK canonicalizes/sorts winner IDs without changing their membership. It never trusts a
client winner, adds rewards locally or silently substitutes a new revision.

`MatchView.data` is an opaque `byte[]`, alongside exact canonical `dataBase64`. An invitation or
removal projection has **null** data; an accepted empty state is `byte[0]`. Report bytes similarly
appear as `result`/`resultBase64`. Safe integer revisions/strength stay `long`, including the full
±9,007,199,254,740,991 range. Names preserve valid Unicode and NUL. `recent` can contain an ongoing
Match when this player already forfeited. Disputed/unconfirmed outcomes and `notificationPending`
remain visible; the latter is server notification metadata, not a grant or a reason to claim locally.

Read calls serialize. Respect server `pollAfterSeconds` and the game screen lifecycle. An opaque
next cursor is one explicit next-page action, including an empty candidate page with a cursor;
there is no automatic pagination. `GetMatches/GetMatch(recovery: true)` is an explicit uncertain
original reconciliation read. `GetCached<T>(identity)` returns a defensive copy. `ClearCache()`
invalidates content/queued responses while retaining bounded cadence/revision/server-time fences.
Host, project, user, session, network-instance and ABA/generation changes fence late responses.
Optional realtime `matches:{matchId}` is invalidation metadata; explicitly reread HTTP on event or
reconnect. simpleServer supports the ordinary REST paths and advertises `matchesRanked=false`
and `realtime=false`; unsupported ranked operations return501 `FEATURE_NOT_SUPPORTED`.

`StartRankedRun` shares the existing VA start/submit lease with generic starts and Shared Objects
defense. It cannot overwrite a current or in-flight ticket, including during callbacks; discard
invalidates adoption without releasing in-flight ownership. A confirmed ranked Match resumes
only its original server run. `RUN_EXPIRED`/`RUN_ALREADY_USED` never create another run.
`GetCurrentRankedRun(userId, matchId, finalStateRevision)` reads only a matching actual local
binding. Submit through `ValidatedActionsManager.SubmitValidated` using the same persisted score
and input log, then apply only returned state. VA's existing transport retries retain that tuple.

Import `MatchesExample` and `MatchesSampleStore` from the Examples sample and set the stable
project UUID. The executable explicit-action recipe covers ledger/Match review, create/join/start,
turns, reports, ranked start/submit and reconciliation. Its exclusive OS handle spans preparation,
HTTP, result persistence and exact reviewed-hash acknowledgement. Journal scope is project key
plus player, so regional host/session changes do not erase the original. An uncertain create is
never sent again: a complete explicitly reviewed, stable **strictly newer** player ledger must
fence its old CAS. This resolves uncertainty without guessing that any candidate Match was this
request. A fresh, explicitly rejected create can instead be reviewed/acknowledged only for known
nonapplying feature errors and their actual400/401/403/404/422/501 status;503, transaction conflict
and unknown responses remain unresolved. Lost leave/decline acknowledgement needs signed
`MATCH_NOT_FOUND`, absence from the player ledger and a strictly advanced original player revision.

The sample saves the full original ranked run ID/score/input log before VA submit. Conflicting
replacement data, partial/corrupt/interrupted results and another live actor/process block sends.
Complete interrupted results recover before HTTP. Explicit acknowledgement retires the proven original/result journal atomically and then removes only that new terminal directory. It cannot erase a newer original reviewed by another actor. Existing `*.acknowledged` archives are preserved; a crash between retirement and removal can leave a terminal `.completed` directory for game-owned cleanup. No new completed state archive is maintained.
These are tested process-death recovery properties, not a power-loss/filesystem durability claim.
WebGL mutations require game-owned durable browser storage; the sample fails closed without it.
Local native/protocol evidence does not imply a deployed game, hosted SDK E2E or release.


### Operator configuration and compact source references

Friends' four limits and all eleven Group limits are positive Int32 values from
current Admin System Config. Retained friends, requests, blocks, memberships,
pending entries and inventory remain readable after lowering a quota; no old
200/20/100/500/5/2048-element default truncates them. Legacy Friends `since` and
Friends/Groups pending `createdAt` may be missing or null and remain null.
Group limits additionally expose `maxPlayerInvitations`, `maxPlayerRequests`,
`maxNameBytes`, `maxDescriptionBytes` and `maxDataDepth`; `memberPageSize` governs
server page admission. The sample's `ReadOneMemberPageWithLimit(limit,next)` makes
an explicit configured page possible; 50 remains a convenience default.

Shared collection/storage/owner counts accept higher configured values. The
independent opaque-byte contract is 1 MiB decoded per object and per query result,
with explicit 50-row pages. Exact bytes survive a lower current quota. Sample
journals admit the complete original payload before sending.

Matches retain all seven limits: `activeMatches`, `stateBytes`, `retainedMatches`,
`pendingInvites`, `settlementMatches`, `historyRetentionSeconds`,
`rankedSettlementSeconds`; retained count/history seconds may be zero. The physical
BSON/Jackson state envelope is 14,648 KiB, separate from current policy. Read lists
are projected summaries plus `pendingResults` entries containing only `matchId`,
`finalStateRevision`, nullable `ownRankedRunId`, `expiresAt`. After an explicit
`ReadLedger`, `SaveRankedAfterPendingResultReview(matchId)` persists that original
source for the existing retry lifecycle even when full detail expired. Known run
IDs cannot be replaced by a different fresh response. No result/item reconstruction.
The 32 MiB technical response/journal frame admits the maximum opaque Match state;
it is not a claim that every possible unpaginated retained-list cardinality fits.
Groups journals separately admit 64 MiB for nested original JSON escaping; Shared
journals admit 2 MiB. The ordinary decoder retains its 24-level transport guard;
Groups uses the explicit iterative 1024-level path. These bounds are technical,
independent of System Config admission and database-storage accounting.

Chat channels/sends/reports accept 0 through Int32.MaxValue. Its fixed 20/15-message
projection and retention/operation protocols are unchanged; there is no App
all-channel response multiplied by the account's configured channel quota.

### Chat and device history

`ChatManager.Instance` provides `GetLimits`, `OpenChannel`, `SyncChannel`,
`SendMessage`, `GetOperation`, and `ReportMessage`. Hosted Chat is available in
all plans including Free. simpleServer reports Chat/realtime as unsupported.

`WatchChannel(userId, channelId)` listens to metadata over realtime and fetches
only missing message bodies over authorized HTTP. `GetFeed(channelId)` returns
the current server window: up to20 messages in project chat, up to15 messages
for30seconds in private/group chat. Offline/revoked subscriptions hide this
window. There is no polling fallback for Chat. `SyncChannel` is an explicit
REST snapshot and also works without a connected socket. Pass its complete
previous result to request only missing messages.

Successfully received or confirmed sent messages are copied to device history.
`ReadLocalHistory(channelId, before: null, limit: 50)` returns the newest delivered
messages first and an opaque next-page cursor (maximum100 per page). Server
expiry, epoch changes, logout and permission changes do not erase these copies.
`ClearLocalHistory(channelId)` and `ClearAllLocalHistory()` are explicit local
deletions. Files are isolated by API key and player beneath
`Application.persistentDataPath/horizon/chat`, with hashed directories and no
stored access token or plaintext API key. No hidden history count limit applies.

`GetServerTime()` advances the latest accepted server timestamp with a monotonic clock
for a new send. It returns null until a successful Chat response.

`FeedChanged`, `LocalHistoryChanged`, `LocalHistoryFailed` and `ChannelError`
separate live state from local persistence. An I/O error leaves a successful
server send successful and never resends it. Corrupt/partial local records are
reported, never silently overwritten. Native history uses an append log and a
small ID/hash/offset index, loaded once per channel and extended by new records.
Pagination is local delivery order, not a global original-timestamp sort.

Every write is sent once, including429/503. Preserve the original operation ID,
server-clock issuedAt and text to investigate an uncertain send. A missing
`GetOperation` result does not prove the server never accepted it and never
triggers an automatic new send. See `Samples~/Examples/Features/ChatExample.cs`.

Local native checks include a separate-process archive restart, actual HTTP
requests, and a real metadata WebSocket. Start
`python3 Tests~/ChatSocketFixture/mock_chat_server.py` before the Editor run and
set `HORIZON_CHAT_FIXTURE_URL=http://127.0.0.1:20124` and
`HORIZON_CHAT_FIXTURE_WS=ws://127.0.0.1:20125`. The fixture exits after the test.
These checks do not constitute a hosted deployment or a browser-device test.

WebGL flushes local changes to the browser IndexedDB filesystem explicitly and
coalesces writes during an in-flight flush. Browser storage errors are reported
through `LocalHistoryFailed`; they do not resend messages. Browser storage is
subject to browser quotas and user deletion. The flush bridge has automated
contract tests; native tests do not prove an exported browser game.


## Marketplace: current escrow and durable original actions

`MarketplaceManager.Instance` implements all ten authenticated App routes: `GetLimits`,
`GetRules`, `GetMarketplace`, `QueryListings`, `GetListing`, `CreateListing`,
`BuyListing`, `CancelListing`, `GetOperation`, and `RetryReturn`. Rules/query pages
are explicit (1–50 rows); continuation retains its original catalog revision or
opaque cursor. Every call uses the current player's Bearer and project key. No
method creates a player or polls automatically.

The five operator quotas are exact positive Int32 values, including values above
old tariff defaults. Current state keeps every retained listing/history reference
even after a quota is lowered. The Marketplace decoder uses the shared 32 MiB
JSON / 24-level transport frame without the generic 2,048-element guard. This is a
transport limitation, not a database-size or product-quota claim. Original item
properties, embedded NUL/Unicode and safe integers remain exact. Nullable fields
use the explicit Marketplace codec; Unity `JsonUtility` is not its wire decoder.

Import `MarketplaceExample.cs`, `MarketplaceSampleStore.cs` and `MarketplaceQuickstart.cs` from the Examples
sample. Set the real stable project UUID in `projectId`, retain the normal existing
SDK session, and call the explicit screen methods to read config/rules/inventory or
a listing. `ReviewCreate`, `ReviewBuy` and `ReviewCancel` freeze displayed terms;
only `ConfirmReviewedTrade` creates an operation UUID and a server-clock UTC
millisecond timestamp. The sample flushes and verifies the complete original JSON
before its single POST. A project/player OS-exclusive handle spans the entire
asynchronous send; closing the screen releases it after the in-flight task returns.

After an uncertain response use `RetryPending` for the same original bytes or
`RecoverOriginalOperation` to look up that original operation. Neither a 404 nor
expiry authorizes a new purchase. Replays may contain today's later terminal
listing (with `item == null`) or no retained listing at all. They do not restore
old inventory or balances. Only a minimal known identity is saved before explicit
current Inventory, Player Values and Marketplace GETs; after all three succeed,
the one resolved original is removed. Confirmed restarts perform only GETs. There
is no local completed-purchase/result archive or synthetic item grant. Storage
errors preserve originals and surface as exceptions; partial/conflicting files
require recovery and must not be deleted to permit a new trade.

`RetryReturnDelivery` retries the same listing's source-owned delivery.
`ClaimReturn` delegates the same original entry to Inbox. `INBOX_FULL` and
`INVENTORY_FULL` remain recoverable; a later replay after discard/resale is only a
reference and never recreates the item. The supplied file journal supports native
platforms. WebGL writes require a game-owned durable browser journal with exclusive
ownership; the native sample rejects writes instead of pretending a synchronous
file flush persisted browser storage.

`Tests/Editor/MarketplaceTransportTests.cs` runs the actual imported sample and
production transport against an isolated strict Python HTTP fixture: all ten
routes, 2,051 retained open offers and 151 history references after lowering,
actor destruction during an uncertain send, original retries, GET-only recovery,
and original escrow claim followed by discard/replay. The shared .NET source gate
also kills real journal-owner processes at five persistence boundaries. These
artificial local tests do not prove a hosted deployment or exported browser game.

The Groups sample's `ReadOneMemberPage()` reads the current auth-bound
`CurrentGroups.limits.memberPageSize`; missing limits require an explicit own-group
refresh. A changed size restarts the sample traversal without its old cursor.
`ReadOneMemberPageWithLimit(limit, next)` preserves the explicit wire parameter.

Minimal Match/final-revision→run-ID pairs stay in the current authenticated
context even with201 distinct pending sources; another read cannot evict a
still-protected source. Auth changes clear these local bindings. Their memory
can grow across reads; no result/item/state history or server record is added.

Group revision/time fences likewise retain only minimal IDs and watermarks until auth changes, including257 retained groups after policy is lowered to1. They hold no copied group content.

Minimal Match revision/time watermarks also survive more than512 retained entries until auth changes. Payload caches and polling-hint caches keep their existing small bounds.

Groups responses aggregate independent BSON records and can duplicate a group in a mutation. A6MiB control-character name needs about36MiB JSON; the same mutation can exceed72MiB. Groups alone parses its already received body/fragments at their actual length, preserving strict field/grammar/depth checks. There is no derived32/96/192MiB aggregate product quota. Available engine/HTTP memory remains a technical client limit; a parse/transport failure must preserve the original for explicit recovery. Other feature frames and small payload caches are unchanged.
