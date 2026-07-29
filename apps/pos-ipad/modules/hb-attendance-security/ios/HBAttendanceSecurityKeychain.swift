import Foundation
import Security

struct HBAttendanceA256Identity {
  let keyHandle: String
  let kid: String
}

final class HBAttendanceSecurityKeychain {
  private let service = "com.hbweb.posipad.attendance.a256"

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

  private func baseQuery(handle: String) -> [String: Any] {
    [
      kSecClass as String: kSecClassGenericPassword,
      kSecAttrService as String: service,
      kSecAttrAccount as String: handle,
      kSecAttrSynchronizable as String: kCFBooleanFalse as Any,
    ]
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
