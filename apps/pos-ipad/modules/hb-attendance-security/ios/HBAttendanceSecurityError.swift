import ExpoModulesCore
import Foundation

enum HBAttendanceSecurityErrorCode: String {
  case invalidArgument = "ATTENDANCE_SECURITY_INVALID_ARGUMENT"
  case keyNotFound = "ATTENDANCE_KEY_NOT_FOUND"
  case keychainFailure = "ATTENDANCE_KEYCHAIN_FAILURE"
  case keyGenerationFailed = "ATTENDANCE_KEY_GENERATION_FAILED"
  case tokenGenerationFailed = "ATTENDANCE_TOKEN_GENERATION_FAILED"
  case qrRenderFailed = "ATTENDANCE_QR_RENDER_FAILED"
}

final class HBAttendanceSecurityException: Exception {
  private let stableCode: String
  private let stableReason: String

  init(_ code: HBAttendanceSecurityErrorCode, _ reason: String) {
    stableCode = code.rawValue
    stableReason = reason
    super.init(
      name: "HBAttendanceSecurityException",
      description: reason,
      code: code.rawValue
    )
  }

  override var code: String { stableCode }
  override var reason: String { stableReason }
}

func attendanceInvalidArgument(_ field: String) -> HBAttendanceSecurityException {
  HBAttendanceSecurityException(
    .invalidArgument,
    "考勤安全参数无效：\(field)"
  )
}
