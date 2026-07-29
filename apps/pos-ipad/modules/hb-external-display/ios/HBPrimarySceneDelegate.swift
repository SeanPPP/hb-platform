import UIKit

@MainActor
public protocol HBPrimaryWindowAppDelegate: AnyObject {
  var window: UIWindow? { get set }
}

/**
 * Expo AppDelegate 拥有唯一的主 UIWindow 和 React Native root。
 * 该 delegate 只将既有主窗口接管到已连接的 application scene。
 */
@MainActor
public final class HBPrimarySceneDelegate: UIResponder, UIWindowSceneDelegate {
  public var window: UIWindow?

  public func scene(
    _ scene: UIScene,
    willConnectTo session: UISceneSession,
    options connectionOptions: UIScene.ConnectionOptions
  ) {
    guard session.role == .windowApplication else { return }
    guard let windowScene = scene as? UIWindowScene else { return }
    guard
      let appDelegate =
        UIApplication.shared.delegate as? HBPrimaryWindowAppDelegate,
      let appWindow = appDelegate.window
    else {
      NSLog("[HBPrimarySceneDelegate] 主窗口不可用，拒绝创建第二个 root")
      return
    }
    if
      let existingWindowScene = appWindow.windowScene,
      existingWindowScene !== windowScene,
      existingWindowScene.session.role == .windowApplication,
      existingWindowScene.activationState != .unattached
    {
      NSLog("[HBPrimarySceneDelegate] 主窗口仍附着到另一主 Scene，拒绝接管")
      return
    }

    appWindow.windowScene = windowScene
    window = appWindow
    appWindow.makeKeyAndVisible()

    forward(urlContexts: connectionOptions.urlContexts)
    for userActivity in connectionOptions.userActivities {
      forward(userActivity: userActivity)
    }
  }

  public func sceneDidDisconnect(_ scene: UIScene) {
    guard window?.windowScene === scene else { return }
    window = nil
  }

  public func scene(
    _ scene: UIScene,
    openURLContexts URLContexts: Set<UIOpenURLContext>
  ) {
    forward(urlContexts: URLContexts)
  }

  public func scene(
    _ scene: UIScene,
    continue userActivity: NSUserActivity
  ) {
    forward(userActivity: userActivity)
  }

  private func forward(urlContexts: Set<UIOpenURLContext>) {
    for context in urlContexts {
      _ = UIApplication.shared.delegate?.application?(
        UIApplication.shared,
        open: context.url,
        options: [:]
      )
    }
  }

  private func forward(userActivity: NSUserActivity) {
    _ = UIApplication.shared.delegate?.application?(
      UIApplication.shared,
      continue: userActivity,
      restorationHandler: { _ in }
    )
  }
}
