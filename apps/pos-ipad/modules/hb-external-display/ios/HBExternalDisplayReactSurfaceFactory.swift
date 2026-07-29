import Expo
import UIKit

enum HBExternalDisplayReactSurfaceError: LocalizedError {
  case appDelegateUnavailable
  case rootViewFactoryUnavailable

  var errorDescription: String? {
    switch self {
    case .appDelegateUnavailable:
      return "expo-app-delegate-unavailable"
    case .rootViewFactoryUnavailable:
      return "react-root-view-factory-unavailable"
    }
  }
}

enum HBExternalDisplayReactSurfaceFactory {
  static func makeSurface(
    initialProperties: [AnyHashable: Any]
  ) throws -> UIView {
    precondition(Thread.isMainThread)

    guard
      let appDelegate = UIApplication.shared.delegate as? ExpoAppDelegate
    else {
      throw HBExternalDisplayReactSurfaceError.appDelegateUnavailable
    }
    guard let rootViewFactory = appDelegate.factory?.rootViewFactory else {
      throw HBExternalDisplayReactSurfaceError.rootViewFactoryUnavailable
    }

    let surface = rootViewFactory.view(
      withModuleName: "HBExternalDisplay",
      initialProperties: initialProperties,
      launchOptions: nil
    )
    surface.isUserInteractionEnabled = false
    surface.isOpaque = false
    surface.backgroundColor = .clear
    return surface
  }
}
