export type NativeAttendanceIdentity = Readonly<{
  keyHandle: string;
  kid: string;
}>;

export type NativeAttendanceQrInput = Readonly<{
  deviceCode: string;
  issuedAtEpochMs: number;
  keyHandle: string;
  kid: string;
  storeCode: string;
}>;

export type NativeEmergencyPublicKey = Readonly<{
  algorithm: "ES256";
  fingerprintHex: string;
  kid: string;
  publicKeyPem: string;
}>;

export type NativeEmergencyVerificationInput = Readonly<{
  expectedStoreCode: string;
  nowEpochMs: number;
  publicKeys: readonly NativeEmergencyPublicKey[];
  token: string;
}>;

/**
 * Expo bridge 返回值在运行时仍是不可信的 `unknown`；适配器负责逐字段校验，
 * 避免原生实现变更把密钥或未声明字段带入业务状态。
 */
export interface HbAttendanceSecurityNativeModule {
  getSystemUptimeMilliseconds(): unknown;
  createA256Identity(): Promise<unknown>;
  hasA256Key(keyHandle: string): Promise<unknown>;
  readRegistrationKeyMaterial(keyHandle: string): Promise<unknown>;
  issueAttendanceQr(input: NativeAttendanceQrInput): Promise<unknown>;
  destroyA256Key(keyHandle: string): Promise<unknown>;
  validateEs256P256PublicKey(
    key: NativeEmergencyPublicKey,
  ): Promise<unknown>;
  verifyEs256P256Token(
    input: NativeEmergencyVerificationInput,
  ): Promise<unknown>;
}
