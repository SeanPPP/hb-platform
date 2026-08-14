import CryptoKit
import Foundation
import Security

struct HBAttendanceQrInput {
  let deviceCode: String
  let issuedAtEpochMs: Int64
  let keyHandle: String
  let kid: String
  let storeCode: String
}

enum HBAttendanceTokenCodecError: Error {
  case invalidInput
  case invalidKey
  case randomFailure
  case tokenTooLong
}

enum HBAttendanceTokenCodec {
  static let tokenPrefix = "HBATE1"

  static func encrypt(
    _ input: HBAttendanceQrInput,
    key: Data
  ) throws -> String {
    var nonceData = Data(count: 12)
    let randomStatus = nonceData.withUnsafeMutableBytes { buffer in
      guard let baseAddress = buffer.baseAddress else {
        return errSecAllocate
      }
      return SecRandomCopyBytes(kSecRandomDefault, 12, baseAddress)
    }
    guard randomStatus == errSecSuccess else {
      throw HBAttendanceTokenCodecError.randomFailure
    }
    return try encrypt(
      input,
      key: key,
      nonceData: nonceData,
      tokenId: UUID()
    )
  }

  static func encrypt(
    _ input: HBAttendanceQrInput,
    key: Data,
    nonceData: Data,
    tokenId: UUID
  ) throws -> String {
    guard key.count == 32 else {
      throw HBAttendanceTokenCodecError.invalidKey
    }
    guard nonceData.count == 12 else {
      throw HBAttendanceTokenCodecError.invalidInput
    }
    let plaintext = try encodePayload(input, tokenId: tokenId)
    let nonce = try AES.GCM.Nonce(data: nonceData)
    let aad = Data("\(tokenPrefix).\(input.kid)".utf8)
    let sealed = try AES.GCM.seal(
      plaintext,
      using: SymmetricKey(data: key),
      nonce: nonce,
      authenticating: aad
    )
    let token = [
      tokenPrefix,
      input.kid,
      base64UrlEncode(nonceData),
      base64UrlEncode(sealed.ciphertext),
      base64UrlEncode(sealed.tag),
    ].joined(separator: ".")
    guard token.count <= 600 else {
      throw HBAttendanceTokenCodecError.tokenTooLong
    }
    return token
  }

  private static func encodePayload(
    _ input: HBAttendanceQrInput,
    tokenId: UUID
  ) throws -> Data {
    let storeBytes = try validatedCode(input.storeCode)
    let deviceBytes = try validatedCode(input.deviceCode)

    var payload = Data([1])
    payload.append(contentsOf: toDotNetGuidBytes(tokenId))
    var issuedAtEpochMs = input.issuedAtEpochMs.littleEndian
    withUnsafeBytes(of: &issuedAtEpochMs) { bytes in
      payload.append(contentsOf: bytes)
    }
    payload.append(UInt8(storeBytes.count))
    payload.append(storeBytes)
    payload.append(UInt8(deviceBytes.count))
    payload.append(deviceBytes)
    return payload
  }

  private static func validatedCode(_ value: String) throws -> Data {
    let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
    let bytes = Data(value.utf8)
    guard
      !trimmed.isEmpty,
      trimmed == value,
      value.utf16.count <= 50,
      bytes.count <= 150
    else {
      throw HBAttendanceTokenCodecError.invalidInput
    }
    return bytes
  }

  private static func toDotNetGuidBytes(_ uuid: UUID) -> [UInt8] {
    var tuple = uuid.uuid
    var bytes = withUnsafeBytes(of: &tuple) { Array($0) }
    // .NET Guid.ToByteArray() 的前三段是小端，最后 8 字节保持 RFC 顺序。
    bytes.swapAt(0, 3)
    bytes.swapAt(1, 2)
    bytes.swapAt(4, 5)
    bytes.swapAt(6, 7)
    return bytes
  }

  private static func base64UrlEncode(_ data: Data) -> String {
    data.base64EncodedString()
      .trimmingCharacters(in: CharacterSet(charactersIn: "="))
      .replacingOccurrences(of: "+", with: "-")
      .replacingOccurrences(of: "/", with: "_")
  }
}
