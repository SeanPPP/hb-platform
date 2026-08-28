export type ProtectedMaterialIntegrityErrorCode =
  | "PROTECTED_MATERIAL_JSON_INVALID"
  | "PROTECTED_MATERIAL_VERSION_INVALID"
  | "PROTECTED_MATERIAL_SHAPE_INVALID"
  | "PROTECTED_MATERIAL_BINDING_MISMATCH"
  | "PROTECTED_MATERIAL_CONTEXT_MISSING";

/**
 * 只表示密文已经成功解密后，可确定且不可重试的持久化内容损坏。
 * 解密器、Keychain、数据库和 IO 错误不得包装成此类型。
 */
export class ProtectedMaterialIntegrityError extends Error {
  public constructor(
    public readonly code: ProtectedMaterialIntegrityErrorCode,
  ) {
    super(`Protected material integrity check failed (${code}).`);
    this.name = "ProtectedMaterialIntegrityError";
  }
}
