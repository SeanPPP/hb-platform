import ExpoModulesCore
import Foundation

public final class HBAttendanceSecurityModule: Module {
  private let keychain = HBAttendanceSecurityKeychain()
  private lazy var attendanceQr = HBAttendanceQrCodec(
    keychain: keychain
  )
  private let emergencyVerifier = HBEmergencyLoginVerifier()

  public func definition() -> ModuleDefinition {
    Name("HBAttendanceSecurity")

    Function("getSystemUptimeMilliseconds") { () -> Double in
      let uptimeMilliseconds =
        ProcessInfo.processInfo.systemUptime * 1_000
      return uptimeMilliseconds.rounded(.down)
    }

    AsyncFunction("createA256Identity") { () throws -> [String: Any] in
      let identity = try self.keychain.createIdentity()
      return [
        "keyHandle": identity.keyHandle,
        "kid": identity.kid,
      ]
    }

    AsyncFunction("hasA256Key") { (keyHandle: String) throws -> Bool in
      try self.keychain.hasKey(handle: keyHandle)
    }

    AsyncFunction("readRegistrationKeyMaterial") {
      (keyHandle: String) throws -> String in
      var key = try self.keychain.readKey(handle: keyHandle)
      defer {
        key.resetBytes(in: 0..<key.count)
      }
      return self.base64UrlEncode(key)
    }

    AsyncFunction("issueAttendanceQr") {
      (record: HBAttendanceQrRequestRecord) throws -> [String: Any] in
      let input = try record.validated()
      let imageUri = try self.attendanceQr.issue(input)
      return ["imageUri": imageUri]
    }

    AsyncFunction("destroyA256Key") {
      (keyHandle: String) throws -> Void in
      try self.keychain.destroyKey(handle: keyHandle)
    }

    AsyncFunction("validateEs256P256PublicKey") {
      (record: HBEmergencyPublicKeyRecord) -> Bool in
      self.emergencyVerifier.validatePublicKey(record.value)
    }

    AsyncFunction("verifyEs256P256Token") {
      (record: HBEmergencyVerificationRequestRecord) throws -> [String: Any] in
      let input = try record.validated()
      return self.emergencyVerifier.verify(
        token: input.token,
        publicKeys: input.publicKeys,
        expectedStoreCode: input.expectedStoreCode,
        nowEpochMs: input.nowEpochMs
      ).dictionary
    }
  }

  private func base64UrlEncode(_ data: Data) -> String {
    data.base64EncodedString()
      .trimmingCharacters(in: CharacterSet(charactersIn: "="))
      .replacingOccurrences(of: "+", with: "-")
      .replacingOccurrences(of: "/", with: "_")
  }
}
