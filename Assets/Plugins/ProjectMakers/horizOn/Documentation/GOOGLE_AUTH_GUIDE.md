# Google Sign-In Integration Guide

This guide explains how to add Google Sign-In to your Unity game using the horizOn SDK.

## Overview

The horizOn SDK supports Google authentication via the **Authorization Code** flow:

1. Your game uses the **Google Sign-In SDK** to show the Google login UI
2. The user signs in with their Google account
3. Google returns an **authorization code** to your game
4. Your game passes the authorization code to the horizOn SDK
5. The horizOn server exchanges the code for user info and creates/authenticates the user

```
Player → Google Sign-In SDK → Authorization Code → horizOn SDK → horizOn Server → Google API
```

> **Important:** The horizOn SDK does **not** include the Google Sign-In SDK itself. You need to install it separately (see setup below).

## Prerequisites

- horizOn SDK integrated and working (email/anonymous auth functional)
- A Google Cloud Console project with OAuth 2.0 credentials
- Google Sign-In for Unity plugin installed

## Step 1: Google Cloud Console Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create or select a project
3. Navigate to **APIs & Services > Credentials**
4. Create OAuth 2.0 Client IDs:
   - **Android**: Select "Android" application type, provide your package name and SHA-1 fingerprint
   - **iOS**: Select "iOS" application type, provide your bundle ID
   - **Web**: Select "Web application" (needed for the server-side code exchange)
5. Note: The **Web Client ID** is what the horizOn server uses. Make sure you've configured it in your horizOn Dashboard under API Key settings.

## Step 2: Install Google Sign-In for Unity

### Option A: Google Sign-In Unity Plugin (Recommended)

Install the [Google Sign-In Unity Plugin](https://github.com/googlesamples/google-signin-unity):

1. Download the latest `.unitypackage` from the releases
2. Import it into your Unity project
3. Configure the Web Client ID in the plugin settings

### Option B: Native SDKs via Custom Bridge

For more control, you can use the native Android/iOS SDKs directly:
- Android: [Google Identity Services](https://developers.google.com/identity/sign-in/android)
- iOS: [Google Sign-In for iOS](https://developers.google.com/identity/sign-in/ios)

## Step 3: Request the Authorization Code

Configure Google Sign-In to request a **server auth code** (not an ID token):

```csharp
using Google;

// Configure Google Sign-In
GoogleSignIn.Configuration = new GoogleSignInConfiguration
{
    WebClientId = "YOUR_WEB_CLIENT_ID.apps.googleusercontent.com",
    RequestAuthCode = true  // This is critical - we need the auth code
};

// Trigger sign-in
GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(task =>
{
    if (task.IsFaulted)
    {
        Debug.LogError($"Google Sign-In failed: {task.Exception}");
        return;
    }

    GoogleSignInUser googleUser = task.Result;
    string authCode = googleUser.AuthCode;

    // Pass the auth code to horizOn SDK (see Step 4)
    SignInWithHorizOn(authCode);
});
```

> **Key Point:** You must set `RequestAuthCode = true`. The horizOn server needs the authorization code (not an ID token) to securely verify the user with Google's servers.

## Step 4: Use horizOn SDK for Authentication

### Sign Up (New User)

```csharp
using PM.horizOn.Cloud.Manager;

private async void SignUpWithHorizOn(string googleAuthCode)
{
    bool success = await UserManager.Instance.SignUpGoogle(
        googleAuthCode,          // Authorization code from Google Sign-In
        redirectUri: "",         // Empty for mobile apps (Android/iOS)
        username: "OptionalName" // Optional display name
    );

    if (success)
    {
        var user = UserManager.Instance.CurrentUser;
        Debug.Log($"Welcome, {user.DisplayName}! (ID: {user.UserId})");
    }
    else
    {
        Debug.LogError("Google sign-up failed. User may already exist - try SignInGoogle instead.");
    }
}
```

### Sign In (Existing User)

```csharp
private async void SignInWithHorizOn(string googleAuthCode)
{
    bool success = await UserManager.Instance.SignInGoogle(
        googleAuthCode,  // Authorization code from Google Sign-In
        redirectUri: ""  // Empty for mobile apps (Android/iOS)
    );

    if (success)
    {
        var user = UserManager.Instance.CurrentUser;
        Debug.Log($"Welcome back, {user.DisplayName}!");
    }
    else
    {
        // User doesn't exist yet - sign up first
        Debug.Log("User not found, attempting sign-up...");
        await UserManager.Instance.SignUpGoogle(googleAuthCode);
    }
}
```

### Combined Flow (Recommended)

In most games, you want a seamless "Sign in or create account" experience:

```csharp
private async void HandleGoogleAuth(string googleAuthCode)
{
    // Try sign-in first (existing user)
    bool success = await UserManager.Instance.SignInGoogle(googleAuthCode);

    if (!success)
    {
        // User doesn't exist, create new account
        success = await UserManager.Instance.SignUpGoogle(googleAuthCode);
    }

    if (success)
    {
        var user = UserManager.Instance.CurrentUser;
        Debug.Log($"Authenticated as {user.DisplayName} ({user.Email})");
        // Proceed to game
    }
    else
    {
        Debug.LogError("Google authentication failed");
        // Show error to player
    }
}
```

## Step 5: Session Management

After successful Google authentication, the horizOn SDK automatically:
- Stores the session token via `PlayerPrefs`
- Restores the session on next app launch via `CheckAuth()`

```csharp
// On game startup, check for existing session
bool hasSession = await UserManager.Instance.CheckAuth();

if (hasSession)
{
    // Session restored, user is authenticated
    Debug.Log("Session active");
}
else
{
    // No valid session, show Google Sign-In button
    ShowLoginUI();
}
```

## Redirect URI

The `redirectUri` parameter controls where Google sends the authorization code after the user consents.

| Platform | Redirect URI | Why |
|----------|-------------|-----|
| Android | `""` (empty string) | Native Google Sign-In SDK handles the redirect internally |
| iOS | `""` (empty string) | Native Google Sign-In SDK handles the redirect internally |
| Web | Your callback URL | e.g., `https://yourgame.com/auth/callback` |

For mobile games (Android/iOS), always pass an empty string or omit the parameter entirely (it defaults to `""`).

## Troubleshooting

### "Google user not found. Please sign up first."
The user hasn't registered yet. Call `SignUpGoogle()` before `SignInGoogle()`, or use the combined flow above.

### "User with this Google account already exists"
The user already has an account. Use `SignInGoogle()` instead of `SignUpGoogle()`.

### "Email already registered with a different authentication method"
The Google account's email is already associated with an email/password account. The user needs to sign in with their original method.

### Authorization code errors
- Ensure `RequestAuthCode = true` in the Google Sign-In configuration
- The auth code is **single-use** - if it fails, request a new one from Google Sign-In
- Verify the Web Client ID matches between your Google Cloud Console and horizOn Dashboard

### "Invalid redirect URI"
- For mobile apps, ensure you're passing `""` (empty string) as the redirect URI
- For web, ensure the redirect URI matches exactly what's configured in Google Cloud Console

## API Reference

| Method | Parameters | Returns |
|--------|-----------|---------|
| `SignUpGoogle(authCode, redirectUri, username)` | `authCode`: Google authorization code (required), `redirectUri`: OAuth redirect URI (default `""`), `username`: display name (optional) | `Task<bool>` |
| `SignInGoogle(authCode, redirectUri)` | `authCode`: Google authorization code (required), `redirectUri`: OAuth redirect URI (default `""`) | `Task<bool>` |

Both methods return `true` on success. After success, access user data via `UserManager.Instance.CurrentUser`.
