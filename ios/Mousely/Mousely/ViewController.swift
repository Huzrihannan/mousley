import UIKit
import WebKit
import CoreMotion

class ViewController: UIViewController, WKScriptMessageHandler, WKNavigationDelegate {

    private var webView: WKWebView!
    private let volumeObserver = VolumeObserver()
    private let networkDiscovery = NetworkDiscovery()
    private let motionManager = CMMotionManager()
    private let motionQueue: OperationQueue = {
        let q = OperationQueue()
        q.name = "com.mousely.motionQueue"
        q.qualityOfService = .userInteractive
        return q
    }()
    private var isMotionTrackingActive = false
    private var lastEvalTime: TimeInterval = 0
    private var lastNativeRollDeg: Double = 0.0

    override var preferredStatusBarStyle: UIStatusBarStyle {
        return .lightContent
    }

    override var supportedInterfaceOrientations: UIInterfaceOrientationMask {
        return .landscape
    }

    override var preferredInterfaceOrientationForPresentation: UIInterfaceOrientation {
        return .landscapeRight
    }

    override var shouldAutorotate: Bool {
        return true
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        setupWebView()
        setupVolumeObserver()
        setupNetworkDiscovery()
        setupLifecycleObservers()
        loadLocalWebContent()
    }

    private func setupLifecycleObservers() {
        NotificationCenter.default.addObserver(
            forName: UIApplication.didEnterBackgroundNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            self?.networkDiscovery.pauseBroadcastingQueries()
        }

        NotificationCenter.default.addObserver(
            forName: UIApplication.willEnterForegroundNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            self?.networkDiscovery.resumeBroadcastingQueries()
        }
    }

    private func setupWebView() {
        view.backgroundColor = UIColor(red: 10/255.0, green: 2/255.0, blue: 4/255.0, alpha: 1.0)

        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true
        config.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")
        config.userContentController.add(self, name: "nativeApp")

        webView = WKWebView(frame: .zero, configuration: config)
        webView.translatesAutoresizingMaskIntoConstraints = false
        webView.isOpaque = false
        webView.backgroundColor = .clear
        webView.scrollView.backgroundColor = .clear
        webView.scrollView.bounces = false
        webView.navigationDelegate = self

        view.addSubview(webView)

        NSLayoutConstraint.activate([
            webView.topAnchor.constraint(equalTo: view.topAnchor),
            webView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            webView.bottomAnchor.constraint(equalTo: view.bottomAnchor)
        ])
    }

    private func loadLocalWebContent() {
        // Look for bundled Web/index.html
        if let indexURL = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "Web") {
            let directoryURL = indexURL.deletingLastPathComponent()
            webView.loadFileURL(indexURL, allowingReadAccessTo: directoryURL)
        } else if let altURL = Bundle.main.url(forResource: "index", withExtension: "html") {
            let directoryURL = altURL.deletingLastPathComponent()
            webView.loadFileURL(altURL, allowingReadAccessTo: directoryURL)
        } else {
            print("[ViewController] Could not locate index.html in main bundle.")
        }
    }

    private func setupVolumeObserver() {
        volumeObserver.onVolumeChanged = { [weak self] newVolume in
            let percent = Int(round(newVolume * 100))
            self?.webView.evaluateJavaScript("if (window.onNativeVolumeChanged) window.onNativeVolumeChanged(\(percent));", completionHandler: nil)
        }
        volumeObserver.start(in: view)
    }

    private func setupNetworkDiscovery() {
        networkDiscovery.onServerDiscovered = { [weak self] server in
            let dict: [String: Any] = [
                "name": server.name,
                "ip": server.ip,
                "port": server.port
            ]
            if let data = try? JSONSerialization.data(withJSONObject: dict),
               let jsonString = String(data: data, encoding: .utf8) {
                self?.webView.evaluateJavaScript("if (window.onServerDiscovered) window.onServerDiscovered(\(jsonString));", completionHandler: nil)
            }
        }
        networkDiscovery.start()
    }

    // MARK: - WKScriptMessageHandler
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "nativeApp",
              let body = message.body as? [String: Any],
              let action = body["action"] as? String else {
            return
        }

        switch action {
        case "haptic":
            let styleStr = body["style"] as? String ?? "light"
            let style: UIImpactFeedbackGenerator.FeedbackStyle = (styleStr == "medium") ? .medium : .light
            let generator = UIImpactFeedbackGenerator(style: style)
            generator.prepare()
            generator.impactOccurred()

        case "connectionState":
            let state = body["state"] as? String ?? ""
            if state == "connected" {
                networkDiscovery.pauseBroadcastingQueries()
            } else if state == "disconnected" {
                networkDiscovery.resumeBroadcastingQueries()
                UdpInputTransmitter.shared.close()
            }

        case "setUdpTarget":
            if let host = body["host"] as? String {
                let port = UInt16(body["port"] as? Int ?? 58922)
                UdpInputTransmitter.shared.setTarget(host: host, port: port)
            }

        case "udp_move":
            let dx = Int16(clamping: body["dx"] as? Int ?? 0)
            let dy = Int16(clamping: body["dy"] as? Int ?? 0)
            UdpInputTransmitter.shared.sendMove(dx: dx, dy: dy)

        case "udp_scroll":
            let deltaY = Int16(clamping: body["deltaY"] as? Int ?? 0)
            let deltaX = Int16(clamping: body["deltaX"] as? Int ?? 0)
            UdpInputTransmitter.shared.sendScroll(deltaY: deltaY, deltaX: deltaX)

        case "udp_gamepad":
            let stickX = Int16(clamping: body["stickX"] as? Int ?? 0)
            let stickY = Int16(clamping: body["stickY"] as? Int ?? 0)
            let lt = UInt8(clamping: body["lt"] as? Int ?? 0)
            let rt = UInt8(clamping: body["rt"] as? Int ?? 0)
            let buttons = UInt16(clamping: body["buttons"] as? Int ?? 0)
            UdpInputTransmitter.shared.sendGamepad(stickX: stickX, stickY: stickY, lt: lt, rt: rt, buttons: buttons)

        case "startGyro":
            startHardwareMotionUpdates()

        case "stopGyro":
            stopHardwareMotionUpdates()

        default:
            break
        }
    }

    private func startHardwareMotionUpdates() {
        guard motionManager.isDeviceMotionAvailable, !isMotionTrackingActive else { return }
        isMotionTrackingActive = true
        lastEvalTime = 0
        lastNativeRollDeg = 0.0
        motionManager.deviceMotionUpdateInterval = 1.0 / 30.0 // 30Hz sensor polling
        motionManager.startDeviceMotionUpdates(to: motionQueue) { [weak self] motion, _ in
            guard let self = self, let motion = motion else { return }
            // In Landscape Right: turning the steering wheel rotates around the device pitch axis
            let rollDeg = -motion.attitude.pitch * (180.0 / .pi)

            let now = CACurrentMediaTime()
            // Throttle evaluateJavaScript to max 20Hz (50ms) and require at least 0.25 deg movement
            if (now - self.lastEvalTime >= 0.05) && (abs(rollDeg - self.lastNativeRollDeg) >= 0.25 || abs(rollDeg) < 0.2) {
                self.lastEvalTime = now
                self.lastNativeRollDeg = rollDeg
                DispatchQueue.main.async {
                    self.webView.evaluateJavaScript("if (window.onNativeGyroUpdate) window.onNativeGyroUpdate(\(rollDeg));", completionHandler: nil)
                }
            }
        }
    }

    private func stopHardwareMotionUpdates() {
        if isMotionTrackingActive {
            motionManager.stopDeviceMotionUpdates()
            motionQueue.cancelAllOperations()
            isMotionTrackingActive = false
        }
    }

    deinit {
        stopHardwareMotionUpdates()
        volumeObserver.stop()
        networkDiscovery.stop()
    }
}
