import Foundation
import Security

struct HBAttendanceA256Identity {
  let keyHandle: String
  let kid: String
}

final class HBAttendanceSecurityKeychain {
  private let service = "com.hbweb.posipad.attendance.a256"
  private let faceHmacService = "com.hbweb.posipad.attendance.face.hmac.v1"

  func createIdentity() throws -> HBAttendanceA256Identity {
    var key = Data(count: 32)
    let randomStatus = key.withUnsafeMutableBytes { buffer in
      guard let baseAddress = buffer.baseAddress else {
        return errSecAllocate
      }
      return SecRandomCopyBytes(kSecRandomDefault, 32, baseAddress)
    }
    guard randomStatus == errSecSuccess else {
      throw HBAttendanceSecurityException(
        .keyGenerationFailed,
        "无法生成考勤签名密钥。"
      )
    }
    defer {
      key.resetBytes(in: 0..<key.count)
    }

    var kidBytes = Data(count: 10)
    let kidStatus = kidBytes.withUnsafeMutableBytes { buffer in
      guard let baseAddress = buffer.baseAddress else {
        return errSecAllocate
      }
      return SecRandomCopyBytes(kSecRandomDefault, 10, baseAddress)
    }
    guard kidStatus == errSecSuccess else {
      throw HBAttendanceSecurityException(
        .keyGenerationFailed,
        "无法生成考勤签名密钥标识。"
      )
    }
    let kid = base64UrlEncode(kidBytes)
    let handle = UUID().uuidString.lowercased()

    var addQuery = baseQuery(handle: handle)
    addQuery[kSecAttrAccessible as String] =
      kSecAttrAccessibleWhenUnlockedThisDeviceOnly
    addQuery[kSecValueData as String] = key
    let status = SecItemAdd(addQuery as CFDictionary, nil)
    guard status == errSecSuccess else {
      throw HBAttendanceSecurityException(
        .keychainFailure,
        "无法保存考勤签名密钥。"
      )
    }
    return HBAttendanceA256Identity(keyHandle: handle, kid: kid)
  }

  func hasKey(handle: String) throws -> Bool {
    try validateHandle(handle)
    var query = baseQuery(handle: handle)
    query[kSecReturnAttributes as String] = kCFBooleanTrue
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    let status = SecItemCopyMatching(query as CFDictionary, nil)
    if status == errSecSuccess {
      return true
    }
    if status == errSecItemNotFound {
      return false
    }
    throw HBAttendanceSecurityException(
      .keychainFailure,
      "无法检查考勤签名密钥。"
    )
  }

  func readKey(handle: String) throws -> Data {
    try validateHandle(handle)
    var query = baseQuery(handle: handle)
    query[kSecReturnData as String] = kCFBooleanTrue
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    var result: CFTypeRef?
    let status = SecItemCopyMatching(
      query as CFDictionary,
      &result
    )
    if status == errSecItemNotFound {
      throw HBAttendanceSecurityException(
        .keyNotFound,
        "考勤签名密钥不存在。"
      )
    }
    guard
      status == errSecSuccess,
      let key = result as? Data,
      key.count == 32
    else {
      throw HBAttendanceSecurityException(
        .keychainFailure,
        "无法读取考勤签名密钥。"
      )
    }
    return key
  }

  func destroyKey(handle: String) throws {
    try validateHandle(handle)
    let status = SecItemDelete(baseQuery(handle: handle) as CFDictionary)
    guard status == errSecSuccess || status == errSecItemNotFound else {
      throw HBAttendanceSecurityException(
        .keychainFailure,
        "无法删除考勤签名密钥。"
      )
    }
  }

  /** 人脸事件专用 HMAC 密钥来自受认证的 device-session，绝不复用 QR A256 密钥。 */
  func saveFaceHmacKey(keyId: String, secretBase64: String) throws {
    try validateFaceKeyId(keyId)
    guard var secret = Data(base64Encoded: secretBase64), secret.count == 32 else {
      throw attendanceInvalidArgument("faceHmacSecret")
    }
    defer { secret.resetBytes(in: 0..<secret.count) }
    var query = faceHmacQuery(keyId: keyId)
    query[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
    query[kSecValueData as String] = secret
    let status = SecItemAdd(query as CFDictionary, nil)
    if status == errSecDuplicateItem {
      let update: [String: Any] = [kSecValueData as String: secret]
      let updateStatus = SecItemUpdate(faceHmacQuery(keyId: keyId) as CFDictionary, update as CFDictionary)
      guard updateStatus == errSecSuccess else { throw HBAttendanceSecurityException(.keychainFailure, "无法更新人脸考勤签名密钥。") }
      return
    }
    guard status == errSecSuccess else { throw HBAttendanceSecurityException(.keychainFailure, "无法保存人脸考勤签名密钥。") }
  }

  func hasFaceHmacKey(keyId: String) throws -> Bool {
    try validateFaceKeyId(keyId)
    var query = faceHmacQuery(keyId: keyId)
    query[kSecReturnAttributes as String] = kCFBooleanTrue
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    let status = SecItemCopyMatching(query as CFDictionary, nil)
    if status == errSecSuccess { return true }
    if status == errSecItemNotFound { return false }
    throw HBAttendanceSecurityException(.keychainFailure, "无法检查人脸考勤签名密钥。")
  }

  func readFaceHmacKey(keyId: String) throws -> Data {
    try validateFaceKeyId(keyId)
    var query = faceHmacQuery(keyId: keyId)
    query[kSecReturnData as String] = kCFBooleanTrue
    query[kSecMatchLimit as String] = kSecMatchLimitOne
    var result: CFTypeRef?
    let status = SecItemCopyMatching(query as CFDictionary, &result)
    if status == errSecItemNotFound { throw HBAttendanceSecurityException(.keyNotFound, "人脸考勤签名密钥不存在。") }
    guard status == errSecSuccess, let key = result as? Data, key.count == 32 else {
      throw HBAttendanceSecurityException(.keychainFailure, "无法读取人脸考勤签名密钥。")
    }
    return key
  }

  private func baseQuery(handle: String) -> [String: Any] {
    [
      kSecClass as String: kSecClassGenericPassword,
      kSecAttrService as String: service,
      kSecAttrAccount as String: handle,
      kSecAttrSynchronizable as String: kCFBooleanFalse as Any,
    ]
  }

  private func faceHmacQuery(keyId: String) -> [String: Any] {
    [kSecClass as String: kSecClassGenericPassword,
     kSecAttrService as String: faceHmacService,
     kSecAttrAccount as String: keyId,
     kSecAttrSynchronizable as String: kCFBooleanFalse as Any]
  }

  private func validateFaceKeyId(_ keyId: String) throws {
    guard keyId.range(of: "^[A-Za-z0-9_-]{1,128}$", options: .regularExpression) != nil else {
      throw attendanceInvalidArgument("faceKeyId")
    }
  }

  private func validateHandle(_ handle: String) throws {
    guard
      handle.range(
        of: #"^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"#,
        options: .regularExpression
      ) != nil
    else {
      throw attendanceInvalidArgument("keyHandle")
    }
  }

  private func base64UrlEncode(_ data: Data) -> String {
    data.base64EncodedString()
      .trimmingCharacters(in: CharacterSet(charactersIn: "="))
      .replacingOccurrences(of: "+", with: "-")
      .replacingOccurrences(of: "/", with: "_")
  }
}
