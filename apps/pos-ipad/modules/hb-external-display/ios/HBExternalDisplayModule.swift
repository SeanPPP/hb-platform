import ExpoModulesCore
import Foundation

private final class HBExternalDisplayException: Exception {
  private let externalDisplayCode: String
  private let externalDisplayReason: String

  init(_ code: String, _ reason: String) {
    externalDisplayCode = code
    externalDisplayReason = reason
    super.init(name: "HBExternalDisplayException", description: reason, code: code)
  }

  override var code: String { externalDisplayCode }
  override var reason: String { externalDisplayReason }
}

public final class HBExternalDisplayModule: Module {
  private let producerSessionID = UUID().uuidString

  public func definition() -> ModuleDefinition {
    Name("HBExternalDisplay")
    Events("onStatusChanged", "onSnapshotChanged")

    OnCreate {
      let installProducerSession = { [weak self] in
        guard let self else { return }
        HBExternalDisplayCoordinator.shared.beginProducerSession(
          self.producerSessionID,
          statusEventSink: { [weak self] payload in
            self?.sendEvent("onStatusChanged", payload)
          },
          snapshotEventSink: { [weak self] payload in
            self?.sendEvent("onSnapshotChanged", payload)
          }
        )
      }

      if Thread.isMainThread {
        installProducerSession()
      } else {
        // OnCreate 返回前建立 epoch，避免 JS 随后的 ready/publish 调用抢先到达。
        DispatchQueue.main.sync(execute: installProducerSession)
      }
    }

    OnDestroy {
      let producerSessionID = self.producerSessionID
      DispatchQueue.main.async {
        HBExternalDisplayCoordinator.shared.endProducerSession(
          producerSessionID
        )
      }
    }

    AsyncFunction("getStatus") { (promise: Promise) in
      DispatchQueue.main.async {
        promise.resolve(HBExternalDisplayCoordinator.shared.statusPayload())
      }
    }

    AsyncFunction("setEnabled") { (enabled: Bool, promise: Promise) in
      DispatchQueue.main.async {
        promise.resolve(
          HBExternalDisplayCoordinator.shared.setEnabled(
            enabled,
            producerSessionID: self.producerSessionID
          )
        )
      }
    }

    AsyncFunction("forceBlank") { (promise: Promise) in
      DispatchQueue.main.async {
        promise.resolve(
          HBExternalDisplayCoordinator.shared.forceBlank(
            producerSessionID: self.producerSessionID
          )
        )
      }
    }

    AsyncFunction("markReactSurfaceReady") { (promise: Promise) in
      DispatchQueue.main.async {
        HBExternalDisplayCoordinator.shared.setReactSurfaceReady(
          true,
          producerSessionID: self.producerSessionID
        )
        promise.resolve()
      }
    }

    AsyncFunction("markReactSurfaceRendered") { (
      surfaceId: String,
      promise: Promise
    ) in
      DispatchQueue.main.async {
        HBExternalDisplayCoordinator.shared.markReactSurfaceRendered(
          sessionID: surfaceId,
          producerSessionID: self.producerSessionID
        )
        promise.resolve()
      }
    }

    AsyncFunction("publishSnapshot") { (
      record: HBExternalDisplaySnapshotRecord,
      promise: Promise
    ) in
      let snapshot: HBExternalDisplaySnapshot
      do {
        snapshot = try record.validated()
      } catch {
        promise.reject(
          HBExternalDisplayException(
            "EXTERNAL_DISPLAY_INVALID_SNAPSHOT",
            error.localizedDescription
          )
        )
        return
      }

      DispatchQueue.main.async {
        promise.resolve(
          HBExternalDisplayCoordinator.shared.publish(
            snapshot: snapshot,
            producerSessionID: self.producerSessionID
          )
        )
      }
    }
  }
}
