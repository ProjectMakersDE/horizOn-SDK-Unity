# Crash Reporting — Unity SDK Implementation Plan

**Date**: 2026-02-22
**Status**: Implemented
**SDK Version**: 1.2.0
**Language**: C# (Unity 2021+)
**Related**: `ansible-horizon/docs/plans/2026-02-22-crash-reporting-sdk-logic.md` (universal spec)

---

## 1. Overview

Documents the existing CrashManager implementation in the horizOn Unity SDK. The CrashManager captures unhandled exceptions, non-fatal errors, and contextual data, then sends structured crash reports to the horizOn backend.

**File location**: `Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Manager/CrashManager.cs`

---

## 2. Architecture

### 2.1 Class Hierarchy

```
BaseManager<CrashManager> (MonoBehaviour singleton)
    └── CrashManager
```

CrashManager inherits from `BaseManager<T>`, providing:
- Lazy-initialized singleton via `CrashManager.Instance`
- `DontDestroyOnLoad` lifecycle
- `OnInit()` / `OnDestroy()` hooks
- Event registration via `RegisterEvents()` / `UnregisterEvents()`

### 2.2 Dependencies

| Dependency | Purpose |
|-----------|---------|
| `HorizonApp.Network` (NetworkService) | HTTP requests |
| `HorizonApp.Events` (EventService) | Pub-sub event system |
| `HorizonApp.Log` (LogService) | SDK logging |
| `UserManager.Instance` | User ID fallback |
| `HorizonConfig` | API key and host configuration |

### 2.3 Related Files

| File | Purpose |
|------|---------|
| `Manager/CrashManager.cs` | Core crash reporting manager |
| `Enums/CrashType.cs` | CRASH, NON_FATAL, ANR enum |
| `Objects/Data/BreadcrumbData.cs` | Breadcrumb entry structure |
| `Objects/Network/Requests/CreateCrashReportRequest.cs` | Report request DTO |
| `Objects/Network/Requests/CreateCrashSessionRequest.cs` | Session registration DTO |
| `Objects/Network/Responses/CrashReportResponses.cs` | Response DTOs |

---

## 3. Crash Capture Hooks

### 3.1 Unity Log Callback

```csharp
Application.logMessageReceived += OnUnityLogMessage;
```

Captures Unity log messages by type:
- `LogType.Exception` → Creates **CRASH** report
- `LogType.Error` → Creates **NON_FATAL** report
- Other log types (Log, Warning, Assert) → Ignored

Parameters received: `condition` (message), `stackTrace`, `type`

### 3.2 Unhandled Exception Handler

```csharp
AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
```

Captures unhandled managed (.NET) exceptions. Always creates **CRASH** type.
Extracts `Exception.Message` and `Exception.StackTrace` from the event args.

### 3.3 Lifecycle

- **StartCapture()**: Hooks both handlers, generates session ID, registers session, initializes rate limiter
- **StopCapture()**: Unhooks both handlers
- **OnDestroy()**: Calls `StopCapture()` automatically

---

## 4. Fingerprint Generation

### 4.1 Algorithm

1. Parse stack trace by newlines
2. For each frame, extract class name via regex: `^(?:at\s+)?([A-Za-z_][\w.]*)\.[A-Za-z_]\w*\s*[\(:]`
3. Skip engine-internal classes: `UnityEngine.*`, `System.*`, `Mono.*`, `Unity.*`
4. Normalize remaining frames (strip file refs, addresses, line numbers, lambda markers)
5. Take top 5 game-code frames
6. Join with `|` separator
7. SHA-256 hash → 64-char hex string

### 4.2 Normalization Regex

| Pattern | Strips |
|---------|--------|
| `" (at ...)"` | File references |
| `" [0x...]"` | Memory addresses |
| `":line N"` | Line numbers |
| `" <...>"` | Lambda/generic markers |
| `" in ..."` | File paths (suffix) |

### 4.3 Edge Cases

- Empty/null stack trace → Empty fingerprint (backend handles grouping)
- Fewer than 5 game frames → Uses all available frames
- All engine frames → Falls back to empty fingerprint

---

## 5. Breadcrumb Ring Buffer

### 5.1 Implementation

```csharp
private const int MaxBreadcrumbs = 50;
private readonly BreadcrumbData[] _breadcrumbs = new BreadcrumbData[MaxBreadcrumbs];
private int _breadcrumbHead;   // Write position
private int _breadcrumbCount;  // Total added
```

- Pre-allocated array of 50 slots
- Circular write: `head = (head + 1) % 50`
- Retrieval returns chronological order (oldest first)
- Timestamps in ISO 8601 UTC: `DateTime.UtcNow.ToString("o")`

### 5.2 Public API

- `RecordBreadcrumb(string type, string message)` — Generic breadcrumb
- `Log(string message)` — Shorthand for type `"log"`
- Both publish `EventKeys.BreadcrumbRecorded` event

---

## 6. Rate Limiting

### 6.1 Token Bucket

```csharp
private const int TokensPerMinute = 5;
private const int MaxTokensPerSession = 20;
```

**Refill**: Continuous, based on `Time.realtimeSinceStartup`
- Refill rate: `elapsed * (TokensPerMinute / 60f)` tokens per second
- Capped at `TokensPerMinute` (5)

**Two-level enforcement**:
1. Per-minute: `tokens < 1` → drop report with warning
2. Per-session: `sessionCount >= 20` → drop report with warning

### 6.2 Initialization

- Tokens start at `TokensPerMinute` (5)
- Last refill time set to `Time.realtimeSinceStartup`

---

## 7. JSON Serialization

### 7.1 Manual JSON Building

CrashManager uses manual `StringBuilder`-based JSON construction instead of `JsonUtility.ToJson()` because Unity's `JsonUtility` cannot serialize:
- `Dictionary<string, string>` (custom keys)
- `List<T>` inside complex objects (breadcrumbs)

### 7.2 JSON Escaping

Custom `EscapeJson()` handles: `\\`, `\"`, `\n`, `\r`, `\t`

### 7.3 HTTP Transmission

Uses a custom `PostRawJsonAsync<T>()` method (not `NetworkService.PostAsync`) that:
- Creates `UnityWebRequest` manually
- Sets `Content-Type: application/json` and `X-API-Key` headers
- Awaits completion via `Task.Yield()` loop
- Deserializes response via `JsonUtility.FromJson<T>()`

Session registration uses standard `HorizonApp.Network.PostAsync<T>()` since the request is simple.

---

## 8. Device Info

### 8.1 Cached at Init

```csharp
_cachedPlatform = Application.platform.ToString();
_cachedOs = SystemInfo.operatingSystem;
_cachedDeviceModel = SystemInfo.deviceModel;
_cachedDeviceMemoryMb = SystemInfo.systemMemorySize;
```

### 8.2 SDK Version

Hardcoded: `"1.0.0"` (constant in CrashManager)

---

## 9. Session Registration

### 9.1 Session ID

```csharp
_sessionId = Guid.NewGuid().ToString("N");  // 32-char hex
```

### 9.2 Registration

- Endpoint: `POST /api/v1/app/crash-reports/session`
- Fire-and-forget async: `_ = RegisterSessionPing()`
- User ID resolved: override → UserManager → empty string

---

## 10. Custom Keys

- `Dictionary<string, string>` storage, max 10 entries
- `SetCustomKey(key, value)` — insert or update
- New key rejected if limit reached (existing keys can always be updated)
- Merged into report: persistent keys + per-report extra keys (extra overrides persistent)

---

## 11. Events

| Event Key | Payload | Trigger |
|-----------|---------|---------|
| `EventKeys.CrashReported` (410) | `CreateCrashReportResponse` | Successful submission |
| `EventKeys.CrashReportFailed` (411) | `string` error message | Failed submission or rate limited |
| `EventKeys.CrashSessionRegistered` (412) | `string` session ID | Session registered |
| `EventKeys.BreadcrumbRecorded` (413) | `string` message | Breadcrumb added |

---

## 12. Error Handling

| HTTP Status | Behavior |
|-------------|----------|
| 201 | Log success, publish `CrashReported` event |
| 403 | Log "not available for FREE accounts", publish failure |
| 429 | Log "server rate limit exceeded", publish failure |
| Other | Log error message, publish failure |

---

## 13. Public API

```csharp
// Lifecycle
CrashManager.Instance.StartCapture();
CrashManager.Instance.StopCapture();

// Manual reporting
CrashManager.Instance.RecordException(exception, extraKeys?);

// Context
CrashManager.Instance.RecordBreadcrumb(type, message);
CrashManager.Instance.Log(message);
CrashManager.Instance.SetCustomKey(key, value);
CrashManager.Instance.SetUserId(userId);
```

---

## 14. Not Implemented

- **Offline persistence**: Failed reports are dropped (not queued locally)
- **ANR detection**: CrashType.ANR exists but no detection logic
- **IL2CPP symbolication**: Stack traces from IL2CPP builds are raw C++ frames
- **Auto-breadcrumbs from other managers**: Not yet wired (breadcrumbs are manual only)
- **Retry on failure**: Crash report POST does not use NetworkService retry logic
