# horizOn-Server Unity SDK API Reference

## Overview

This document provides a comprehensive reference for all `/api/v1/app/**` endpoints in the horizOn-Server backend, including Unity SDK code examples for each endpoint.

**Base URL**: `https://horizon.pm`
**All endpoints require**: `X-API-Key` header for authentication
**Rate Limit**: 10 requests/minute per client

## Table of Contents

1. [Authentication & Rate Limiting](#authentication--rate-limiting)
2. [User Management](#user-management)
3. [Gift Codes](#gift-codes)
4. [User Logs](#user-logs)
5. [User Feedback](#user-feedback)
6. [Remote Config](#remote-config)
7. [News](#news)
8. [Cloud Save](#cloud-save)
9. [Leaderboard](#leaderboard)
10. [Player Profile](#player-profile)
11. [Validated Actions](#validated-actions)
12. [Data Models](#data-models)
13. [Error Handling](#error-handling)

---

## Authentication & Rate Limiting

### Authentication Header
All endpoints require an API key:
```
X-API-Key: horizon_your-api-key-here
```

### Rate Limiting
- **10 requests per minute** per client (all tiers)
- Exceeding returns HTTP 429 with `Retry-After` header
- Design API calls efficiently using caching

### Account Tiers
| Tier | Cloud Save | User Logs |
|------|------------|-----------|
| FREE | 1 KB | Not available |
| BASIC | 5 KB | Available |
| PRO | 20 KB | Available |
| ENTERPRISE | 250 KB | Available |

---

## User Management

Base path: `/api/v1/app/user-management`
**Manager**: `UserManager`

### 1. Sign Up

**Endpoint**: `POST /api/v1/app/user-management/signup`

**Description**: Create a new user account. Supports ANONYMOUS, EMAIL, and GOOGLE authentication.

**Request Body**:
```json
{
  "type": "EMAIL | ANONYMOUS | GOOGLE",
  "username": "string (max 30 chars, optional)",
  "email": "string (max 40 chars, required for EMAIL)",
  "password": "string (4-32 chars, required for EMAIL)",
  "anonymousToken": "string (max 32 chars, for ANONYMOUS)",
  "googleAuthorizationCode": "string (max 2000 chars, for GOOGLE)",
  "googleRedirectUri": "string (for GOOGLE, empty string for mobile apps)"
}
```

**Response (201 Created)**:
```json
{
  "userId": "uuid",
  "username": "string",
  "email": "string (nullable)",
  "isAnonymous": "boolean",
  "isVerified": "boolean",
  "anonymousToken": "string (nullable)",
  "createdAt": "ISO-8601 datetime"
}
```

**Error Responses**:
| Code | Cause | Solution |
|------|-------|----------|
| 400 | Invalid data or user exists | Check input, try different email |
| 401 | Invalid API key | Verify API key in config |
| 429 | Rate limit exceeded | Wait before retrying |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Anonymous sign-up (recommended for new players)
bool success = await UserManager.Instance.SignUpAnonymous("GuestPlayer");

// Email sign-up
bool success = await UserManager.Instance.SignUpEmail(
    "user@example.com",
    "password123",
    "DisplayName"  // optional
);

// Google sign-up (requires Google Sign-In SDK)
bool success = await UserManager.Instance.SignUpGoogle(
    googleAuthorizationCode,   // from Google Sign-In SDK
    redirectUri: "",           // empty for mobile (Android/iOS)
    username: "DisplayName"    // optional
);

// Error handling
if (!success)
{
    HorizonApp.Log.Error("Sign-up failed - user may already exist");
}
```

---

### 2. Sign In

**Endpoint**: `POST /api/v1/app/user-management/signin`

**Description**: Authenticate an existing user.

**Request Body**:
```json
{
  "type": "EMAIL | ANONYMOUS | GOOGLE",
  "email": "string (for EMAIL)",
  "password": "string (for EMAIL)",
  "anonymousToken": "string (for ANONYMOUS)",
  "googleAuthorizationCode": "string (for GOOGLE)",
  "googleRedirectUri": "string (for GOOGLE, empty string for mobile apps)"
}
```

**Response (200 OK)**:
```json
{
  "userId": "uuid",
  "username": "string",
  "email": "string (nullable)",
  "accessToken": "string",
  "authStatus": "AUTHENTICATED",
  "message": "string (nullable)"
}
```

**Auth Status Values**:
| Status | HTTP Code | Meaning |
|--------|-----------|---------|
| AUTHENTICATED | 200 | Success |
| USER_NOT_FOUND | 404 | User doesn't exist |
| INVALID_CREDENTIALS | 401 | Wrong password/token |
| USER_NOT_VERIFIED | 403 | Email not verified |
| USER_DEACTIVATED | 403 | Account deactivated |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Email sign-in
bool success = await UserManager.Instance.SignInEmail("user@example.com", "password123");

// Anonymous sign-in (with known token)
bool success = await UserManager.Instance.SignInAnonymous(savedToken);

// Restore anonymous session (uses cached token)
if (UserManager.Instance.HasCachedAnonymousToken())
{
    bool success = await UserManager.Instance.RestoreAnonymousSession();
}

// Google sign-in
bool success = await UserManager.Instance.SignInGoogle(
    googleAuthCode,    // from Google Sign-In SDK
    redirectUri: ""    // empty for mobile (Android/iOS)
);

// Check result and access user data
if (success && UserManager.Instance.IsSignedIn)
{
    var user = UserManager.Instance.CurrentUser;
    Debug.Log($"Welcome back, {user.DisplayName}!");
    Debug.Log($"User ID: {user.UserId}");
    Debug.Log($"Auth Type: {user.AuthType}");
}
else
{
    // Handle specific error cases
    HorizonApp.Log.Error("Sign-in failed - check credentials or verify email");
}
```

---

### 3. Check Authentication

**Endpoint**: `POST /api/v1/app/user-management/check-auth`

**Description**: Verify if session token is still valid.

**Request Body**:
```json
{
  "userId": "uuid",
  "sessionToken": "string"
}
```

**Response (200 OK)**:
```json
{
  "userId": "uuid (nullable)",
  "isAuthenticated": "boolean",
  "authStatus": "AUTHENTICATED | TOKEN_EXPIRED | INVALID_TOKEN | ...",
  "message": "string (nullable)"
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Call on game startup to validate saved session
bool isValid = await UserManager.Instance.CheckAuth();

if (isValid)
{
    Debug.Log("Session restored successfully");
    // Proceed with authenticated user
}
else
{
    Debug.Log("Session expired - require re-authentication");
    // Show login UI
}
```

---

### 4. Verify Email

**Endpoint**: `POST /api/v1/app/user-management/verify-email`

**Description**: Verify email address using token from email link.

**Request Body**:
```json
{
  "token": "string (max 256 chars)"
}
```

**Response**: 200 OK on success, 400 on invalid/expired token

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Extract token from deep link URL
string verificationToken = ExtractTokenFromDeepLink(url);

bool success = await UserManager.Instance.VerifyEmail(verificationToken);

if (success)
{
    Debug.Log("Email verified successfully!");
}
else
{
    Debug.Log("Verification failed - token may be expired");
}
```

---

### 5. Forgot Password

**Endpoint**: `POST /api/v1/app/user-management/forgot-password`

**Description**: Request password reset email. Always returns success to prevent email enumeration.

**Request Body**:
```json
{
  "email": "string (max 254 chars)"
}
```

**Response**: 200 OK (always)

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

bool success = await UserManager.Instance.ForgotPassword("user@example.com");

// Always show generic message (for security)
Debug.Log("If an account exists, a password reset email has been sent.");
```

---

### 6. Reset Password

**Endpoint**: `POST /api/v1/app/user-management/reset-password`

**Description**: Set new password using reset token from email.

**Request Body**:
```json
{
  "token": "string (max 256 chars)",
  "newPassword": "string (4-128 chars)"
}
```

**Response**: 200 OK on success, 400 on invalid token

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Extract token from deep link
string resetToken = ExtractTokenFromDeepLink(url);
string newPassword = passwordInput.text;

// Validate password length client-side
if (newPassword.Length < 4 || newPassword.Length > 128)
{
    Debug.LogError("Password must be 4-128 characters");
    return;
}

bool success = await UserManager.Instance.ResetPassword(resetToken, newPassword);

if (success)
{
    Debug.Log("Password reset successfully - please sign in");
}
else
{
    Debug.Log("Reset failed - token may be expired");
}
```

---

### 7. Change Name

**Endpoint**: `POST /api/v1/app/user-management/change-name`

**Description**: Update display name for authenticated user.

**Request Body**:
```json
{
  "userId": "uuid",
  "sessionToken": "string",
  "newName": "string (1-50 chars)"
}
```

**Response (200 OK)**:
```json
{
  "isAuthenticated": "boolean",
  "authStatus": "AUTHENTICATED | TOKEN_EXPIRED | ...",
  "message": "string (nullable)"
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Requires user to be signed in
if (!UserManager.Instance.IsSignedIn)
{
    Debug.LogError("Must be signed in to change name");
    return;
}

bool success = await UserManager.Instance.ChangeName("NewDisplayName");

if (success)
{
    Debug.Log($"Name changed to: {UserManager.Instance.CurrentUser.DisplayName}");
}
else
{
    Debug.Log("Name change failed - session may have expired");
}
```

---

## Gift Codes

Base path: `/api/v1/app/gift-codes`
**Manager**: `GiftCodeManager`

### 8. Redeem Gift Code

**Endpoint**: `POST /api/v1/app/gift-codes/redeem`

**Description**: Redeem a promotional code for rewards.

**Headers**: `X-API-Key` and `Authorization: Bearer <accessToken>` of the signed-in player. The SDK sends the
current session automatically. The server only redeems for the player who owns that session.

**Request Body**:
```json
{
  "code": "string (max 50 chars)",
  "userId": "uuid"
}
```

**Response (200 OK)**:
```json
{
  "success": "boolean",
  "message": "string",
  "giftData": "string (JSON with rewards)",
  "grantedUnlocks": ["string (cosmetic ID)"]
}
```

`grantedUnlocks` lists the cosmetic IDs from the code's `grants` that the player owns after
this redemption (newly unlocked or owned before), `[]` when the code grants nothing. See
[Player Profile](#player-profile). When it is not empty, the SDK drops the cached
`PlayerProfileManager.CurrentProfile`, so the next `GetProfile()` shows the unlock.

**Example giftData**:
```json
{
  "gold": 100,
  "crystals": 50,
  "items": ["sword", "shield"],
  "grants": ["badge.supporter"]
}
```

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Invalid/expired/already redeemed |
| 401 | Session missing, invalid or expired (sign in again) |
| 403 | Code doesn't belong to API key, or the session belongs to another player |
| 404 | Code not found |
| 409 | `UNLOCK_LIMIT_REACHED`: the player would hold more than 25 unlocks; the code is not used up |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Requires user to be signed in
if (!UserManager.Instance.IsSignedIn)
{
    Debug.LogError("Must be signed in to redeem codes");
    return;
}

var result = await GiftCodeManager.Instance.Redeem("SUMMER2024");

if (result != null && result.success)
{
    Debug.Log("Code redeemed successfully!");

    // Parse rewards from giftData (JSON string)
    if (!string.IsNullOrEmpty(result.giftData))
    {
        // Parse JSON and grant rewards
        var rewards = JsonUtility.FromJson<RewardsData>(result.giftData);
        GrantRewards(rewards);
    }

    // Cosmetics unlocked by the code (player profile)
    foreach (var cosmeticId in result.grantedUnlocks)
    {
        Debug.Log($"Unlocked: {cosmeticId}");
    }
}
else
{
    // Redeem returns null on failure; details are in the SDK log.
    // Common causes:
    // - "Code already redeemed"
    // - "Code expired"
    // - "Code not found"
    Debug.Log("Redemption failed");
}
```

---

### 9. Validate Gift Code

**Endpoint**: `POST /api/v1/app/gift-codes/validate`

**Description**: Check if code is valid without redeeming.

**Request Body**:
```json
{
  "code": "string (max 50 chars)",
  "userId": "uuid"
}
```

**Response (200 OK)**:
```json
{
  "valid": "boolean"
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Validate before showing redeem confirmation
bool? isValid = await GiftCodeManager.Instance.Validate("SUMMER2024");

if (isValid == null)
{
    Debug.Log("Validation request failed");
}
else if (isValid == true)
{
    Debug.Log("Code is valid - show redemption UI");
}
else
{
    Debug.Log("Code is invalid, expired, or already redeemed");
}
```

---

## User Logs

Base path: `/api/v1/app/user-logs`
**Manager**: `UserLogManager`

> **Note**: User Logs are NOT available for FREE tier accounts.

### 10. Create User Log

**Endpoint**: `POST /api/v1/app/user-logs/create`

**Description**: Create server-side log entry for analytics/debugging.

**Request Body**:
```json
{
  "message": "string (max 1000 chars)",
  "errorCode": "string (max 50 chars, optional)",
  "type": "INFO | WARN | ERROR",
  "userId": "uuid"
}
```

**Response (201 Created)**:
```json
{
  "id": "uuid",
  "createdAt": "ISO-8601 datetime"
}
```

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Invalid request |
| 403 | FREE tier or user mismatch |
| 429 | Rate limit exceeded |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Enums;

// Requires user to be signed in
if (!UserManager.Instance.IsSignedIn)
{
    return; // Silently skip if not authenticated
}

// Convenience methods
await UserLogManager.Instance.Info("Player completed tutorial");
await UserLogManager.Instance.Warn("Low memory detected");
await UserLogManager.Instance.Error("Failed to load asset", errorCode: "ASSET_001");

// Generic method with full control
var result = await UserLogManager.Instance.CreateLog(
    LogType.ERROR,
    "Critical error occurred",
    errorCode: "CRITICAL_001"
);

if (result != null)
{
    Debug.Log($"Log created: {result.Id} at {result.CreatedAt}");
}
else
{
    // Common failure: FREE tier account
    Debug.Log("Logging failed - may require PRO tier");
}
```

**Best Practices**:
- Use sparingly (counts against rate limit)
- Log only significant events
- Message auto-truncates at 1000 characters
- Error code auto-truncates at 50 characters

---

## User Feedback

Base path: `/api/v1/app/user-feedback`
**Manager**: `FeedbackManager`

### 11. Submit Feedback

**Endpoint**: `POST /api/v1/app/user-feedback/submit`

**Description**: Submit user feedback (bugs, features, general).

**Request Body**:
```json
{
  "title": "string (1-100 chars)",
  "message": "string (1-2048 chars)",
  "userId": "uuid"
}
```

**Response (200 OK)**: `"ok"`

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Validation errors |
| 403 | Feedback limit exceeded |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Bug report (auto-includes device info)
bool success = await FeedbackManager.Instance.ReportBug(
    title: "Game crashes on level 5",
    message: "The game crashes when opening the inventory on level 5. " +
             "Steps to reproduce: 1. Start level 5, 2. Open inventory, 3. Crash",
    email: "player@example.com"  // optional
);

// Feature request
bool success = await FeedbackManager.Instance.RequestFeature(
    title: "Add dark mode",
    message: "Please add a dark mode option for the UI to reduce eye strain"
);

// General feedback
bool success = await FeedbackManager.Instance.SendGeneral(
    title: "Great game!",
    message: "Really enjoying the gameplay, keep up the good work!"
);

// Full control method
bool success = await FeedbackManager.Instance.Submit(
    title: "Custom feedback",
    category: "SUPPORT",
    message: "Need help with my account",
    email: "user@example.com",
    includeDeviceInfo: true  // Captures Unity version, OS, device model, etc.
);

if (success)
{
    Debug.Log("Feedback submitted - thank you!");
}
else
{
    Debug.Log("Failed to submit feedback");
}
```

**Device Info Captured** (when `includeDeviceInfo: true`):
- Unity version
- Operating system
- Device model
- Graphics device

---

## Remote Config

Base path: `/api/v1/app/remote-config`
**Manager**: `RemoteConfigManager`

### 12. Get Config Value

**Endpoint**: `GET /api/v1/app/remote-config/{configKey}`

**Description**: Get single configuration value.

**Response (200 OK)**:
```json
{
  "configKey": "string",
  "configValue": "string (nullable)",
  "found": "boolean"
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Get single config (cached by default)
string value = await RemoteConfigManager.Instance.GetConfig("welcome_message");

// Force fresh fetch
string value = await RemoteConfigManager.Instance.GetConfig("welcome_message", useCache: false);
```

---

### 13. Get All Config Values

**Endpoint**: `GET /api/v1/app/remote-config/all`

**Description**: Get all configurations as key-value map.

**Response (200 OK)**:
```json
{
  "configs": {
    "game.max_level": "100",
    "game.daily_reward": "500"
  },
  "total": 2
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Get all configs (recommended at startup)
var configs = await RemoteConfigManager.Instance.GetAllConfigs();

if (configs != null)
{
    foreach (var kvp in configs)
    {
        Debug.Log($"{kvp.Key}: {kvp.Value}");
    }
}

// Type-safe getters with defaults
string welcome = await RemoteConfigManager.Instance.GetString("welcome_message", "Welcome!");
int maxLives = await RemoteConfigManager.Instance.GetInt("max_lives", 3);
float speed = await RemoteConfigManager.Instance.GetFloat("player_speed", 1.0f);
bool featureOn = await RemoteConfigManager.Instance.GetBool("new_feature", false);

// Clear cache to force refresh
RemoteConfigManager.Instance.ClearCache();
```

**Best Practices**:
- Load all configs at startup (1 request vs. N requests)
- Use caching (`useCache: true` default)
- Provide sensible default values

---

## News

Base path: `/api/v1/app/news`
**Manager**: `NewsManager`

### 14. Load News

**Endpoint**: `GET /api/v1/app/news`

**Query Parameters**:
- `limit` (optional): 0-100, default 20
- `languageCode` (optional): ISO 639-1 code (e.g., "en", "de")

**Response (200 OK)**:
```json
[
  {
    "id": "uuid",
    "title": "string",
    "message": "string",
    "releaseDate": "ISO-8601 datetime",
    "languageCode": "string"
  }
]
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Load news (cached for 5 minutes)
var news = await NewsManager.Instance.LoadNews();

// Load with parameters
var news = await NewsManager.Instance.LoadNews(
    limit: 10,
    languageCode: "en",
    useCache: false  // Force fresh fetch
);

if (news != null)
{
    foreach (var item in news)
    {
        Debug.Log($"[{item.ReleaseDate}] {item.Title}");
        Debug.Log(item.Message);
    }
}

// Get specific news from cache
var specificNews = NewsManager.Instance.GetNewsById("news-uuid-123");

// Clear cache
NewsManager.Instance.ClearCache();
```

**Caching**: News is cached for 5 minutes (300 seconds) by default.

---

## Cloud Save

Base path: `/api/v1/app/cloud-save`
**Manager**: `CloudSaveManager`

All save and load operations require the signed-in player's Bearer session in addition
to the API key. The SDK sends it automatically and rejects missing or mismatched sessions
before making a request.

### 15. Save Cloud Data

**Endpoint**: `POST /api/v1/app/cloud-save/save`

**Request Body (JSON mode)**:
```json
{
  "userId": "uuid",
  "saveData": "string (UTF-8, max 300,000 chars)"
}
```

**Request Body (Binary mode)**:
- Query: `?userId={uuid}`
- Content-Type: `application/octet-stream`
- Body: Raw bytes

**Response (200 OK)**:
```json
{
  "success": "boolean",
  "dataSizeBytes": "integer"
}
```

**Size Limits by Tier**:
| Tier | Limit |
|------|-------|
| FREE | 1,000 bytes |
| BASIC | 5,000 bytes |
| PRO | 20,000 bytes |
| ENTERPRISE | 250,000 bytes |

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Invalid request |
| 401 | Missing or invalid player session |
| 403 | Size limit exceeded |
| 429 | Rate limit exceeded |

---

### 16. Load Cloud Data

**Endpoint**: `POST /api/v1/app/cloud-save/load`

**Request Body**:
```json
{
  "userId": "uuid"
}
```

JSON and binary loading use the same JSON request body. For binary loading,
`LoadBytes()` sends `Accept: application/octet-stream`. The response contains raw bytes
on HTTP 200, or HTTP 204 when no save exists (`LoadBytes()` returns `null`).

**Response (200 OK)**:
```json
{
  "found": "boolean",
  "saveData": "string (nullable)"
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Define your save data structure
[System.Serializable]
public class GameSaveData
{
    public int Level;
    public int Coins;
    public List<string> UnlockedItems;
    public string LastSaved;
}

// SAVE - Object serialization (recommended)
var saveData = new GameSaveData
{
    Level = 10,
    Coins = 5000,
    UnlockedItems = new List<string> { "sword", "shield" },
    LastSaved = System.DateTime.UtcNow.ToString("O")
};

bool saved = await CloudSaveManager.Instance.SaveObject(saveData);

if (saved)
{
    Debug.Log("Game saved to cloud!");
}
else
{
    // Handle errors
    Debug.LogError("Save failed - check size limits or authentication");
}

// LOAD - Object deserialization
var loaded = await CloudSaveManager.Instance.LoadObject<GameSaveData>();

if (loaded != null)
{
    Debug.Log($"Loaded: Level {loaded.Level}, Coins {loaded.Coins}");
}
else
{
    // No save found - use defaults
    Debug.Log("No cloud save found, starting fresh");
    loaded = new GameSaveData { Level = 1, Coins = 0 };
}

// RAW STRING methods
await CloudSaveManager.Instance.Save(jsonString);
string json = await CloudSaveManager.Instance.Load();

// BINARY methods (for custom formats, compressed data)
await CloudSaveManager.Instance.SaveBytes(binaryData);
byte[] bytes = await CloudSaveManager.Instance.LoadBytes();
```

**Best Practices**:
- Save on natural breakpoints (level complete, quit game)
- Don't save on every frame or minor change
- Check save size against tier limits
- Use `[System.Serializable]` attribute on data classes

---

## Leaderboard

Base path: `/api/v1/app/leaderboard`
**Manager**: `LeaderboardManager`

### 17. Submit Score

**Endpoint**: `POST /api/v1/app/leaderboard/submit`

**Description**: Submit score (only updates if higher than previous).

**Headers**: `X-API-Key` and `Authorization: Bearer <accessToken>` of the signed-in player (sent by the SDK).

**Request Body**:
```json
{
  "userId": "uuid",
  "score": "long (>= 0)"
}
```

There is no `metadata` field: the server never stored score metadata. The `metadata`
parameter of `SubmitScore(long score, string metadata = null, string boardKey = null)` is
deprecated, ignored and not sent. It stays only for source compatibility and will be removed
in the next major version. Pass a board key as a named argument: `SubmitScore(12500, boardKey: "weekly")`.

**Response**: 200 OK on success

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Invalid request |
| 403 | Entry limit exceeded |
| 403 | `VALIDATED_SUBMIT_REQUIRED`: the board only accepts validated runs (see [Validated Actions](#validated-actions)); nothing is written |
| 403 | `PLAYER_BANNED`: the player is banned from this board; nothing is written, not retried |

On failure `LeaderboardManager.Instance.LastErrorCode` holds the server `code` (for example
`VALIDATED_SUBMIT_REQUIRED` or `PLAYER_BANNED`), `SESSION_REQUIRED` without a signed-in player, or an HTTP
fallback code. `ListBoards()` returns `validatedOnly` (bool) for every board.

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Submit score (only saves if higher than previous best)
bool submitted = await LeaderboardManager.Instance.SubmitScore(12500);

if (submitted)
{
    Debug.Log("Score submitted!");
    // Clear leaderboard cache to see updated rankings
    LeaderboardManager.Instance.ClearCache();
}
else
{
    Debug.Log("Score submission failed");
}

// Submit to a named board of a multi-board leaderboard
bool weekly = await LeaderboardManager.Instance.SubmitScore(12500, boardKey: "weekly");
```

---

### 18. Get Top Entries

**Endpoint**: `GET /api/v1/app/leaderboard/top`

**Query Parameters**:
- `userId` (required): UUID
- `limit` (optional): max 100, default 100

**Response (200 OK)**:
```json
{
  "entries": [
    {
      "position": 1,
      "username": "string",
      "score": 1000,
      "profile": { "avatarId": "avatar.zombie_07", "frameId": null, "badges": ["badge.supporter"] }
    }
  ]
}
```

Every entry carries the player's `profile` (see [Player Profile](#player-profile)). In Unity
`entry.profile` is never null; JSON `null` IDs read as `""`, check `HasAvatar` / `HasFrame`.

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Get top 10 players (cached by default)
var topPlayers = await LeaderboardManager.Instance.GetTop(10);

if (topPlayers != null)
{
    foreach (var entry in topPlayers)
    {
        Debug.Log($"#{entry.position} {entry.username}: {entry.score}");
        if (entry.profile.HasAvatar)
        {
            // Map entry.profile.avatarId to your sprite; unknown IDs count as "not set"
        }
    }
}

// Force fresh fetch
var topPlayers = await LeaderboardManager.Instance.GetTop(10, useCache: false);
```

---

### 19. Get User Rank

**Endpoint**: `GET /api/v1/app/leaderboard/rank`

**Query Parameters**:
- `userId` (required): UUID

**Response (200 OK)**:
```json
{
  "position": 42,
  "username": "string",
  "score": 1000,
  "profile": { "avatarId": null, "frameId": null, "badges": [] }
}
```

**Error Response**: 404 if user has no entry

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

var myRank = await LeaderboardManager.Instance.GetRank();

if (myRank != null)
{
    Debug.Log($"Your rank: #{myRank.position}");
    Debug.Log($"Your score: {myRank.score}");
    Debug.Log($"Username: {myRank.username}");
    Debug.Log($"Avatar: {(myRank.profile.HasAvatar ? myRank.profile.avatarId : "(none)")}");
}
else
{
    Debug.Log("You haven't submitted a score yet");
}
```

---

### 20. Get Entries Around User

**Endpoint**: `GET /api/v1/app/leaderboard/around`

**Query Parameters**:
- `userId` (required): UUID
- `range` (optional): entries above/below, default 10

**Response (200 OK)**:
```json
{
  "entries": [
    { "position": 8, "username": "Player8", "score": 950, "profile": { "avatarId": "avatar.zombie_07", "frameId": null, "badges": [] } },
    { "position": 9, "username": "CurrentUser", "score": 920, "profile": { "avatarId": null, "frameId": null, "badges": [] } },
    { "position": 10, "username": "Player10", "score": 900, "profile": { "avatarId": null, "frameId": "frame.gold", "badges": ["badge.supporter"] } }
  ]
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Get players around your position (5 above, 5 below)
var nearby = await LeaderboardManager.Instance.GetAround(5);

if (nearby != null)
{
    foreach (var entry in nearby)
    {
        string marker = entry.username == UserManager.Instance.CurrentUser.DisplayName
            ? " <-- YOU" : "";
        Debug.Log($"#{entry.position} {entry.username}: {entry.score}{marker}");
    }
}
else
{
    Debug.Log("You haven't submitted a score yet");
}

// Force fresh fetch
var nearby = await LeaderboardManager.Instance.GetAround(5, useCache: false);
```

---

## Player Profile

Base path: `/api/v1/app/player-profile`
**Manager**: `PlayerProfileManager`

Leaderboards show an avatar, an optional frame and up to 3 badges next to name and score.
Each API key has a cosmetics catalog, maintained in the horizOn Dashboard: every entry has an
ID, a type (`avatar`, `frame`, `badge`) and `locked`. Free entries can be selected by every
player, locked entries only after an unlock (gift code with `grants`, or the Dashboard). The
server stores IDs only; the game maps them to its own assets and treats unknown IDs as "not set".

**Headers** (both endpoints): `X-API-Key` and `Authorization: Bearer <accessToken>` of the
signed-in player. The SDK sends the current session automatically. Without a signed-in player
both methods fail locally (no request) with `LastErrorCode = "SESSION_REQUIRED"`.

**Cosmetic ID format**: 1 to 32 characters, `^[a-z0-9][a-z0-9._-]{0,31}$`.

### 21. Get Player Profile

**Endpoint**: `GET /api/v1/app/player-profile?userId={uuid}`

**Description**: Profile, unlocks and the full catalog of the API key with an `available`
flag per entry, so a game builds its picker from one call.

**Response (200 OK)**:
```json
{
  "userId": "0d7e...",
  "profile": { "avatarId": "avatar.zombie_07", "frameId": null, "badges": ["badge.supporter"] },
  "unlocks": ["badge.supporter"],
  "cosmetics": [
    { "id": "avatar.zombie_07", "type": "avatar", "locked": false, "available": true },
    { "id": "badge.supporter", "type": "badge", "locked": true, "available": true },
    { "id": "frame.gold", "type": "frame", "locked": true, "available": false }
  ],
  "limits": { "maxBadges": 3, "maxUnlocks": 25 }
}
```

`cosmetics` is sorted by `id`, `available = !locked || id in unlocks`. `unlocks` may contain
IDs that were deleted from the catalog.

**Error Responses**:
| Code | Cause |
|------|-------|
| 401 `SESSION_REQUIRED` | Session missing, invalid or expired |
| 401 (no code) | Invalid API key |
| 403 `SESSION_FORBIDDEN` | Session of another player |
| 404 `PLAYER_NOT_FOUND` | Player missing, deleted, inactive or of another API key |
| 429 | Rate limit; the SDK retries after `Retry-After` |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;

PlayerProfileResponse profile = await PlayerProfileManager.Instance.GetProfile();
if (profile == null)
{
    Debug.Log($"Loading failed: {PlayerProfileManager.Instance.LastErrorCode}");
    return;
}

// Build the pickers from the catalog
foreach (PlayerCosmetic avatar in profile.GetCosmetics("avatar"))
{
    Debug.Log($"{avatar.id} locked={avatar.locked} available={avatar.available}");
}

bool canUseGoldFrame = profile.IsAvailable("frame.gold");
```

`GetProfile()` has no time based cache: every call asks the server, so new unlocks show up
right away. The last result stays in `PlayerProfileManager.Instance.CurrentProfile`.

**Event**: `EventKeys.PlayerProfileLoaded` (307) with the `PlayerProfileResponse`.

---

### 22. Set Player Profile

**Endpoint**: `PUT /api/v1/app/player-profile`

**Description**: Replace the whole visible profile. Returns the same body as GET.

**Request Body**:
```json
{
  "userId": "uuid",
  "avatarId": "string or null",
  "frameId": "string or null",
  "badges": ["string"]
}
```

A missing, `null` or empty `avatarId` / `frameId` clears the slot; missing or `[]` badges clear
all badges. At most 3 distinct badges, order kept. The server checks, in this order: badge count
and duplicates (`INVALID_BADGES`), then per ID the format (`INVALID_COSMETIC_ID`), the catalog
(`COSMETIC_NOT_FOUND`), the type (`COSMETIC_TYPE_MISMATCH`) and the unlock (`COSMETIC_LOCKED`).

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 `INVALID_BADGES` | More than 3 badges, or a badge listed twice |
| 400 `INVALID_COSMETIC_ID` | ID does not match the format |
| 400 `COSMETIC_NOT_FOUND` | ID is not in the catalog of the API key |
| 400 `COSMETIC_TYPE_MISMATCH` | ID exists with another type than the slot |
| 401 `SESSION_REQUIRED` | Session missing, invalid or expired |
| 403 `COSMETIC_LOCKED` | Locked cosmetic and the player has no unlock |
| 403 `SESSION_FORBIDDEN` | Session of another player |
| 404 `PLAYER_NOT_FOUND` | See GET |
| 429 | Rate limit; the SDK retries after `Retry-After` |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;

var current = PlayerProfileManager.Instance.CurrentProfile
              ?? await PlayerProfileManager.Instance.GetProfile();

// PUT replaces everything: pass the current frame to keep it.
// null or "" clears a slot, null or an empty list clears the badges.
PlayerProfileResponse updated = await PlayerProfileManager.Instance.SetProfile(
    "avatar.zombie_07",
    current?.profile.frameId,
    new[] { "badge.supporter" });

if (updated == null)
{
    switch (PlayerProfileManager.Instance.LastErrorCode)
    {
        case PlayerProfileErrorCodes.CosmeticLocked:
            Debug.Log("Unlock this cosmetic first");
            break;
        case PlayerProfileErrorCodes.SessionRequired:
            Debug.Log("Sign in first");
            break;
        default:
            Debug.Log($"Saving failed: {PlayerProfileManager.Instance.LastErrorCode}");
            break;
    }
}
```

The SDK checks locally before sending (no request on failure): signed-in player
(`SESSION_REQUIRED`), more than 3 or duplicate badges (`INVALID_BADGES`) and the ID format
(`INVALID_COSMETIC_ID`). IDs are trimmed like on the server.

On success the SDK also clears the `LeaderboardManager` cache, so the next `GetTop()` /
`GetAround()` shows the new profile (other pods may serve the old one for up to 10 minutes).

**Event**: `EventKeys.PlayerProfileChanged` (204) with the `PlayerProfileResponse`.

### PlayerProfileManager members

| Member | Description |
|--------|-------------|
| `Task<PlayerProfileResponse> GetProfile()` | Load profile, unlocks and catalog; `null` on failure |
| `Task<PlayerProfileResponse> SetProfile(string avatarId, string frameId, IList<string> badges)` | Replace the profile; `null` on failure |
| `PlayerProfileResponse CurrentProfile` | Last result; `null` before the first call, after `ClearCache()`, after sign-out and when another player signed in |
| `string LastErrorCode` | Code of the last failure (see below); `null` after a success |
| `void ClearCache()` | Drop `CurrentProfile` (called by `GiftCodeManager.Redeem` when `grantedUnlocks` is not empty) |

`LastErrorCode` holds the server `code` of the error body. Without one it falls back to an
HTTP based code: `BAD_REQUEST` (400), `UNAUTHORIZED` (401, for example an invalid API key),
`FORBIDDEN` (403), `NOT_FOUND` (404), `CONFLICT` (409), `RATE_LIMITED` (429 after the retries),
`SERVER_ERROR` (5xx), `NETWORK_ERROR` (no response), `INVALID_RESPONSE` (unreadable 2xx body).
All codes are constants in `PlayerProfileErrorCodes`.

---

## Validated Actions

Base path: `/api/v1/app/validated-actions`
**Manager**: `ValidatedActionsManager`

Server-checked runs. The game starts a run and gets a single-use ticket with a server seed,
plays deterministically with that seed while it records the input log, and submits the score
with the SHA-256 of the log. The server checks the ticket and every rule of the API key
(score limits, minimum duration measured by the server, score per second, stage rules) before
anything is written. Rule values never appear in responses or error messages; only the `code`
tells which rule rejected a run. Runs may also earn or spend server-owned values (currency,
loot) defined in the rules; only the server writes them (see 25). The server may ask for the
input log of an accepted run as evidence (see 26). Cloud only: without the
endpoints (simpleServer) the SDK reports `NOT_SUPPORTED`.

**Headers** (every endpoint): `X-API-Key` and `Authorization: Bearer <accessToken>` of the
signed-in player, sent by the SDK. Without a signed-in player every method fails locally (no
request) with `LastErrorCode = "SESSION_REQUIRED"`.

### 23. Start Validated Run

**Endpoint**: `POST /api/v1/app/validated-actions/runs`

**Request Body**:
```json
{
  "userId": "uuid",
  "leaderboardKey": "weekly",
  "context": {
    "gameVersion": "1.4.2",
    "contentVersion": "levels-7",
    "simulationVersion": "sim-3",
    "replayFormatVersion": "inputs-v1",
    "contentDigest": "64 hex characters (SHA-256 of the content)",
    "initialState": "base64 of the initial state bytes"
  }
}
```
`leaderboardKey` is optional and left out when empty (the ticket is then not bound to a board).
`context` (optional) comes from a `ValidatedRunContext` passed to `StartRun` or from
`DefaultRunContext`: versions of at most 64 printable ASCII characters, the content digest and
the raw initial state, sent as standard base64. Empty fields are left out, and a context without
any field is left out entirely. The server binds it to the run and keeps it with a sus run.

**Response** (200):
```json
{
  "runId": "5b0b6c1e-8d0f-4c55-9b0e-0e6a4a8a3d11",
  "ticket": "hzn-rt1:2026-09:Qm9...:c2Vj...",
  "seed": 1834201177,
  "leaderboardKey": "weekly",
  "issuedAt": "2026-09-29T14:00:00.120Z",
  "expiresAt": "2026-09-29T16:00:00.120Z",
  "expiresInSeconds": 7200
}
```

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Validation (bad key format, bad `context` field), `INITIAL_STATE_INVALID_ENCODING` |
| 401 | `SESSION_REQUIRED` |
| 403 | `SESSION_FORBIDDEN` |
| 404 | `PLAYER_NOT_FOUND`, `LEADERBOARD_NOT_FOUND` |
| 413 | `INITIAL_STATE_TOO_LARGE` (decoded initial state above the game's evidence size limit) |
| 429 | Account request limit (empty body, retried by the SDK), or `RUN_RATE_LIMITED` / `RUN_CAPACITY_REACHED` (not retried) |
| 503 | `VALIDATED_ACTIONS_UNAVAILABLE` |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

ValidatedRun run = await ValidatedActionsManager.Instance.StartRun("weekly");
if (run == null)
{
    Debug.Log(ValidatedActionsManager.Instance.LastErrorCode);
    return;
}
var random = new System.Random(run.seed);

// With a start context (all fields optional)
run = await ValidatedActionsManager.Instance.StartRun("weekly", new ValidatedRunContext(
    Application.version,
    contentVersion: "levels-7",
    contentDigest: ValidatedActionsManager.ComputeInputLogHash(levelBytes),
    initialState: initialStateBytes));
```

The run becomes `CurrentRun`; a new `StartRun` replaces it. A `contentDigest` that is not 64 hex
characters fails locally with `INVALID_CONTENT_DIGEST` (no request).
**Event**: `EventKeys.ValidatedRunStarted` (420) with the `ValidatedRun`.

---

### 24. Submit Validated Run

**Endpoint**: `POST /api/v1/app/validated-actions/submit`

**Request Body**:
```json
{
  "userId": "uuid",
  "ticket": "hzn-rt1:...",
  "inputLogHash": "64 lower case hex characters",
  "score": 18250,
  "stage": "wave_10",
  "leaderboardKey": "weekly",
  "earned": [{ "key": "gold", "amount": 250 }]
}
```
`stage`, `leaderboardKey` and `earned` are left out when empty. Without `leaderboardKey` the
board of the ticket is used; for a run without a board the server ignores `score`. `earned`
(Part 2, at most 64 entries) lists values the run earned (positive) or spent (negative); every
key must be defined under `values` in the rules, so send it only when the game uses server-owned
values.

**Response** (200):
```json
{
  "accepted": true,
  "runId": "5b0b6c1e-8d0f-4c55-9b0e-0e6a4a8a3d11",
  "leaderboardKey": "weekly",
  "score": 18250,
  "bestScore": 21000,
  "isNewHighScore": false,
  "rank": 17,
  "durationSeconds": 734,
  "state": {
    "day": "2026-09-29",
    "values": [
      { "key": "chest.gold", "balance": 1, "earnedToday": 0, "dailyCap": null, "requested": -1, "credited": -1 },
      { "key": "gold", "balance": 1500, "earnedToday": 500, "dailyCap": 500, "requested": 400, "credited": 250 }
    ]
  },
  "evidence": null,
  "sus": false
}
```
`sus` is true when the accepted run crossed a soft threshold of the rules: the score counts, the
server keeps the run with its start context for a review and asks for the input log through
`evidence` (uploaded by the SDK like a top N record). The reasons stay on the server. Older
servers omit the field; the SDK reads it as `false`.
`state` lists every value of the rules; values the run touched carry `requested` and `credited`.
It is `null` when the rules define no values (the SDK keeps an empty `PlayerState`, `HasData`
false, and leaves `CurrentState` unchanged).

**Error Responses**:
| Code | Cause | Run |
|------|-------|-----|
| 400 | Validation, `SCORE_REQUIRED`, `PLAYER_NAME_REQUIRED` | kept |
| 401 / 403 | `SESSION_REQUIRED` / `SESSION_FORBIDDEN` | kept |
| 403 | `SCORE_LIMIT_REACHED` (ticket used up) | dropped |
| 403 | `PLAYER_BANNED` (banned from the target board, checked before the ticket is used) | kept; the same board refuses it again, call `DiscardRun()` |
| 404 | `PLAYER_NOT_FOUND`, `LEADERBOARD_NOT_FOUND` | kept |
| 422 | `TICKET_INVALID`, `TICKET_EXPIRED`, `TICKET_FOREIGN`, `TICKET_CONSUMED` | dropped |
| 422 | `LEADERBOARD_MISMATCH` (checked before the ticket is used) | kept |
| 422 | Rule codes: `STAGE_REQUIRED`, `STAGE_UNKNOWN`, `SCORE_ABOVE_MAX`, `SCORE_BELOW_MIN`, `STAGE_SCORE_ABOVE_MAX`, `STAGE_SCORE_BELOW_MIN`, `DURATION_TOO_SHORT`, `SCORE_RATE_TOO_HIGH` | dropped |
| 422 | Value codes: `UNKNOWN_VALUE_KEY`, `DUPLICATE_VALUE_KEY`, `EARNED_ABOVE_MAX`, `EARNED_BELOW_MIN`, `INSUFFICIENT_BALANCE` | dropped |
| 429 | Account request limit (empty body, retried by the SDK) | kept |
| 503 | `VALIDATED_ACTIONS_UNAVAILABLE` | kept |

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// The SDK hashes the log (SHA-256) and sends the ticket of CurrentRun.
ValidatedSubmitResult result = await ValidatedActionsManager.Instance.SubmitValidated(
    18250, inputLog, stage: "wave_10");
if (result == null)
{
    Debug.Log(ValidatedActionsManager.Instance.LastErrorCode);        // e.g. DURATION_TOO_SHORT
    Debug.Log(ValidatedActionsManager.Instance.HasActiveRun);         // false when the ticket is used up
}

// Or with a ready hash
string hash = ValidatedActionsManager.ComputeInputLogHash(inputLog);
result = await ValidatedActionsManager.Instance.SubmitValidatedWithHash(18250, hash);
```

The SDK checks locally before sending (no request on failure): signed-in player
(`SESSION_REQUIRED`), a current run (`NO_ACTIVE_RUN`), the hash format
(`INVALID_INPUT_LOG_HASH`). An expired run (by the device clock) is still sent; the server
decides. After an accepted run with a board the `LeaderboardManager` cache is cleared.

**Events**: `EventKeys.ValidatedRunSubmitted` (421) with the `ValidatedSubmitResult`;
`EventKeys.ValidatedRunRejected` (422) on a 422 or 403 with a `ValidatedRunRejection`
(`code`, `runId`, `httpStatus`, `runCleared`). When the result carries a state,
`EventKeys.ValidatedStateLoaded` (308) is published first with the new `CurrentState`.
When `result.evidence.required` is true and the submit had the raw log, the SDK starts the
evidence upload (see 26) before it publishes `ValidatedRunSubmitted`; the upload finishes in
the background.

---

### 25. Get Player State

**Endpoint**: `GET /api/v1/app/validated-actions/state?userId={userId}`

The signed-in player's server-owned values. Read only: values change only through `earned` of an
accepted validated run; support corrects them in the dashboard.

**Response** (200):
```json
{
  "userId": "0d7e...",
  "day": "2026-09-29",
  "values": [
    { "key": "chest.gold", "balance": 2, "earnedToday": 0, "dailyCap": null },
    { "key": "gold", "balance": 1250, "earnedToday": 250, "dailyCap": 5000 }
  ]
}
```
Every key defined under `values` in the rules is listed, sorted by key (balance 0 when never
earned); `values` is empty when the rules define none. `day` is the current UTC day,
`earnedToday` the positive credit on that day, `dailyCap` `null` without a cap (the SDK reads 0).

**Error Responses**: 401 `SESSION_REQUIRED`, 403 `SESSION_FORBIDDEN`, 404 `PLAYER_NOT_FOUND`
(404 without a code: `NOT_SUPPORTED`), 429 (empty body, retried by the SDK).

#### Unity SDK Usage

```csharp
PlayerState state = await ValidatedActionsManager.Instance.GetState();
if (state == null)
{
    Debug.Log(ValidatedActionsManager.Instance.LastErrorCode);   // SESSION_REQUIRED, NOT_SUPPORTED, ...
    return;
}
PlayerStateValue gold = state.GetValue("gold");
Debug.Log($"{gold.balance} gold, {gold.earnedToday} / {gold.dailyCap} today");
```

The state becomes `CurrentState`. **Event**: `EventKeys.ValidatedStateLoaded` (308) with the
`PlayerState`.

**Cloud save as a mirror.** The cloud save stays a client-written blob. Keep server-owned values
there only as a copy: copy `CurrentState` (or `result.state`) into the save after each accepted
run, call `GetState()` on start and overwrite the copy with it (never the other way round), never
send a value from the save back as a balance, and send values earned offline as `earned` of the
next validated run (the per-run and daily limits apply as always). `PlayerState` is
`[Serializable]`, so it can be a field of the object you pass to `CloudSaveManager.SaveObject`.

---

### 26. Upload Evidence

**Endpoint**: `PUT /api/v1/app/validated-actions/runs/{runId}/evidence`

Only after a submit answered with `evidence` (`required: true`), before `evidence.uploadBefore`
(24 hours). The server asks for the log when the run became the player's new entry on the board
and either carries a soft flag or lands within the board's "Evidence top N"; when the account's
evidence storage is full it asks for nothing (the run still counts).

**Request Body**:
```json
{
  "userId": "uuid",
  "log": "AAECAw=="
}
```
`log` is the raw input log as standard base64 with padding, decoded at most `evidence.maxBytes`
(32,768) bytes. Its SHA-256 must equal the `inputLogHash` sent with the run.

**Response** (200):
```json
{ "runId": "5b0b6c1e-8d0f-4c55-9b0e-0e6a4a8a3d11", "status": "UPLOADED", "bytes": 18234 }
```

**Error Responses**:
| Code | Cause | Retry |
|------|-------|-------|
| 400 | `EVIDENCE_INVALID_ENCODING`: `log` is not standard base64 | no |
| 401 / 403 | `SESSION_REQUIRED` / `SESSION_FORBIDDEN` | after a new sign-in |
| 404 | `EVIDENCE_NOT_REQUESTED`: no request for this run and player (never asked, replaced by a better run, deleted) | no |
| 409 | `EVIDENCE_ALREADY_UPLOADED` | no |
| 410 | `EVIDENCE_EXPIRED`: the 24 h window passed | no |
| 413 | `EVIDENCE_TOO_LARGE`: decoded log above `maxBytes` | no |
| 422 | `EVIDENCE_HASH_MISMATCH`: the bytes differ from the hashed log; the request stays open | yes, with the exact bytes |
| 429 | Account request limit (empty body, retried by the SDK) | |

#### Unity SDK Usage

```csharp
// Automatic: SubmitValidated knows the raw log and uploads it in the background.
ValidatedSubmitResult result = await ValidatedActionsManager.Instance.SubmitValidated(score, inputLog);

// Manual: after SubmitValidatedWithHash, or with AutoUploadEvidence = false.
if (result != null && result.evidence.required)
{
    bool stored = await ValidatedActionsManager.Instance.UploadEvidence(result.evidence.runId, inputLog);
    if (!stored)
    {
        string code = ValidatedActionsManager.Instance.LastErrorCode;   // e.g. EVIDENCE_EXPIRED
        bool retry = ValidatedActionsManager.IsEvidenceRetryable(code);  // hash mismatch or network error
    }
}
```

The SDK checks locally before sending (no request on failure): signed-in player
(`SESSION_REQUIRED`), a run ID (`INVALID_RUN_ID`), a non-empty log (`EMPTY_INPUT_LOG`) and, for the
automatic upload, `evidence.maxBytes` (`EVIDENCE_TOO_LARGE`). Server errors and timeouts are
retried by the network layer; the SDK does not retry an upload on its own beyond that. An upload
never changes the submit result. `UploadEvidence` sets `LastErrorCode` and `LastEvidenceErrorCode`;
the automatic upload sets only `LastEvidenceErrorCode`.

**Events**: `EventKeys.ValidatedEvidenceUploaded` (423) with the `EvidenceUploadResult`;
`EventKeys.ValidatedEvidenceUploadFailed` (424) with a `ValidatedEvidenceFailure` (`runId`,
`code`, `httpStatus` (0 for local and network failures), `retryable`, `automatic`).

### ValidatedActionsManager members

| Member | Description |
|--------|-------------|
| `Task<ValidatedRun> StartRun(string leaderboardKey = null)` | Start a run; `null` on failure |
| `Task<ValidatedSubmitResult> SubmitValidated(long score, byte[] inputLog, string stage = null, string leaderboardKey = null, IList<EarnedValue> earned = null)` | Hash the log and submit the current run; `null` on failure |
| `Task<ValidatedSubmitResult> SubmitValidatedWithHash(long score, string inputLogHash, string stage = null, string leaderboardKey = null, IList<EarnedValue> earned = null)` | Submit with a ready hash; `null` on failure |
| `static string ComputeInputLogHash(byte[] inputLog)` | SHA-256 as 64 lower case hex characters (`null` hashes like an empty log) |
| `ValidatedRun CurrentRun` | The current run; `null` before the first run, after a final submit, after `DiscardRun()`, after sign-out and when another player signed in |
| `bool HasActiveRun` | `CurrentRun != null` |
| `string LastErrorCode` | Code of the last failure; `null` after a success |
| `void DiscardRun()` | Drop the current run without submitting |
| `bool AutoUploadEvidence` | Default `true`: upload the raw log in the background when an accepted `SubmitValidated` asks for evidence |
| `Task<bool> UploadEvidence(string runId, byte[] inputLog)` | Upload the input log of a run whose submit asked for evidence; `false` on failure |
| `string LastEvidenceErrorCode` | Code of the last failed evidence upload (automatic or manual); `null` after a successful upload |
| `static bool IsEvidenceRetryable(string errorCode)` | `true` for `EVIDENCE_HASH_MISMATCH` and `NETWORK_ERROR` |
| `Task<PlayerState> GetState()` | Load the server-owned values; `null` on failure |
| `PlayerState CurrentState` | Last known state (from `GetState` or the last accepted submit with a state), `requested` and `credited` always 0; `null` before the first load, after sign-out and when another player signed in |

`LastErrorCode` holds the server `code` of the error body, a local code (`SESSION_REQUIRED`,
`NO_ACTIVE_RUN`, `INVALID_INPUT_LOG_HASH`, `INVALID_RUN_ID`, `EMPTY_INPUT_LOG`) or an HTTP fallback: `NOT_SUPPORTED` (404 without a
code, for example a simpleServer), `BAD_REQUEST` (400), `UNAUTHORIZED` (401), `FORBIDDEN` (403),
`UNPROCESSABLE_ENTITY` (422), `RATE_LIMITED` (429 after the retries), `SERVER_ERROR` (5xx),
`NETWORK_ERROR` (no response), `INVALID_RESPONSE` (unreadable 2xx body). All codes are constants
in `ValidatedActionsErrorCodes`.

---

## Data Models

### Enums

#### AuthType / SignUpType / SignInType
```
ANONYMOUS
EMAIL
GOOGLE
```

#### UserAuthStatus
```
AUTHENTICATED
USER_NOT_FOUND
INVALID_CREDENTIALS
USER_NOT_VERIFIED
USER_DEACTIVATED
USER_DELETED
TOKEN_EXPIRED
INVALID_TOKEN
```

#### LogType
```
INFO
WARN
ERROR
```

### SimpleLeaderboardEntry
| Field | Type | Description |
|-------|------|-------------|
| position | long | Rank (1-indexed) |
| username | string | Display name |
| score | long | Score value |
| profile | HorizonPlayerProfile | Visible profile of the player, never null |

`AppUserRankResponse` (from `GetRank()`) has the same four fields.

### HorizonPlayerProfile
| Field | Type | Description |
|-------|------|-------------|
| avatarId | string | Selected avatar, empty when not set |
| frameId | string | Selected frame, empty when not set |
| badges | string[] | Displayed badges (0 to 3), order kept |
| HasAvatar | bool (property) | `avatarId` is not empty |
| HasFrame | bool (property) | `frameId` is not empty |

### PlayerProfileResponse
| Field | Type | Description |
|-------|------|-------------|
| userId | string | The player |
| profile | HorizonPlayerProfile | Current selection |
| unlocks | string[] | Owned locked cosmetics (may contain deleted IDs) |
| cosmetics | PlayerCosmetic[] | Catalog of the API key, sorted by `id` |
| limits | PlayerProfileLimits | `maxBadges` (3), `maxUnlocks` (25) |

Helpers: `List<PlayerCosmetic> GetCosmetics(string type)` and `bool IsAvailable(string id)`.

### PlayerCosmetic
| Field | Type | Description |
|-------|------|-------------|
| id | string | Cosmetic ID |
| type | string | `avatar`, `frame` or `badge` |
| locked | bool | Needs an unlock |
| available | bool | The player may select it now |

### RedeemGiftCodeResponse
| Field | Type | Description |
|-------|------|-------------|
| success | bool | Redemption succeeded |
| message | string | Server message |
| giftData | string | JSON string with the rewards |
| grantedUnlocks | string[] | Cosmetic IDs the code unlocked (owned after this redemption), empty when none |

### ValidatedRun
| Field | Type | Description |
|-------|------|-------------|
| runId | string | Ticket ID |
| ticket | string | Opaque token, sent unchanged |
| seed | int | Server seed, 0 to 2,147,483,646 |
| leaderboardKey | string | Bound board, empty when unbound |
| issuedAt, expiresAt | string | ISO 8601 UTC |
| expiresInSeconds | int | Lifetime at issue |

Helpers: `HasLeaderboard`, `ExpiresAtUtc`, `IsExpired` (device clock, informational).

### ValidatedSubmitResult
| Field | Type | Description |
|-------|------|-------------|
| accepted | bool | Always true on success |
| runId | string | The run |
| leaderboardKey | string | Board written to, empty without a board |
| score | long | Submitted score (0 without a board) |
| bestScore | long | Player's row after the write (0 without a board) |
| isNewHighScore | bool | New best score |
| rank | long | 1-based rank (0 without a board) |
| durationSeconds | long | Server-measured duration |
| state | PlayerState | Never null; `HasData` false when the server sent no state |
| evidence | EvidenceRequest | Never null; `required` false when the server does not ask for the log |

### EvidenceRequest
| Field | Type | Description |
|-------|------|-------------|
| required | bool | The server wants the input log of this run |
| runId | string | Run whose log is requested (empty when not required) |
| uploadBefore | string | Deadline, ISO 8601 UTC (24 h after the submit) |
| maxBytes | int | Maximum log size in bytes (32,768) |

Helper: `UploadBeforeUtc` (`DateTime?`).

### EvidenceUploadResult
| Field | Type | Description |
|-------|------|-------------|
| runId | string | The run |
| status | string | `UPLOADED` |
| bytes | int | Stored log size |

### EarnedValue
| Field | Type | Description |
|-------|------|-------------|
| key | string | Value key, `^[a-z0-9][a-z0-9._-]{0,23}$` |
| amount | long | Earned (positive) or spent (negative) |

### PlayerState
| Field | Type | Description |
|-------|------|-------------|
| day | string | UTC day of `earnedToday`, empty when the server sent no state |
| values | PlayerStateValue[] | One entry per value key, sorted by key, never null |

Helpers: `GetValue(key)` (null when missing), `GetBalance(key)` (0 when missing), `IsEmpty`,
`HasData`, `WithoutRunDetails()` (copy with `requested` and `credited` set to 0).

### PlayerStateValue
| Field | Type | Description |
|-------|------|-------------|
| key | string | Value key |
| balance | long | Current balance |
| earnedToday | long | Positive credit on `day` |
| dailyCap | long | Daily cap, 0 when none (JSON `null`) |
| requested | long | Amount the run sent (submit results only, touched values; 0 otherwise) |
| credited | long | Amount applied (submit results only, touched values; 0 otherwise) |

Helpers: `HasDailyCap`, `RemainingToday` (`long.MaxValue` without a cap), `IsFullyCredited`
(`credited == requested`; grant a purchase paid with a spend only when true).

### UserNewsResponse
| Field | Type | Description |
|-------|------|-------------|
| id | uuid | News entry ID |
| title | string | News title |
| message | string | News content |
| releaseDate | datetime | Publication date |
| languageCode | string | ISO 639-1 code |

---

## Error Handling

### Standard HTTP Status Codes

| Code | Meaning | Unity SDK Handling |
|------|---------|-------------------|
| 200 | OK | Success, process response |
| 201 | Created | Resource created successfully |
| 204 | No Content | No data found (binary cloud save) |
| 400 | Bad Request | Check input parameters |
| 401 | Unauthorized | Invalid API key, re-authenticate |
| 403 | Forbidden | Tier limit, wrong user, check permissions |
| 404 | Not Found | Resource doesn't exist |
| 409 | Conflict | For example `UNLOCK_LIMIT_REACHED` on a gift code with grants |
| 429 | Rate Limited | Wait and retry with backoff |

Player profile errors carry a stable `code` in the JSON body (`COSMETIC_LOCKED`, ...). The
Unity SDK exposes it as `PlayerProfileManager.Instance.LastErrorCode`; switch on the code,
never on the message.
| 500 | Server Error | Retry with exponential backoff |

### Rate Limit Handling

```csharp
// Example: Retry with exponential backoff
private async Task<bool> SubmitWithRetry(long score, int maxRetries = 3)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        bool success = await LeaderboardManager.Instance.SubmitScore(score);

        if (success)
            return true;

        // Exponential backoff: 1s, 2s, 4s
        int delay = (int)Math.Pow(2, attempt) * 1000;
        await Task.Delay(delay);

        HorizonApp.Log.Warn($"Retry attempt {attempt + 1} after {delay}ms");
    }

    return false;
}
```

### Common Error Patterns

```csharp
// Pattern 1: Check authentication before operations
if (!UserManager.Instance.IsSignedIn)
{
    HorizonApp.Log.Error("User must be signed in");
    return;
}

// Pattern 2: Handle null responses
var data = await CloudSaveManager.Instance.LoadObject<GameData>();
if (data == null)
{
    // Either no save exists or request failed
    data = new GameData(); // Use defaults
}

// Pattern 3: Nullable bool for validation
bool? isValid = await GiftCodeManager.Instance.Validate(code);
if (isValid == null)
{
    Debug.Log("Validation request failed");
}
else if (isValid == true)
{
    Debug.Log("Code is valid");
}
else
{
    Debug.Log("Code is invalid");
}
```

---

## Quick Reference Table

| # | Feature | Endpoint | Method | Manager Method |
|---|---------|----------|--------|----------------|
| 1 | Sign Up | `/user-management/signup` | POST | `SignUpEmail()`, `SignUpAnonymous()`, `SignUpGoogle()` |
| 2 | Sign In | `/user-management/signin` | POST | `SignInEmail()`, `SignInAnonymous()`, `SignInGoogle()` |
| 3 | Check Auth | `/user-management/check-auth` | POST | `CheckAuth()` |
| 4 | Verify Email | `/user-management/verify-email` | POST | `VerifyEmail()` |
| 5 | Forgot Password | `/user-management/forgot-password` | POST | `ForgotPassword()` |
| 6 | Reset Password | `/user-management/reset-password` | POST | `ResetPassword()` |
| 7 | Change Name | `/user-management/change-name` | POST | `ChangeName()` |
| 8 | Redeem Code | `/gift-codes/redeem` | POST | `Redeem()` |
| 9 | Validate Code | `/gift-codes/validate` | POST | `Validate()` |
| 10 | Create Log | `/user-logs/create` | POST | `Info()`, `Warn()`, `Error()`, `CreateLog()` |
| 11 | Submit Feedback | `/user-feedback/submit` | POST | `ReportBug()`, `RequestFeature()`, `SendGeneral()`, `Submit()` |
| 12 | Get Config | `/remote-config/{key}` | GET | `GetConfig()`, `GetString()`, `GetInt()`, etc. |
| 13 | Get All Configs | `/remote-config/all` | GET | `GetAllConfigs()` |
| 14 | Load News | `/news` | GET | `LoadNews()` |
| 15 | Save Cloud Data | `/cloud-save/save` | POST | `Save()`, `SaveObject()`, `SaveBytes()` |
| 16 | Load Cloud Data | `/cloud-save/load` | POST | `Load()`, `LoadObject()`, `LoadBytes()` |
| 17 | Submit Score | `/leaderboard/submit` | POST | `SubmitScore()` |
| 18 | Get Top | `/leaderboard/top` | GET | `GetTop()` |
| 19 | Get Rank | `/leaderboard/rank` | GET | `GetRank()` |
| 20 | Get Around | `/leaderboard/around` | GET | `GetAround()` |
| 21 | Get Player Profile | `/player-profile?userId=` | GET | `PlayerProfileManager.GetProfile()` |
| 22 | Set Player Profile | `/player-profile` | PUT | `PlayerProfileManager.SetProfile()` |
| 23 | Start Validated Run | `/validated-actions/runs` | POST | `ValidatedActionsManager.StartRun()` |
| 24 | Submit Validated Run | `/validated-actions/submit` | POST | `ValidatedActionsManager.SubmitValidated()`, `SubmitValidatedWithHash()` |
| 25 | Get Player State | `/validated-actions/state?userId=` | GET | `ValidatedActionsManager.GetState()` |

**Total Endpoints**: 25

---

## Support

- **Dashboard**: [horizon.pm](https://horizon.pm)
- **Quick Start**: See [QUICKSTART.md](../QUICKSTART.md)
- **README**: See [README.md](../README.md)

**Version**: 1.9.0
**Last Updated**: 2026-09-29
