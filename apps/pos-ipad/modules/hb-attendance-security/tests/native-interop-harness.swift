import Foundation

private struct Request: Decodable {
  let expectedStoreCode: String
  let fingerprintHex: String
  let kid: String
  let legacyToken: String
  let nowEpochMs: Int64
  let publicKeyPem: String
  let v2Token: String
}

private struct Response: Encodable {
  let keyValid: Bool
  let legacy: [String: String]
  let v2: [String: String]
}

@main
private enum HBAttendanceSecurityInteropHarness {
  static func main() throws {
    let input = FileHandle.standardInput.readDataToEndOfFile()
    let request = try JSONDecoder().decode(Request.self, from: input)
    let verifier = HBEmergencyLoginVerifier()
    let key = HBEmergencyPublicKey(
      algorithm: "ES256",
      fingerprintHex: request.fingerprintHex,
      kid: request.kid,
      publicKeyPem: request.publicKeyPem
    )
    let legacy = verifier.verify(
      token: request.legacyToken,
      publicKeys: [key],
      expectedStoreCode: request.expectedStoreCode,
      nowEpochMs: request.nowEpochMs
    )
    let v2 = verifier.verify(
      token: request.v2Token,
      publicKeys: [key],
      expectedStoreCode: request.expectedStoreCode,
      nowEpochMs: request.nowEpochMs
    )
    let output = Response(
      keyValid: verifier.validatePublicKey(key),
      legacy: flatten(legacy),
      v2: flatten(v2)
    )
    FileHandle.standardOutput.write(try JSONEncoder().encode(output))
  }

  private static func flatten(
    _ result: HBEmergencyVerificationResult
  ) -> [String: String] {
    switch result {
    case .success(let claims):
      return [
        "ok": "true",
        "grantId": claims.grantId,
        "storeCode": claims.storeCode,
        "notBeforeEpochMs": String(claims.notBeforeEpochMs),
        "expiresAtEpochMs": String(claims.expiresAtEpochMs),
      ]
    case .failure(let errorCode):
      return ["ok": "false", "errorCode": errorCode]
    }
  }
}
