import Foundation
import Network

struct DiscoveredServer {
    let name: String
    let ip: String
    let port: Int
}

class NetworkDiscovery {
    private var listener: NWListener?
    private var broadcastConnection: NWConnection?
    private var broadcastTimer: Timer?
    private let discoveryPort: NWEndpoint.Port = 58921
    private let queue = DispatchQueue(label: "com.mousely.discovery", qos: .userInitiated)

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
                    print("[Discovery] Listening on UDP port \(self.discoveryPort)")
                case .failed(let error):
                    print("[Discovery] Listener failed: \(error.localizedDescription)")
                default:
                    break
                }
            }

            listener?.start(queue: queue)
        } catch {
            print("[Discovery] Unable to create NWListener: \(error.localizedDescription)")
        }
    }

    private func handleIncomingConnection(_ connection: NWConnection) {
        connection.start(queue: queue)
        receiveNextPacket(from: connection)
    }

    private func receiveNextPacket(from connection: NWConnection) {
        connection.receiveMessage { [weak self] (content, context, isComplete, error) in
            if let data = content, let jsonString = String(data: data, encoding: .utf8) {
                self?.parseBeacon(data: data, rawString: jsonString)
            }
            if error == nil {
                self?.receiveNextPacket(from: connection)
            }
        }
    }

    private func parseBeacon(data: Data, rawString: String) {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let app = json["app"] as? String, app == "Mousely",
              let ip = json["ip"] as? String else {
            return
        }

        let name = json["name"] as? String ?? "Windows PC"
        let port = json["port"] as? Int ?? 8089

        let server = DiscoveredServer(name: name, ip: ip, port: port)
        DispatchQueue.main.async {
            self.onServerDiscovered?(server)
        }
    }

    private func startBroadcastingQueries() {
        sendBroadcastQuery()
        broadcastTimer = Timer.scheduledTimer(withTimeInterval: 3.0, repeats: true) { [weak self] _ in
            self?.sendBroadcastQuery()
        }
    }

    private func sendBroadcastQuery() {
        let broadcastEndpoint = NWEndpoint.hostPort(host: "255.255.255.255", port: discoveryPort)
        let connection = NWConnection(to: broadcastEndpoint, using: .udp)
        
        connection.stateUpdateHandler = { state in
            if case .ready = state {
                let payload = "MOUSELY_DISCOVER".data(using: .utf8)!
                connection.send(content: payload, completion: .contentProcessed({ _ in
                    connection.cancel()
                }))
            }
        }
        
        connection.start(queue: queue)
    }

    func stop() {
        broadcastTimer?.invalidate()
        broadcastTimer = nil
        listener?.cancel()
        listener = nil
    }

    deinit {
        stop()
    }
}
