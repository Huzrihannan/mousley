import Foundation
import AVFoundation
import MediaPlayer
import UIKit

class VolumeObserver: NSObject {
    private var audioSession = AVAudioSession.sharedInstance()
    private var volumeObservation: NSKeyValueObservation?
    private var hiddenVolumeView: MPVolumeView?

    var onVolumeChanged: ((Float) -> Void)?

    func start(in view: UIView) {
        do {
            try audioSession.setCategory(.ambient, options: [.mixWithOthers])
            try audioSession.setActive(true)
        } catch {
            print("[VolumeObserver] Audio session error: \(error.localizedDescription)")
        }

        // Suppress native iOS volume HUD by attaching an off-screen MPVolumeView
        let volumeView = MPVolumeView(frame: CGRect(x: -100, y: -100, width: 1, height: 1))
        volumeView.alpha = 0.01
        view.addSubview(volumeView)
        self.hiddenVolumeView = volumeView

        // Observe volume button presses
        volumeObservation = audioSession.observe(\.outputVolume, options: [.new]) { [weak self] session, change in
            guard let newVolume = change.newValue else { return }
            DispatchQueue.main.async {
                self?.onVolumeChanged?(newVolume)
            }
        }
    }

    func stop() {
        volumeObservation?.invalidate()
        volumeObservation = nil
        hiddenVolumeView?.removeFromSuperview()
        hiddenVolumeView = nil
    }

    deinit {
        stop()
    }
}
