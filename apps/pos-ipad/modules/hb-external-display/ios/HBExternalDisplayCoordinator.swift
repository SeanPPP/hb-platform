import UIKit

final class HBExternalDisplayCoordinator {
  static let shared = HBExternalDisplayCoordinator()

  typealias EventSink = ([String: Any]) -> Void

  private final class Endpoint {
    weak var window: UIWindow?
    weak var scene: UIWindowScene?
    weak var controller: HBExternalDisplayViewController?
    var reactSurfaceRendered = false

    init(
      window: UIWindow,
      scene: UIWindowScene,
      controller: HBExternalDisplayViewController
    ) {
      self.window = window
      self.scene = scene
      self.controller = controller
    }
  }

  private var endpoints: [String: Endpoint] = [:]
  private var enabled = false
  private var latestRevision = -1
  private var latestSnapshot: HBExternalDisplaySnapshot?
  private var lastFailure: String?
  private var reactSurfaceReady = false
  private var activeProducerSessionID: String?
  private var statusEventSink: EventSink?
  private var snapshotEventSink: EventSink?

  private init() {}

  func beginProducerSession(
    _ producerSessionID: String,
    statusEventSink: @escaping EventSink,
    snapshotEventSink: @escaping EventSink
  ) {
    precondition(Thread.isMainThread)
    guard !producerSessionID.isEmpty else { return }

    let isNewProducerSession =
      producerSessionID != activeProducerSessionID
    activeProducerSessionID = producerSessionID
    lastFailure = nil
    self.statusEventSink = statusEventSink
    self.snapshotEventSink = snapshotEventSink

    if isNewProducerSession {
      // OTA / JS reload 后进入新 producer epoch；旧交易快照不能跨 runtime 重放。
      resetProducerEpoch()
    }

    let state = currentState()
    let event: String
    switch state {
    case "ready":
      event = "ready"
    case "failed":
      event = "failed"
    default:
      event = endpoints.isEmpty ? "disconnected" : "connected"
    }
    emit(event: event, reason: "event-sink-attached", state: state)
  }

  func endProducerSession(_ producerSessionID: String) {
    precondition(Thread.isMainThread)
    guard producerSessionID == activeProducerSessionID else { return }

    activeProducerSessionID = nil
    statusEventSink = nil
    snapshotEventSink = nil
    resetProducerEpoch()
  }

  private func resetProducerEpoch() {
    latestRevision = -1
    latestSnapshot = nil
    reactSurfaceReady = false
    lastFailure = nil

    for endpoint in endpoints.values {
      endpoint.reactSurfaceRendered = false
      endpoint.controller?.stopMedia()
      endpoint.controller?.removeReactSurface()
      endpoint.controller?.showWaitingState()
    }
  }

  func setReactSurfaceReady(
    _ ready: Bool,
    producerSessionID: String
  ) {
    precondition(Thread.isMainThread)
    guard producerSessionID == activeProducerSessionID else { return }

    reactSurfaceReady = ready
    lastFailure = nil

    if !ready {
      for endpoint in endpoints.values {
        endpoint.reactSurfaceRendered = false
        endpoint.controller?.removeReactSurface()
      }
      return
    }

    if enabled {
      renderActiveEndpoints(reason: "react-surface-registration-ready")
    }
  }

  func markReactSurfaceRendered(
    sessionID: String,
    producerSessionID: String
  ) {
    precondition(Thread.isMainThread)
    guard
      producerSessionID == activeProducerSessionID,
      reactSurfaceReady,
      let endpoint = endpoints[sessionID],
      endpoint.controller?.hasReactSurface == true
    else {
      return
    }

    endpoint.reactSurfaceRendered = true
    if let latestSnapshot {
      // JS 先订阅 onSnapshotChanged 再完成此握手，因此这里重放可闭合
      // “publish 发生在订阅之前”的竞态窗口。
      snapshotEventSink?(latestSnapshot.dictionary)
    }
    if endpoints.values.allSatisfy(\.reactSurfaceRendered) {
      emit(event: "ready", reason: "react-surface-rendered", state: "ready")
    } else {
      emit(event: "connected", reason: "waiting-for-react-surface", state: "connecting")
    }
  }

  func attach(
    sessionID: String,
    window: UIWindow,
    scene: UIWindowScene,
    controller: HBExternalDisplayViewController
  ) {
    precondition(Thread.isMainThread)
    endpoints[sessionID] = Endpoint(
      window: window,
      scene: scene,
      controller: controller
    )
    lastFailure = nil
    emit(event: "connected", reason: "external-display-connected", state: "connecting")

    guard enabled else {
      window.isHidden = true
      return
    }

    window.makeKeyAndVisible()
    renderActiveEndpoints(reason: "external-display-connected")
  }

  func detach(sessionID: String) {
    precondition(Thread.isMainThread)
    endpoints[sessionID]?.controller?.stopMedia()
    endpoints[sessionID]?.controller?.removeReactSurface()
    endpoints[sessionID]?.window?.isHidden = true
    endpoints.removeValue(forKey: sessionID)
    removeReleasedEndpoints()
    if endpoints.isEmpty {
      lastFailure = nil
    }
    emit(event: "disconnected", reason: "external-display-disconnected")
  }

  func reportResolutionChange(sessionID: String) {
    precondition(Thread.isMainThread)
    guard endpoints[sessionID] != nil else { return }
    emit(event: "resolutionChanged", reason: "external-display-resolution-changed")
  }

  func reportFailure(_ reason: String) {
    precondition(Thread.isMainThread)
    lastFailure = reason
    emit(event: "failed", reason: reason, state: "failed")
  }

  func setEnabled(
    _ nextEnabled: Bool,
    producerSessionID: String
  ) -> [String: Any] {
    precondition(Thread.isMainThread)
    guard producerSessionID == activeProducerSessionID else {
      return makePayload(
        state: currentState(),
        reason: "producer-session-expired"
      )
    }

    enabled = nextEnabled
    lastFailure = nil
    removeReleasedEndpoints()

    for endpoint in endpoints.values {
      guard let window = endpoint.window else { continue }
      if nextEnabled {
        window.makeKeyAndVisible()
      } else {
        endpoint.controller?.stopMedia()
        window.isHidden = true
      }
    }

    emit(
      event: "enabledChanged",
      reason: nextEnabled ? "external-display-enabled" : "external-display-disabled"
    )
    if nextEnabled {
      renderActiveEndpoints(reason: "external-display-enabled")
    }
    return statusPayload()
  }

  func forceBlank(
    producerSessionID: String
  ) -> [String: Any] {
    precondition(Thread.isMainThread)
    guard producerSessionID == activeProducerSessionID else {
      return makePayload(
        state: currentState(),
        reason: "producer-session-expired"
      )
    }

    // 发布桥失效时绕开 RN event/surface，原生同步覆盖上一位顾客的交易画面。
    latestSnapshot = nil
    lastFailure = nil
    for endpoint in endpoints.values {
      endpoint.reactSurfaceRendered = false
      endpoint.controller?.stopMedia()
      endpoint.controller?.removeReactSurface()
      endpoint.controller?.showWaitingState()
    }

    let state = currentState()
    emit(
      event: state == "disconnected" ? "disconnected" : "connected",
      reason: "sensitive-content-reset",
      state: state
    )
    return makePayload(
      state: state,
      reason: "sensitive-content-reset"
    )
  }

  func publish(
    snapshot: HBExternalDisplaySnapshot,
    producerSessionID: String
  ) -> [String: Any] {
    precondition(Thread.isMainThread)

    guard producerSessionID == activeProducerSessionID else {
      return [
        "accepted": false,
        "revision": snapshot.revision,
        "latestRevision": max(latestRevision, 0),
        "reason": "producer-session-expired",
      ]
    }

    guard snapshot.revision > latestRevision else {
      return [
        "accepted": false,
        "revision": snapshot.revision,
        "latestRevision": max(latestRevision, 0),
        "reason": "stale-revision",
      ]
    }

    latestRevision = snapshot.revision
    latestSnapshot = snapshot
    lastFailure = nil
    if enabled {
      renderActiveEndpoints(reason: "snapshot-rendered")
    }
    snapshotEventSink?(snapshot.dictionary)

    return [
      "accepted": true,
      "revision": snapshot.revision,
      "latestRevision": latestRevision,
      "reason": "accepted",
    ]
  }

  func statusPayload() -> [String: Any] {
    precondition(Thread.isMainThread)
    return makePayload(
      state: currentState(),
      reason: currentReason()
    )
  }

  private func renderActiveEndpoints(reason: String) {
    removeReleasedEndpoints()
    guard enabled, !endpoints.isEmpty else { return }

    var renderingFailure: String?
    for (sessionID, endpoint) in endpoints {
      guard let controller = endpoint.controller else { continue }
      if let snapshot = latestSnapshot {
        renderingFailure = controller.render(snapshot: snapshot) ?? renderingFailure
      } else {
        controller.showWaitingState()
      }

      if reactSurfaceReady, !controller.hasReactSurface {
        let initialProperties: [AnyHashable: Any] = [
          "surfaceId": sessionID,
          "snapshot": latestSnapshot?.dictionary ?? NSNull(),
        ]
        if let failure = controller.installReactSurface(
          initialProperties: initialProperties
        ) {
          renderingFailure = failure
        }
      }
    }

    if let renderingFailure {
      lastFailure = renderingFailure
      emit(event: "failed", reason: renderingFailure, state: "failed")
    } else if reactSurfaceReady, endpoints.values.allSatisfy(\.reactSurfaceRendered) {
      lastFailure = nil
      emit(event: "ready", reason: reason, state: "ready")
    } else {
      lastFailure = nil
      emit(event: "connected", reason: "waiting-for-react-surface", state: "connecting")
    }
  }

  private func currentState() -> String {
    removeReleasedEndpoints()
    guard enabled, !endpoints.isEmpty else {
      return "disconnected"
    }
    if lastFailure != nil {
      return "failed"
    }
    guard
      reactSurfaceReady,
      endpoints.values.allSatisfy(\.reactSurfaceRendered)
    else {
      return "connecting"
    }
    return "ready"
  }

  private func currentReason() -> String {
    if let lastFailure {
      return lastFailure
    }
    if !enabled {
      return "external-display-disabled"
    }
    if endpoints.isEmpty {
      return "no-external-display"
    }
    if !reactSurfaceReady {
      return "waiting-for-react-surface-registration"
    }
    if !endpoints.values.allSatisfy(\.reactSurfaceRendered) {
      return "waiting-for-react-surface-render"
    }
    return "external-display-ready"
  }

  private func emit(
    event: String,
    reason: String,
    state: String? = nil
  ) {
    guard let statusEventSink else { return }
    var payload = makePayload(
      state: state ?? currentState(),
      reason: reason
    )
    payload["event"] = event
    statusEventSink(payload)
  }

  private func makePayload(
    state: String,
    reason: String
  ) -> [String: Any] {
    let metrics = currentMetrics()
    return [
      "state": state,
      "enabled": enabled,
      "connected": !endpoints.isEmpty,
      "revision": max(latestRevision, 0),
      "widthPixels": metrics.width,
      "heightPixels": metrics.height,
      "scale": metrics.scale,
      "reason": reason,
    ]
  }

  private func currentMetrics() -> (width: Int, height: Int, scale: Double) {
    guard let screen = endpoints.values.compactMap(\.scene).first?.screen else {
      return (0, 0, 0)
    }
    return (
      Int(screen.nativeBounds.width.rounded()),
      Int(screen.nativeBounds.height.rounded()),
      Double(screen.nativeScale)
    )
  }

  private func removeReleasedEndpoints() {
    endpoints = endpoints.filter { _, endpoint in
      endpoint.window != nil && endpoint.scene != nil && endpoint.controller != nil
    }
  }
}
