import Foundation

private struct Request: Decodable {
  let deviceCode: String
  let issuedAtEpochMs: Int64
  let keyBase64Url: String
  let kid: String
  let nonceBase64Url: String
  let storeCode: String
  let tokenId: String
}

@main
private enum HBAttendanceTokenInteropHarness {
  static func main() throws {
    let input = FileHandle.standardInput.readDataToEndOfFile()
    let request = try JSONDecoder().decode(Request.self, from: input)
    guard
      let key = decodeBase64Url(request.keyBase64Url),
      let nonce = decodeBase64Url(request.nonceBase64Url),
      let tokenId = UUID(uuidString: request.tokenId)
    else {
      throw HarnessError.invalidInput
    }
    let token = try HBAttendanceTokenCodec.encrypt(
      HBAttendanceQrInput(
        deviceCode: request.deviceCode,
        issuedAtEpochMs: request.issuedAtEpochMs,
        keyHandle: "test-only",
        kid: request.kid,
        storeCode: request.storeCode
      ),
      key: key,
      nonceData: nonce,
      tokenId: tokenId
    )
    FileHandle.standardOutput.write(Data(token.utf8))
  }

  private static func decodeBase64Url(_ value: String) -> Data? {
    var base64 = value
      .replacingOccurrences(of: "-", with: "+")
      .replacingOccurrences(of: "_", with: "/")
    base64 += String(
      repeating: "=",
      count: (4 - base64.count % 4) % 4
    )
    return Data(base64Encoded: base64)
  }

  private enum HarnessError: Error {
    case invalidInput
  }
}
