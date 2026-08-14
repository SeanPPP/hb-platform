import UIKit

public final class HBExternalDisplaySceneDelegate: UIResponder, UIWindowSceneDelegate {
  public var window: UIWindow?

  private var sessionID: String?

  public func scene(
    _ scene: UIScene,
    willConnectTo session: UISceneSession,
    options connectionOptions: UIScene.ConnectionOptions
  ) {
    guard session.role == .windowExternalDisplayNonInteractive else {
      return
    }
    guard let windowScene = scene as? UIWindowScene else {
      HBExternalDisplayCoordinator.shared.reportFailure(
        "external-display-scene-type-invalid"
      )
      return
    }

    if let displayMode = windowScene.screen.availableModes.max(by: {
      let leftPixels = $0.size.width * $0.size.height
      let rightPixels = $1.size.width * $1.size.height
      return leftPixels < rightPixels
    }) {
      // HDMI 可能先选中低分辨率兼容模式；窗口创建前请求完整画布。
      windowScene.screen.currentMode = displayMode
    }

    let controller = HBExternalDisplayViewController()
    controller.view.isUserInteractionEnabled = false

    let externalWindow = UIWindow(windowScene: windowScene)
    externalWindow.rootViewController = controller
    externalWindow.isUserInteractionEnabled = false
    window = externalWindow
    sessionID = session.persistentIdentifier

    HBExternalDisplayCoordinator.shared.attach(
      sessionID: session.persistentIdentifier,
      window: externalWindow,
      scene: windowScene,
      controller: controller
    )
  }

  public func sceneDidDisconnect(_ scene: UIScene) {
    guard let sessionID else { return }
    HBExternalDisplayCoordinator.shared.detach(sessionID: sessionID)
    self.sessionID = nil
    window = nil
  }

  public func windowScene(
    _ windowScene: UIWindowScene,
    didUpdate previousCoordinateSpace: UICoordinateSpace,
    interfaceOrientation previousInterfaceOrientation: UIInterfaceOrientation,
    traitCollection previousTraitCollection: UITraitCollection
  ) {
    guard let sessionID else { return }
    HBExternalDisplayCoordinator.shared.reportResolutionChange(
      sessionID: sessionID
    )
  }

  @available(iOS 26.0, *)
  public func windowScene(
    _ windowScene: UIWindowScene,
    didUpdateEffectiveGeometry previousEffectiveGeometry: UIWindowScene.Geometry
  ) {
    guard let sessionID else { return }
    HBExternalDisplayCoordinator.shared.reportResolutionChange(
      sessionID: sessionID
    )
  }
}
