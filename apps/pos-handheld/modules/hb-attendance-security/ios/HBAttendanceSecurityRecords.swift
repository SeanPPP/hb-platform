import ExpoModulesCore
import Foundation

private let maximumSafeJavaScriptInteger: Double =
  9_007_199_254_740_991

struct HBAttendanceQrRequestRecord: Record {
  @Field var deviceCode = ""
  @Field var issuedAtEpochMs: Double = 0
  @Field var keyHandle = ""
  @Field var kid = ""
  @Field var storeCode = ""

  func validated() throws -> HBAttendanceQrInput {
    guard
      issuedAtEpochMs.isFinite,
      issuedAtEpochMs.rounded() == issuedAtEpochMs,
      issuedAtEpochMs >= 0,
      issuedAtEpochMs <= maximumSafeJavaScriptInteger
    else {
      throw attendanceInvalidArgument("issuedAtEpochMs")
    }
    guard
      kid.range(
        of: #"^[A-Za-z0-9_-]{1,64}$"#,
        options: .regularExpression
      ) != nil
    else {
      throw attendanceInvalidArgument("kid")
    }
    return HBAttendanceQrInput(
      deviceCode: deviceCode,
      issuedAtEpochMs: Int64(issuedAtEpochMs),
      keyHandle: keyHandle,
      kid: kid,
      storeCode: storeCode
    )
  }
}

struct HBEmergencyPublicKeyRecord: Record {
  @Field var algorithm = ""
  @Field var fingerprintHex = ""
  @Field var kid = ""
  @Field var publicKeyPem = ""

  var value: HBEmergencyPublicKey {
    HBEmergencyPublicKey(
      algorithm: algorithm,
      fingerprintHex: fingerprintHex,
      kid: kid,
      publicKeyPem: publicKeyPem
    )
  }
}

struct HBEmergencyVerificationRequestRecord: Record {
  @Field var expectedStoreCode = ""
  @Field var nowEpochMs: Double = 0
  @Field var publicKeys: [HBEmergencyPublicKeyRecord] = []
  @Field var token = ""

  func validated() throws -> (
    token: String,
    publicKeys: [HBEmergencyPublicKey],
    expectedStoreCode: String,
    nowEpochMs: Int64
  ) {
    guard
      nowEpochMs.isFinite,
      nowEpochMs.rounded() == nowEpochMs,
      nowEpochMs >= 0,
      nowEpochMs <= maximumSafeJavaScriptInteger
    else {
      throw attendanceInvalidArgument("nowEpochMs")
    }
    guard
      token.count <= 2_048,
      token.hasPrefix("HBPOSE1-") || token.hasPrefix("HBPOSE2-")
    else {
      throw attendanceInvalidArgument("token")
    }
    guard publicKeys.count <= 128 else {
      throw attendanceInvalidArgument("publicKeys")
    }
    return (
      token,
      publicKeys.map(\.value),
      expectedStoreCode,
      Int64(nowEpochMs)
    )
  }
}
