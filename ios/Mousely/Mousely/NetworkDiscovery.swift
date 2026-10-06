import Foundation
import Network

struct DiscoveredServer: Equatable {
    let name: String
    let ip: String
    let port: Int

    static func == (lhs: DiscoveredServer, rhs: DiscoveredServer) -> Bool {
        return lhs.ip == rhs.ip && lhs.port == rhs.port
    }
}

class NetworkDiscovery {
    private var listener: NWListener?
    private var broadcastTimer: Timer?
    private let discoveryPort: NWEndpoint.Port = 58921
    private let queue = DispatchQueue(label: "com.mousely.discovery", qos: .utility)

    var onServerDiscovered: ((DiscoveredServer) -> Void)?

    func start() {
        startListener()
        startBroadcastingQueries()
    }

    private func startListener() {
        do {
            let params = NWParameters.udp
            params.allowLocalEndpointReuse = true
            listener = try NWListener(using: params, on: discoveryPort)

            listener?.newConnectionHandler = { [weak self] connection in
                self?.handleIncomingConnection(connection)
            }

            listener?.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    print("[Discovery] iOS UDP Listener active on port 58921")
                case .failed(let error):
                    print("[Discovery] iOS Listener failed: \(error.localizedDescription)")
                default:
                    break
                }
            }

            listener?.start(queue: queue)
        } catch {
            print("[Discovery] Unable to start NWListener: \(error.localizedDescription)")
        }
    }

    private func handleIncomingConnection(_ connection: NWConnection) {
        connection.stateUpdateHandler = { state in
            if case .failed(_) = state {
                connection.cancel()
            }
        }
        connection.start(queue: queue)
        connection.receiveMessage { [weak self] (content, context, isComplete, error) in
            if let data = content, let jsonString = String(data: data, encoding: .utf8) {
                self?.parseBeacon(data: data, rawString: jsonString)
            }
            connection.cancel()
        }

        // Safety timeout to avoid lingering connection handles
        queue.asyncAfter(deadline: .now() + 2.0) {
            connection.cancel()
        }
    }

    private func parseBeacon(data: Data, rawString: String) {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let app = json["app"] as? String, app == "Mousely",
              let ip = json["ip"] as? String else {
            return
        }

        let name = json["name"] as? String ?? "Windows PC"
        let port = json["port"] as? Int ?? 58920

        let server = DiscoveredServer(name: name, ip: ip, port: port)
        DispatchQueue.main.async {
            self.onServerDiscovered?(server)
            self.pauseBroadcastingQueries()
        }
    }

    func startBroadcastingQueries() {
        sendBroadcastQuery()
        // Broadcast every 4.0s (low power)
        broadcastTimer?.invalidate()
        broadcastTimer = Timer.scheduledTimer(withTimeInterval: 4.0, repeats: true) { [weak self] _ in
            self?.sendBroadcastQuery()
        }
    }

    func pauseBroadcastingQueries() {
        broadcastTimer?.invalidate()
        broadcastTimer = nil
    }

    func resumeBroadcastingQueries() {
        if broadcastTimer == nil {
            startBroadcastingQueries()
        }
    }

    private func sendBroadcastQuery() {
        let broadcastEndpoint = NWEndpoint.hostPort(host: "255.255.255.255", port: discoveryPort)
        let connection = NWConnection(to: broadcastEndpoint, using: .udp)
        
        connection.stateUpdateHandler = { state in
            switch state {
            case .ready:
                let payload = "MOUSELY_DISCOVER".data(using: .utf8)!
                connection.send(content: payload, completion: .contentProcessed({ _ in
                    connection.cancel()
                }))
            case .failed, .cancelled:
                connection.cancel()
            default:
                break
            }
        }
        
        connection.start(queue: queue)

        // Safety timeout: cancel after 2 seconds
        queue.asyncAfter(deadline: .now() + 2.0) {
            if connection.state != .cancelled {
                connection.cancel()
            }
        }
    }

    func stop() {
        pauseBroadcastingQueries()
        listener?.cancel()
        listener = nil
    }

    deinit {
        stop()
    }
}

// MARK: - Ultra-Low Latency UDP Input Transmitter (Port 58922)
public final class UdpInputTransmitter {
    public static let shared = UdpInputTransmitter()

    private var connection: NWConnection?
    private var currentHost: String?
    private var currentPort: UInt16 = 58922
    private let queue = DispatchQueue(label: "com.mousely.udp.input", qos: .userInteractive)

    public func setTarget(host: String, port: UInt16 = 58922) {
        guard host != currentHost || port != currentPort else { return }
        currentHost = host
        currentPort = port

        connection?.cancel()
        connection = nil

        guard !host.isEmpty else { return }

        let endpointHost = NWEndpoint.Host(host)
        guard let endpointPort = NWEndpoint.Port(rawValue: port) else { return }

        let parameters = NWParameters.udp
        parameters.serviceClass = .responsiveData
        parameters.allowLocalEndpointReuse = true

        let conn = NWConnection(host: endpointHost, port: endpointPort, using: parameters)
        conn.stateUpdateHandler = { state in
            switch state {
            case .ready:
                print("[UdpInputTransmitter] Stream connected -> \(host):\(port)")
            case .failed(let error):
                print("[UdpInputTransmitter] Stream error: \(error.localizedDescription)")
            default:
                break
            }
        }
        conn.start(queue: queue)
        self.connection = conn
    }

    public func sendMove(dx: Int16, dy: Int16) {
        guard let connection = connection else { return }
        var bytes = [UInt8](repeating: 0, count: 5)
        bytes[0] = 0x01
        let bDx = dx.bigEndian
        let bDy = dy.bigEndian
        withUnsafeBytes(of: bDx) { bytes[1] = $0[0]; bytes[2] = $0[1] }
        withUnsafeBytes(of: bDy) { bytes[3] = $0[0]; bytes[4] = $0[1] }
        let data = Data(bytes)

        connection.send(content: data, completion: .idempotent)
    }

    public func sendScroll(deltaY: Int16, deltaX: Int16) {
        guard let connection = connection else { return }
        var bytes = [UInt8](repeating: 0, count: 5)
        bytes[0] = 0x02
        let bDy = deltaY.bigEndian
        let bDx = deltaX.bigEndian
        withUnsafeBytes(of: bDy) { bytes[1] = $0[0]; bytes[2] = $0[1] }
        withUnsafeBytes(of: bDx) { bytes[3] = $0[0]; bytes[4] = $0[1] }
        let data = Data(bytes)

        connection.send(content: data, completion: .idempotent)
    }

    public func close() {
        connection?.cancel()
        connection = nil
        currentHost = nil
    }
}
