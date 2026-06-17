# horizOn SDK Example UI

A comprehensive example UI for testing all features of the horizOn Cloud SDK. Built with Unity UI Toolkit for a modern, clean interface.

## Features

This example UI allows you to test all horizOn SDK features:

- **Connection Management**: Initialize SDK and connect to the horizOn server
- **Authentication**: Sign up, sign in, sign out, and check authentication status
- **Remote Configuration**: Load and manage remote config values
- **News System**: Fetch and filter news entries
- **Leaderboards**: Submit scores, view rankings, and get player positions
- **Cloud Save**: Save and load game data to/from the cloud
- **Gift Codes**: Validate and redeem gift codes
- **Feedback**: Submit user feedback with different categories

## How to Use

### Opening the Example UI

1. In Unity Editor, go to **Window > horizOn > SDK Example**
2. The example window will open

### Testing the SDK

Follow these steps to test the SDK:

1. **Initialize the SDK**
   - Click "Initialize SDK" button
   - Wait for confirmation

2. **Connect to Server**
   - Click "Connect to Server" button
   - The status will show the connected server URL

3. **Authenticate**
   - Enter email, password, and display name
   - Click "Sign Up" to create a new account (or "Sign In" if you already have one)
   - The response field will show all authentication details

4. **Test Features**
   - Once authenticated, all feature buttons will be enabled
   - Each section has its own buttons and response fields
   - Server responses are displayed in real-time

## UI Structure

All files are contained in the `ExampleUI` folder:

```
ExampleUI/
├── HorizonExampleWindow.cs          # Editor window (opens via Window menu)
├── HorizonExampleUIController.cs    # Main controller handling all logic
├── Resources/
│   └── HorizonExampleUI.uxml        # UI layout definition
└── Styles/
    └── HorizonExampleUI.uss         # Stylesheet for modern, clean design
```

## Design Principles

- **Clean & Modern**: Uses a professional blue color scheme with proper spacing
- **Real-time Feedback**: All server responses are displayed immediately
- **User-Friendly**: Clear sections for each feature with intuitive controls
- **Comprehensive**: Covers all SDK functionality in one place

## Notes

- This example uses a simplified code architecture compared to the main SDK
- All responses from the server are shown in the multi-line text fields
- Authentication is required for most features (leaderboards, cloud save, gift codes, feedback)
- Some features may require server-side configuration (news, remote config, gift codes)

## Color Scheme

- Primary Blue: `rgb(30, 136, 229)`
- Success Green: `rgb(67, 160, 71)`
- Background Gray: `rgb(245, 245, 245)`
- Text Dark: `rgb(33, 33, 33)`

---

**horizOn Cloud SDK** - Making cloud features simple and accessible
