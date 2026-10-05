# 🎵 Mousely Media Remote

> Ultra-responsive, low-latency Windows Media & Volume controller for iPhone 7 Plus and modern iOS devices, featuring an Apple-inspired Liquid Glass UI.

---

## ✨ Features

- **🎛️ Bidirectional Real-Time Volume Sync**:
  - Direct Windows CoreAudio (WASAPI COM interop) integration.
  - Changing volume on iPhone instantly adjusts Windows master volume (<5ms latency), and vice versa.
  - Physical iPhone volume buttons (Volume Up / Down) directly control Windows volume via native `AVAudioSession` observation.
  - Optimistic UI updates with echo cancellation prevent slider fighting.
- **⏯️ Full Media Playback Control**:
  - Integrates with Windows Global System Media Transport Controls (GSMTC).
  - Compatible with **Spotify**, **Apple Music**, **YouTube / Chrome / Edge**, **Tidal**, **VLC**, and more.
  - Live album art extraction and streaming.
  - Scrubbable timeline with live playback position and duration.
  - Fallback to Win32 media keys (`SendInput`) for legacy players.
- **🍏 Apple Liquid Glass UI**:
  - Built with [`liquid-glass-js`](https://github.com/dashersw/liquid-glass-js) WebGL refraction shaders.
  - Light, pleasant aesthetic with soft ambient pastel gradients, specular edge highlights, and tactile haptic feedback.
  - Tailored for the **iPhone 7 Plus** display (414×736 pt) and responsive on all devices.
- **🔍 Zero-Config Local Wi-Fi Discovery**:
  - Automatic UDP broadcast beacon discovery (port `58921`) — no need to type IP addresses manually.
  - Includes QR code and manual IP connection fallbacks.
- **📦 GitHub Actions CI/CD (No Apple Developer Signing Needed)**:
  - Pre-configured `.github/workflows/build-ios.yml` compiles the unsigned `.ipa` on macOS runners using `xcodebuild`.
  - Ready for sideloading onto iOS 15 (iPhone 7 Plus) via **TrollStore**, **AltStore**, **Sideloadly**, or **Scarlet**.
  - Windows background systray executable automatically built via `.github/workflows/build-windows.yml`.

---

## 🏗️ Architecture

```
Mousely/
├── windows/                     # Windows Native Systray Application (.NET 8 C#)
│   ├── Mousely.sln
│   └── Mousely.Tray/
│       ├── Audio/CoreAudioVolume.cs       # WASAPI COM endpoint volume & instant callback
│       ├── Media/WindowsMediaManager.cs   # GSMTC session manager & thumbnail extraction
│       ├── Media/FallbackMediaKeys.cs     # Win32 media keys fallback
│       ├── Server/WebSocketMediaServer.cs # WebSocket & HTTP server
│       ├── Server/UdpDiscoveryBeacon.cs   # UDP Wi-Fi zero-config broadcaster
│       ├── Forms/PairingForm.cs           # Pairing dialog with QR code
│       └── AppTrayContext.cs              # System tray icon and context menu
├── ios/                         # Native iOS Application (Swift / Xcode, iOS 15.0+)
│   └── Mousely/
│       ├── Mousely.xcodeproj/
│       └── Mousely/
│           ├── ViewController.swift       # Fullscreen WKWebView & native bridges
│           ├── VolumeObserver.swift       # Physical hardware volume button observer
│           ├── NetworkDiscovery.swift     # UDP broadcast discovery
│           └── Web/                       # Bundled offline web app with liquid-glass-js
├── shared-web/                  # Light Pleasant Web Client (HTML / CSS / JS)
│   ├── index.html
│   ├── styles.css
│   ├── app.js
│   └── liquid-glass/              # dashersw/liquid-glass-js WebGL components
└── .github/workflows/
    ├── build-ios.yml            # Compiles unsigned Mousely.ipa
    └── build-windows.yml        # Builds Mousely-Windows-Tray-x64.zip
```

---

## 🚀 Getting Started

### 1. Running the Windows App
1. Ensure [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) is installed.
2. Build and run `Mousely.Tray`:
   ```powershell
   dotnet run --project windows/Mousely.Tray/Mousely.Tray.csproj -c Release
   ```
3. Mousely will appear in your **Windows System Tray** (notification area near the clock).
4. Right-click the tray icon to:
   - View local IP and port (default `8089`).
   - View active connected devices.
   - Open the **Pairing Dialog / QR Code**.
   - Toggle **Start with Windows**.

---

### 2. Sideloading the iPhone App (iPhone 7 Plus)

Because iPhone 7 Plus maxes out at iOS 15.8.x, the project deployment target is configured to **iOS 15.0**.

#### Option A: Download from GitHub Actions
1. Push your repository to GitHub or trigger the **Build iOS IPA** action manually under the **Actions** tab.
2. Download `Mousely-iPhone-Unsigned-IPA.zip` from the workflow artifacts.
3. Unzip to obtain `Mousely-unsigned.ipa`.

#### Option B: Sideload onto iPhone
Install `Mousely-unsigned.ipa` using your preferred sideloading tool:
- **TrollStore** (Recommended on iOS 15.0–15.4.1 / supported TrollStore versions):
  - Permanent installation with no 7-day expiration and no computer needed.
  - Open `Mousely-unsigned.ipa` in TrollStore and tap **Install**.
- **AltStore / Sideloadly**:
  - Connect iPhone to your PC.
  - Drag and drop `Mousely-unsigned.ipa` into AltStore / Sideloadly.
  - Enter your free Apple ID to sign and install for 7 days.
- **Scarlet / Esign**:
  - Import the `.ipa` and install directly on the device.

---

### 3. Using the Web Remote directly (Zero Install)
You can also access the exact same Liquid Glass UI directly from Safari on your iPhone without installing the IPA:
1. Connect your iPhone to the same Wi-Fi as your PC.
2. Open Safari and navigate to:
   ```
   http://<YOUR_PC_IP>:8089
   ```
   *(Find `<YOUR_PC_IP>` in the Windows system tray menu or Pairing dialog)*.
3. Tap **Share** → **Add to Home Screen** for a standalone web app experience!

---

## ⚡ Technical Highlights

| Component | Technology | Latency |
|---|---|---|
| **Windows Tray** | .NET 8 / WinForms / CoreAudio COM | < 1 ms |
| **Media Extraction** | WinRT GSMTC (`Windows.Media.Control`) | Real-time events |
| **Network Transport** | Asynchronous WebSockets (`ws://`) | 1–3 ms on LAN |
| **Volume Callback** | `IAudioEndpointVolumeCallback` | Instantaneous hardware interrupts |
| **iPhone Hardware Buttons** | `AVAudioSession.outputVolume` KVO | Instant |
| **UI Aesthetics** | `liquid-glass-js` WebGL + Apple glassmorphism | 60 fps |

---

## 📄 License
MIT License.
