import CryptoKit
import Foundation

struct HBEmergencyPublicKey {
  let algorithm: String
  let fingerprintHex: String
  let kid: String
  let publicKeyPem: String
}

struct HBEmergencyVerifiedClaims {
  let expiresAtEpochMs: Int64
  let grantId: String
  let notBeforeEpochMs: Int64
  let storeCode: String

  var dictionary: [String: Any] {
    [
      "expiresAtEpochMs": expiresAtEpochMs,
      "grantId": grantId,
      "notBeforeEpochMs": notBeforeEpochMs,
      "storeCode": storeCode,
    ]
  }
}

enum HBEmergencyVerificationResult {
  case success(HBEmergencyVerifiedClaims)
  case failure(String)

  var dictionary: [String: Any] {
    switch self {
    case .success(let claims):
      return ["ok": true, "claims": claims.dictionary]
    case .failure(let errorCode):
      return ["ok": false, "errorCode": errorCode]
    }
  }
}

final class HBEmergencyLoginVerifier {
  private let legacyPrefix = "HBPOSE1-"
  private let v2Prefix = "HBPOSE2-"

  func validatePublicKey(_ value: HBEmergencyPublicKey) -> Bool {
    guard
      value.algorithm == "ES256",
      validKid(value.kid),
      value.fingerprintHex.range(
        of: #"^[A-Fa-f0-9]{64}$"#,
        options: .regularExpression
      ) != nil,
      value.publicKeyPem.count >= 64,
      value.publicKeyPem.count <= 8_192,
      value.publicKeyPem.contains("-----BEGIN PUBLIC KEY-----"),
      value.publicKeyPem.contains("-----END PUBLIC KEY-----"),
      !value.publicKeyPem.contains("PRIVATE KEY"),
      let expectedFingerprint = hexDecode(value.fingerprintHex)
    else {
      return false
    }

    do {
      let publicKey = try P256.Signing.PublicKey(
        pemRepresentation: value.publicKeyPem
      )
      let actualFingerprint = Data(
        SHA256.hash(data: publicKey.derRepresentation)
      )
      return constantTimeEquals(
        actualFingerprint,
        expectedFingerprint
      )
    } catch {
      return false
    }
  }

  func verify(
    token: String,
    publicKeys: [HBEmergencyPublicKey],
    expectedStoreCode: String,
    nowEpochMs: Int64
  ) -> HBEmergencyVerificationResult {
    if token.hasPrefix(v2Prefix) {
      return verifyV2(
        token: token,
        publicKeys: publicKeys,
        expectedStoreCode: expectedStoreCode,
        nowEpochMs: nowEpochMs
      )
    }
    guard token.hasPrefix(legacyPrefix) else {
      return .failure("EMERGENCY_TOKEN_FORMAT_INVALID")
    }
    return verifyLegacy(
      token: token,
      publicKeys: publicKeys,
      expectedStoreCode: expectedStoreCode,
      nowEpochMs: nowEpochMs
    )
  }

  private func verifyLegacy(
    token: String,
    publicKeys: [HBEmergencyPublicKey],
    expectedStoreCode: String,
    nowEpochMs: Int64
  ) -> HBEmergencyVerificationResult {
    guard !token.isEmpty, token.count <= 2_048 else {
      return .failure("EMERGENCY_TOKEN_INVALID")
    }
    let parts = token.split(
      separator: "-",
      maxSplits: 3,
      omittingEmptySubsequences: false
    )
    guard
      parts.count == 4,
      parts[0] == "HBPOSE1",
      validKid(String(parts[1]))
    else {
      return .failure("EMERGENCY_TOKEN_FORMAT_INVALID")
    }
    let kid = String(parts[1])
    let matchingKeys = publicKeys.filter { $0.kid == kid }
    guard !matchingKeys.isEmpty else {
      return .failure("EMERGENCY_TOKEN_KEY_UNKNOWN")
    }
    guard matchingKeys.count == 1 else {
      return .failure("EMERGENCY_TOKEN_KEY_INVALID")
    }
    let keyValue = matchingKeys[0]
    guard validatePublicKey(keyValue) else {
      return .failure("EMERGENCY_TOKEN_KEY_INVALID")
    }
    guard
      let payloadBytes = hexDecode(String(parts[2])),
      let signatureBytes = hexDecode(String(parts[3])),
      signatureBytes.count == 64
    else {
      return .failure("EMERGENCY_TOKEN_INVALID")
    }

    let signedBytes =
      Data("HBPOSE1-\(kid)-".utf8) + payloadBytes
    guard
      verifySignature(
        signatureBytes,
        signedBytes: signedBytes,
        key: keyValue
      )
    else {
      return .failure("EMERGENCY_TOKEN_SIGNATURE_INVALID")
    }
    guard
      let payload = try? JSONSerialization.jsonObject(
        with: payloadBytes
      ) as? [String: Any],
      let claims = validateLegacyPayload(
        payload
      )
    else {
      return .failure("EMERGENCY_TOKEN_PAYLOAD_INVALID")
    }

    if nowEpochMs < claims.notBeforeEpochMs {
      return .failure("EMERGENCY_TOKEN_NOT_ACTIVE")
    }
    if nowEpochMs >= claims.expiresAtEpochMs {
      return .failure("EMERGENCY_TOKEN_EXPIRED")
    }
    guard
      claims.storeCode.caseInsensitiveCompare(
        normalizedStoreCode(expectedStoreCode) ?? ""
      ) == .orderedSame
    else {
      return .failure("EMERGENCY_TOKEN_WRONG_STORE")
    }
    return .success(claims)
  }

  private func verifyV2(
    token: String,
    publicKeys: [HBEmergencyPublicKey],
    expectedStoreCode: String,
    nowEpochMs: Int64
  ) -> HBEmergencyVerificationResult {
    guard token.count == 158 else {
      return .failure("EMERGENCY_TOKEN_FORMAT_INVALID")
    }
    let encoded = String(token.dropFirst(v2Prefix.count))
    guard
      isBase64Url(encoded),
      let decoded = base64UrlDecode(encoded),
      decoded.count == 112,
      base64UrlEncode(decoded) == encoded
    else {
      return .failure("EMERGENCY_TOKEN_FORMAT_INVALID")
    }
    let body = Data(decoded.prefix(48))
    let signature = Data(decoded.suffix(64))
    let grantBytes = Data(body[8..<24])
    let notBeforeSeconds = readUInt32BigEndian(body, offset: 40)
    let expiresAtSeconds = readUInt32BigEndian(body, offset: 44)
    guard
      grantBytes.contains(where: { $0 != 0 }),
      expiresAtSeconds > notBeforeSeconds
    else {
      return .failure("EMERGENCY_TOKEN_PAYLOAD_INVALID")
    }

    let selector = Data(body.prefix(8))
    let matchingKeys = publicKeys.filter {
      validKid($0.kid) &&
        constantTimeEquals(keySelector($0.kid), selector)
    }
    guard !matchingKeys.isEmpty else {
      return .failure("EMERGENCY_TOKEN_KEY_UNKNOWN")
    }
    guard matchingKeys.count == 1, validatePublicKey(matchingKeys[0]) else {
      return .failure("EMERGENCY_TOKEN_KEY_INVALID")
    }

    let signedBytes = Data("HBPOSE2-".utf8) + body
    guard
      verifySignature(
        signature,
        signedBytes: signedBytes,
        key: matchingKeys[0]
      )
    else {
      return .failure("EMERGENCY_TOKEN_SIGNATURE_INVALID")
    }

    guard let storeCode = normalizedStoreCode(expectedStoreCode) else {
      return .failure("EMERGENCY_TOKEN_WRONG_STORE")
    }
    let expectedFingerprint = Data(
      SHA256.hash(data: Data(storeCode.utf8))
    ).prefix(16)
    guard
      constantTimeEquals(
        Data(expectedFingerprint),
        Data(body[24..<40])
      )
    else {
      return .failure("EMERGENCY_TOKEN_WRONG_STORE")
    }

    let notBeforeEpochMs = Int64(notBeforeSeconds) * 1_000
    let expiresAtEpochMs = Int64(expiresAtSeconds) * 1_000
    if nowEpochMs < notBeforeEpochMs {
      return .failure("EMERGENCY_TOKEN_NOT_ACTIVE")
    }
    if nowEpochMs >= expiresAtEpochMs {
      return .failure("EMERGENCY_TOKEN_EXPIRED")
    }
    guard let grantId = rfcGuidString(grantBytes) else {
      return .failure("EMERGENCY_TOKEN_PAYLOAD_INVALID")
    }
    return .success(
      HBEmergencyVerifiedClaims(
        expiresAtEpochMs: expiresAtEpochMs,
        grantId: grantId,
        notBeforeEpochMs: notBeforeEpochMs,
        storeCode: storeCode
      )
    )
  }

  private func validateLegacyPayload(
    _ payload: [String: Any]
  ) -> HBEmergencyVerifiedClaims? {
    guard
      let grantIdValue = payload["grantId"] as? String,
      let grantId = UUID(uuidString: grantIdValue),
      grantId != UUID(uuid: (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
      let storeCode = payload["storeCode"] as? String,
      validLegacyStoreCode(storeCode),
      let businessDate = payload["businessDate"] as? String,
      validBusinessDate(businessDate),
      payload["permissionProfile"] as? String == "AllPosTerminal",
      let issuer = payload["issuer"] as? String,
      !issuer.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
      issuer.utf16.count <= 128,
      payload["audience"] as? String == "Hbpos.Wpf",
      let issuedAt = parseIsoDate(payload["issuedAtUtc"]),
      let notBefore = parseIsoDate(payload["notBeforeUtc"]),
      let expiresAt = parseIsoDate(payload["expiresAtUtc"])
    else {
      return nil
    }
    let issuedAtEpochMs = epochMilliseconds(issuedAt)
    let notBeforeEpochMs = epochMilliseconds(notBefore)
    let expiresAtEpochMs = epochMilliseconds(expiresAt)
    guard
      issuedAtEpochMs <= expiresAtEpochMs,
      expiresAtEpochMs > notBeforeEpochMs
    else {
      return nil
    }
    return HBEmergencyVerifiedClaims(
      expiresAtEpochMs: expiresAtEpochMs,
      grantId: grantId.uuidString.lowercased(),
      notBeforeEpochMs: notBeforeEpochMs,
      storeCode: storeCode
    )
  }

  private func verifySignature(
    _ rawSignature: Data,
    signedBytes: Data,
    key: HBEmergencyPublicKey
  ) -> Bool {
    do {
      let publicKey = try P256.Signing.PublicKey(
        pemRepresentation: key.publicKeyPem
      )
      let signature = try P256.Signing.ECDSASignature(
        rawRepresentation: rawSignature
      )
      return publicKey.isValidSignature(signature, for: signedBytes)
    } catch {
      return false
    }
  }

  private func validKid(_ value: String) -> Bool {
    value.range(
      of: #"^[A-Za-z0-9]{1,32}$"#,
      options: .regularExpression
    ) != nil
  }

  private func validLegacyStoreCode(_ value: String) -> Bool {
    !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
      value.trimmingCharacters(in: .whitespacesAndNewlines) == value &&
      value.utf16.count <= 50
  }

  private func normalizedStoreCode(_ value: String) -> String? {
    let normalized = value
      .trimmingCharacters(in: .whitespacesAndNewlines)
      .uppercased()
    return normalized.isEmpty || normalized.utf16.count > 50
      ? nil
      : normalized
  }

  private func validBusinessDate(_ value: String) -> Bool {
    guard
      value.range(
        of: #"^\d{4}-\d{2}-\d{2}$"#,
        options: .regularExpression
      ) != nil
    else {
      return false
    }
    let pieces = value.split(separator: "-")
    guard
      pieces.count == 3,
      let year = Int(pieces[0]),
      let month = Int(pieces[1]),
      let day = Int(pieces[2])
    else {
      return false
    }
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = TimeZone(secondsFromGMT: 0)!
    guard
      let date = calendar.date(
        from: DateComponents(
          calendar: calendar,
          timeZone: calendar.timeZone,
          year: year,
          month: month,
          day: day
        )
      )
    else {
      return false
    }
    let validated = calendar.dateComponents(
      [.year, .month, .day],
      from: date
    )
    return
      validated.year == year &&
      validated.month == month &&
      validated.day == day
  }

  private func parseIsoDate(_ value: Any?) -> Date? {
    guard let text = value as? String else { return nil }
    let fractional = ISO8601DateFormatter()
    fractional.formatOptions = [
      .withInternetDateTime,
      .withFractionalSeconds,
    ]
    if let parsed = fractional.date(from: text) {
      return parsed
    }
    let wholeSeconds = ISO8601DateFormatter()
    wholeSeconds.formatOptions = [.withInternetDateTime]
    return wholeSeconds.date(from: text)
  }

  private func epochMilliseconds(_ date: Date) -> Int64 {
    Int64((date.timeIntervalSince1970 * 1_000).rounded(.towardZero))
  }

  private func keySelector(_ kid: String) -> Data {
    Data(SHA256.hash(data: Data(kid.utf8))).prefix(8)
  }

  private func readUInt32BigEndian(
    _ data: Data,
    offset: Int
  ) -> UInt32 {
    data[offset..<(offset + 4)].reduce(UInt32(0)) {
      ($0 << 8) | UInt32($1)
    }
  }

  private func rfcGuidString(_ bytes: Data) -> String? {
    guard bytes.count == 16 else { return nil }
    let hex = bytes.map { String(format: "%02x", $0) }.joined()
    return [
      String(hex.prefix(8)),
      String(hex.dropFirst(8).prefix(4)),
      String(hex.dropFirst(12).prefix(4)),
      String(hex.dropFirst(16).prefix(4)),
      String(hex.dropFirst(20).prefix(12)),
    ].joined(separator: "-")
  }

  private func hexDecode(_ value: String) -> Data? {
    guard value.count.isMultiple(of: 2) else { return nil }
    var result = Data()
    result.reserveCapacity(value.count / 2)
    var index = value.startIndex
    while index < value.endIndex {
      let next = value.index(index, offsetBy: 2)
      guard let byte = UInt8(value[index..<next], radix: 16) else {
        return nil
      }
      result.append(byte)
      index = next
    }
    return result
  }

  private func isBase64Url(_ value: String) -> Bool {
    !value.isEmpty &&
      value.range(
        of: #"^[A-Za-z0-9_-]+$"#,
        options: .regularExpression
      ) != nil
  }

  private func base64UrlDecode(_ value: String) -> Data? {
    var base64 = value
      .replacingOccurrences(of: "-", with: "+")
      .replacingOccurrences(of: "_", with: "/")
    base64 += String(
      repeating: "=",
      count: (4 - base64.count % 4) % 4
    )
    return Data(base64Encoded: base64)
  }

  private func base64UrlEncode(_ data: Data) -> String {
    data.base64EncodedString()
      .trimmingCharacters(in: CharacterSet(charactersIn: "="))
      .replacingOccurrences(of: "+", with: "-")
      .replacingOccurrences(of: "/", with: "_")
  }

  private func constantTimeEquals(_ lhs: Data, _ rhs: Data) -> Bool {
    guard lhs.count == rhs.count else { return false }
    var difference: UInt8 = 0
    for index in lhs.indices {
      difference |= lhs[index] ^ rhs[index]
    }
    return difference == 0
  }
}
