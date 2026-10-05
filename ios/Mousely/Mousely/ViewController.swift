import UIKit
import WebKit

class ViewController: UIViewController, WKScriptMessageHandler, WKNavigationDelegate {

    private var webView: WKWebView!
    private let volumeObserver = VolumeObserver()
    private let networkDiscovery = NetworkDiscovery()

    override var preferredStatusBarStyle: UIStatusBarStyle {
        return .darkContent
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        setupWebView()
        setupVolumeObserver()
        setupNetworkDiscovery()
        loadLocalWebContent()
    }

    private func setupWebView() {
        view.backgroundColor = UIColor(red: 240/255.0, green: 244/255.0, blue: 253/255.0, alpha: 1.0)

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

        default:
            break
        }
    }

    deinit {
        volumeObserver.stop()
        networkDiscovery.stop()
    }
}
