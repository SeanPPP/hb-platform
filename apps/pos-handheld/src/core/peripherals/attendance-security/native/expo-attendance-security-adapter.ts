import type {
  HbAttendanceSecurityNativeModule,
  NativeAttendanceQrInput,
  NativeEmergencyPublicKey,
  NativeEmergencyVerificationInput,
} from "./types";

import type { AttendanceQrCryptoPort } from "@/features/attendance-audit/attendance-qr-controller";
import type {
  EmergencyLoginCryptoPort,
  EmergencySystemUptimePort,
  EmergencyTokenCryptoResult,
} from "@/features/attendance-audit/emergency-login-security";
import type { EmergencyPublicKey } from "@/features/attendance-audit/hbpos-attendance-security-api";

const OPAQUE_HANDLE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u;
const ATTENDANCE_KID = /^[A-Za-z0-9_-]{14}$/u;
const A256_BASE64URL = /^[A-Za-z0-9_-]{43}$/u;
const EMERGENCY_KID = /^[A-Za-z0-9]{1,32}$/u;
const SHA256_HEX = /^[A-Fa-f0-9]{64}$/u;
const QR_DATA_URI = /^data:image\/png;base64,[A-Za-z0-9+/=]+$/u;
const UUID =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;

const EMERGENCY_ERROR_CODES = new Set([
  "EMERGENCY_TOKEN_EXPIRED",
  "EMERGENCY_TOKEN_FORMAT_INVALID",
  "EMERGENCY_TOKEN_INVALID",
  "EMERGENCY_TOKEN_KEY_INVALID",
  "EMERGENCY_TOKEN_KEY_UNKNOWN",
  "EMERGENCY_TOKEN_NOT_ACTIVE",
  "EMERGENCY_TOKEN_PAYLOAD_INVALID",
  "EMERGENCY_TOKEN_SIGNATURE_INVALID",
  "EMERGENCY_TOKEN_WRONG_STORE",
]);

export class AttendanceSecurityBridgeError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "AttendanceSecurityBridgeError";
  }
}

export class ExpoAttendanceSecurityAdapter
  implements
    AttendanceQrCryptoPort,
    EmergencyLoginCryptoPort,
    EmergencySystemUptimePort
{
  public constructor(
    private readonly native: HbAttendanceSecurityNativeModule,
  ) {}

  public getSystemUptimeMilliseconds(): number {
    const value = this.native.getSystemUptimeMilliseconds();
    if (
      typeof value !== "number" ||
      !Number.isSafeInteger(value) ||
      value < 0
    ) {
      throw bridgeError("Invalid native system uptime.");
    }
    return value;
  }

  public async createA256Identity(): Promise<
    Readonly<{ keyHandle: string; kid: string }>
  > {
    const value = await this.native.createA256Identity();
    const object = exactObject(value, ["keyHandle", "kid"]);
    const keyHandle = opaqueHandle(object.keyHandle);
    const kid = attendanceKid(object.kid);
    return Object.freeze({ keyHandle, kid });
  }

  public async hasA256Key(keyHandle: string): Promise<boolean> {
    const value = await this.native.hasA256Key(
      opaqueHandle(keyHandle),
    );
    if (typeof value !== "boolean") {
      throw bridgeError("Invalid has-key response.");
    }
    return value;
  }

  public async withRegistrationKey<T>(
    keyHandle: string,
    consume: (keyMaterialBase64Url: string) => Promise<T>,
  ): Promise<T> {
    if (typeof consume !== "function") {
      throw bridgeError("Invalid registration callback.");
    }
    const material = await this.native.readRegistrationKeyMaterial(
      opaqueHandle(keyHandle),
    );
    if (
      typeof material !== "string" ||
      !A256_BASE64URL.test(material)
    ) {
      throw bridgeError("Invalid A256 registration material.");
    }

    // 关键逻辑：明文密钥只作为本次调用栈的局部值交给登记回调，不进入实例字段或状态。
    return consume(material);
  }

  public async issueAttendanceQr(
    input: NativeAttendanceQrInput,
  ): Promise<Readonly<{ imageUri: string }>> {
    const nativeInput: NativeAttendanceQrInput = Object.freeze({
      deviceCode: boundedCode(input.deviceCode, 50, "deviceCode"),
      issuedAtEpochMs: safeEpoch(input.issuedAtEpochMs),
      keyHandle: opaqueHandle(input.keyHandle),
      kid: attendanceKid(input.kid),
      storeCode: boundedCode(input.storeCode, 50, "storeCode"),
    });
    const value = await this.native.issueAttendanceQr(nativeInput);
    const object = exactObject(value, ["imageUri"]);
    if (
      typeof object.imageUri !== "string" ||
      object.imageUri.length > 2_500_000 ||
      !QR_DATA_URI.test(object.imageUri)
    ) {
      throw bridgeError("Invalid attendance QR image response.");
    }
    return Object.freeze({ imageUri: object.imageUri });
  }

  public async destroyKey(keyHandle: string): Promise<void> {
    await this.native.destroyA256Key(opaqueHandle(keyHandle));
  }

  public async validateEs256P256PublicKey(
    key: EmergencyPublicKey,
  ): Promise<boolean> {
    const normalized = emergencyPublicKey(key);
    if (!normalized) return false;
    const value =
      await this.native.validateEs256P256PublicKey(normalized);
    if (typeof value !== "boolean") {
      throw bridgeError("Invalid public-key validation response.");
    }
    return value;
  }

  public async verifyEs256P256Token(
    input: NativeEmergencyVerificationInput,
  ): Promise<EmergencyTokenCryptoResult> {
    const token = input.token;
    if (
      typeof token !== "string" ||
      token.length === 0 ||
      token.length > 2_048 ||
      (!token.startsWith("HBPOSE1-") &&
        !token.startsWith("HBPOSE2-"))
    ) {
      throw bridgeError("Invalid emergency token input.");
    }
    if (
      !Array.isArray(input.publicKeys) ||
      input.publicKeys.length > 128
    ) {
      throw bridgeError("Invalid emergency public-key collection.");
    }
    const publicKeys: NativeEmergencyPublicKey[] = [];
    for (const key of input.publicKeys) {
      const normalized = emergencyPublicKey(key);
      if (!normalized) {
        throw bridgeError("Invalid emergency public key.");
      }
      publicKeys.push(normalized);
    }
    const nativeInput: NativeEmergencyVerificationInput =
      Object.freeze({
        expectedStoreCode: boundedCode(
          input.expectedStoreCode,
          50,
          "expectedStoreCode",
        ),
        nowEpochMs: safeEpoch(input.nowEpochMs),
        publicKeys: Object.freeze(publicKeys),
        token,
      });
    const value =
      await this.native.verifyEs256P256Token(nativeInput);
    return verificationResult(value);
  }
}

function verificationResult(
  value: unknown,
): EmergencyTokenCryptoResult {
  if (!isPlainObject(value) || typeof value.ok !== "boolean") {
    throw bridgeError("Invalid emergency verification response.");
  }
  if (value.ok === false) {
    const object = exactObject(value, ["errorCode", "ok"]);
    if (
      typeof object.errorCode !== "string" ||
      !EMERGENCY_ERROR_CODES.has(object.errorCode)
    ) {
      throw bridgeError("Invalid emergency verification error.");
    }
    return Object.freeze({
      errorCode: object.errorCode,
      ok: false,
    });
  }

  const object = exactObject(value, ["claims", "ok"]);
  const claims = exactObject(object.claims, [
    "expiresAtEpochMs",
    "grantId",
    "notBeforeEpochMs",
    "storeCode",
  ]);
  if (
    typeof claims.grantId !== "string" ||
    !UUID.test(claims.grantId) ||
    typeof claims.storeCode !== "string" ||
    !validBoundedCode(claims.storeCode, 50) ||
    !isSafeEpoch(claims.notBeforeEpochMs) ||
    !isSafeEpoch(claims.expiresAtEpochMs) ||
    Number(claims.expiresAtEpochMs) <=
      Number(claims.notBeforeEpochMs)
  ) {
    throw bridgeError("Invalid emergency claims response.");
  }
  return Object.freeze({
    claims: Object.freeze({
      expiresAtEpochMs: Number(claims.expiresAtEpochMs),
      grantId: claims.grantId,
      notBeforeEpochMs: Number(claims.notBeforeEpochMs),
      storeCode: claims.storeCode,
    }),
    ok: true,
  });
}

function emergencyPublicKey(
  value: EmergencyPublicKey,
): NativeEmergencyPublicKey | null {
  if (
    !isPlainObject(value) ||
    value.algorithm !== "ES256" ||
    typeof value.kid !== "string" ||
    !EMERGENCY_KID.test(value.kid) ||
    typeof value.fingerprintHex !== "string" ||
    !SHA256_HEX.test(value.fingerprintHex) ||
    typeof value.publicKeyPem !== "string" ||
    value.publicKeyPem.length < 64 ||
    value.publicKeyPem.length > 8_192 ||
    !value.publicKeyPem.includes("-----BEGIN PUBLIC KEY-----") ||
    !value.publicKeyPem.includes("-----END PUBLIC KEY-----") ||
    value.publicKeyPem.includes("PRIVATE KEY")
  ) {
    return null;
  }
  return Object.freeze({
    algorithm: "ES256",
    fingerprintHex: value.fingerprintHex.toUpperCase(),
    kid: value.kid,
    publicKeyPem: value.publicKeyPem,
  });
}

function exactObject(
  value: unknown,
  keys: readonly string[],
): Record<string, unknown> {
  if (!isPlainObject(value)) {
    throw bridgeError("Invalid native object response.");
  }
  const actualKeys = Object.keys(value).sort();
  const expectedKeys = [...keys].sort();
  if (
    actualKeys.length !== expectedKeys.length ||
    actualKeys.some((key, index) => key !== expectedKeys[index])
  ) {
    throw bridgeError("Unexpected native response fields.");
  }
  return value;
}

function isPlainObject(
  value: unknown,
): value is Record<string, unknown> {
  return (
    typeof value === "object" &&
    value !== null &&
    !Array.isArray(value)
  );
}

function opaqueHandle(value: unknown): string {
  if (typeof value !== "string" || !OPAQUE_HANDLE.test(value)) {
    throw bridgeError("Invalid opaque key handle.");
  }
  return value;
}

function attendanceKid(value: unknown): string {
  if (typeof value !== "string" || !ATTENDANCE_KID.test(value)) {
    throw bridgeError("Invalid attendance kid.");
  }
  return value;
}

function boundedCode(
  value: unknown,
  maxLength: number,
  field: string,
): string {
  if (typeof value !== "string" || !validBoundedCode(value, maxLength)) {
    throw bridgeError(`Invalid ${field}.`);
  }
  return value;
}

function validBoundedCode(
  value: string,
  maxLength: number,
): boolean {
  return (
    value.length > 0 &&
    value.length <= maxLength &&
    value.trim() === value &&
    !/[\u0000-\u001f\u007f]/u.test(value)
  );
}

function safeEpoch(value: unknown): number {
  if (!isSafeEpoch(value)) {
    throw bridgeError("Invalid epoch timestamp.");
  }
  return Number(value);
}

function isSafeEpoch(value: unknown): boolean {
  return (
    typeof value === "number" &&
    Number.isSafeInteger(value) &&
    value >= 0
  );
}

function bridgeError(message: string): AttendanceSecurityBridgeError {
  return new AttendanceSecurityBridgeError(message);
}
