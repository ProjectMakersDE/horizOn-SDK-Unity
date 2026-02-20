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
10. [Data Models](#data-models)
11. [Error Handling](#error-handling)

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
  "giftData": "string (JSON with rewards)"
}
```

**Example giftData**:
```json
{
  "gold": 100,
  "crystals": 50,
  "items": ["sword", "shield"]
}
```

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Invalid/expired/already redeemed |
| 403 | Code doesn't belong to API key |
| 404 | Code not found |

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

if (result != null && result.Success)
{
    Debug.Log("Code redeemed successfully!");

    // Parse rewards from giftData (JSON string)
    if (!string.IsNullOrEmpty(result.GiftData))
    {
        // Parse JSON and grant rewards
        var rewards = JsonUtility.FromJson<RewardsData>(result.GiftData);
        GrantRewards(rewards);
    }
}
else
{
    // Handle specific errors
    string message = result?.Message ?? "Unknown error";
    Debug.Log($"Redemption failed: {message}");

    // Common messages:
    // - "Code already redeemed"
    // - "Code expired"
    // - "Code not found"
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

**Request Body**:
```json
{
  "userId": "uuid",
  "score": "long (>= 0)"
}
```

**Response**: 200 OK on success

**Error Responses**:
| Code | Cause |
|------|-------|
| 400 | Invalid request |
| 403 | Entry limit exceeded |

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

// Optionally include metadata
bool submitted = await LeaderboardManager.Instance.SubmitScore(
    score: 12500,
    metadata: "Level5-HardMode"  // optional extra data
);
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
      "score": 1000
    }
  ]
}
```

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

// Get top 10 players (cached by default)
var topPlayers = await LeaderboardManager.Instance.GetTop(10);

if (topPlayers != null)
{
    foreach (var entry in topPlayers)
    {
        Debug.Log($"#{entry.Position} {entry.Username}: {entry.Score}");
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
  "score": 1000
}
```

**Error Response**: 404 if user has no entry

#### Unity SDK Usage

```csharp
using PM.horizOn.Cloud.Manager;

var myRank = await LeaderboardManager.Instance.GetRank();

if (myRank != null)
{
    Debug.Log($"Your rank: #{myRank.Position}");
    Debug.Log($"Your score: {myRank.Score}");
    Debug.Log($"Username: {myRank.Username}");
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
    { "position": 8, "username": "Player8", "score": 950 },
    { "position": 9, "username": "CurrentUser", "score": 920 },
    { "position": 10, "username": "Player10", "score": 900 }
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
        string marker = entry.Username == UserManager.Instance.CurrentUser.DisplayName
            ? " <-- YOU" : "";
        Debug.Log($"#{entry.Position} {entry.Username}: {entry.Score}{marker}");
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
| 429 | Rate Limited | Wait and retry with backoff |
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

**Total Endpoints**: 20

---

## Support

- **Dashboard**: [horizon.pm](https://horizon.pm)
- **Quick Start**: See [QUICKSTART.md](../QUICKSTART.md)
- **README**: See [README.md](../README.md)

**Version**: 1.1.0
**Last Updated**: 2026-02-20
