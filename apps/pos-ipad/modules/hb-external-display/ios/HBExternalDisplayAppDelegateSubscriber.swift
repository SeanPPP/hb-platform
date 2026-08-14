import ExpoModulesCore
import UIKit

public final class HBExternalDisplayAppDelegateSubscriber: ExpoAppDelegateSubscriber {
  public func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
  ) -> Bool {
    NotificationCenter.default.addObserver(
      self,
      selector: #selector(externalScreenDidConnect(_:)),
      name: UIScreen.didConnectNotification,
      object: nil
    )

    for screen in UIScreen.screens where screen !== UIScreen.main {
      selectHighestResolutionMode(for: screen)
    }
    return true
  }

  deinit {
    NotificationCenter.default.removeObserver(self)
  }

  @objc private func externalScreenDidConnect(_ notification: Notification) {
    guard let screen = notification.object as? UIScreen else { return }
    selectHighestResolutionMode(for: screen)
  }

  private func selectHighestResolutionMode(for screen: UIScreen) {
    guard let displayMode = screen.availableModes.max(by: {
      let leftPixels = $0.size.width * $0.size.height
      let rightPixels = $1.size.width * $1.size.height
      return leftPixels < rightPixels
    }) else {
      return
    }

    // UIScreen 连接事件早于外屏 Scene；此时切换可避免窗口锁定兼容分辨率。
    screen.currentMode = displayMode
  }
}
