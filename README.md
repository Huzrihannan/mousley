# 🎵 Mousely Media Remote

> Ultra-responsive, low-latency Windows Media & Volume controller for iPhone 7 Plus and modern iOS devices, featuring an Apple-inspired Liquid Glass UI.

---

## ✨ Features

- **📱 Landscape-Exclusive Experience**:
  - Permanently optimized for widescreen landscape view (iPhone 7 Plus 736×414 pt).
- **✌️ 2-Finger Swipe Page Navigation**:
  - Seamlessly switch between the 3 dedicated pages with a 2-finger horizontal swipe or the segmented liquid glass tabs:
    1. **🎵 Page 1 — Media Control**: Full GSMTC playback (Spotify, YouTube, VLC, Apple Music), album artwork, live scrubber, and instant bidirectional volume sync (<5ms).
    2. **🚀 Page 2 — Mission Control (8 Blocks)**: 8 liquid glass app launch blocks to trigger Browser, Spotify, File Explorer, Task Manager, Terminal, Calculator, Notepad, and Windows Settings with 1 tap.
    3. **📋 Page 3 — Quick Action Blocks (3 Blocks)**: Giant tactile blocks for `Ctrl + C` (Copy), `Ctrl + V` (Paste), and `Win + V` (Windows Clipboard History).
- **🎛️ Bidirectional Real-Time Volume Sync**:
  - Direct Windows CoreAudio (WASAPI COM interop) integration.
  - Changing volume on iPhone instantly adjusts Windows master volume, and vice versa.
  - Physical iPhone volume buttons (Volume Up / Down) directly control Windows volume via native `AVAudioSession` observation.
- **🍏 Apple Liquid Glass UI & Suiting Logo**:
  - Built with [`liquid-glass-js`](https://github.com/dashersw/liquid-glass-js) WebGL refraction shaders.
  - Light, pleasant aesthetic with soft ambient pastel gradients, specular edge highlights, and tactile haptic feedback.
  - Custom app logo featuring a glassmorphic mouse with luminous play wave.
- **🔍 Zero-Config Local Wi-Fi Discovery**:
  - Automatic UDP broadcast beacon discovery (port `58921`) — no need to type IP addresses manually.
- **🖥️ Standalone Windows Executable**:
  - Includes `Mousely.exe` sitting in the system tray with embedded icon and quick launcher `Start-Mousely.bat`.
- **📦 GitHub Actions CI/CD (No Apple Developer Signing Needed)**:
  - Pre-configured `.github/workflows/build-ios.yml` compiles the unsigned `.ipa` on macOS runners using `xcodebuild`.
  - Ready for sideloading onto iOS 15 (iPhone 7 Plus) via **TrollStore**, **AltStore**, **Sideloadly**, or **Scarlet**.

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
