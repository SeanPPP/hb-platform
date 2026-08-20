# HB POS 加密出口合规技术草稿

本文用于帮助有权限的 Account Holder、Admin 或 App Manager 在 App Store Connect
完成加密问卷。它不是法律意见，也不得据此自行声称豁免。

## 当前二进制使用的加密

- `expo-sqlite` 启用 `useSQLCipher: true`，本地 POS 账本使用 SQLCipher。
- SQLCipher 数据库密钥由 `expo-crypto` 生成并保存在 iOS Keychain。
- `SensitivePayloadEncryptor` 使用 `@noble/ciphers` 的
  XChaCha20-Poly1305 做字段级认证加密；该实现不是仅调用 Apple 操作系统加密 API。
- HTTPS/TLS、Keychain 和系统安全存储仍会使用 Apple 操作系统提供的加密能力。

因此，HB POS 不能按“App 不使用加密”回答。是否属于可豁免加密、是否需要美国
分类或法国声明，必须通过 App Store Connect 问卷并由负责出口合规的人员确认。

## Info.plist 决策

- 在问卷和所需文稿未确认前，不设置
  `ITSAppUsesNonExemptEncryption=false`。
- Apple 确认无需文稿且负责人确认属于豁免后，才可设置为 `false`。
- 若 Apple 判定使用非豁免加密，则设置为 `true`，并在 Apple 审核文稿后按其提供的
  key value 配置 `ITSEncryptionExportComplianceCode`。

## App Store Connect 操作

1. 进入 **App Information > App Encryption Documentation**。
2. 按最终二进制实际使用情况回答问卷，不要按“POS 业务用途”猜测算法分类。
3. 如需文稿，先填写 App Description 与 Availability，再上传完整文稿。
4. 文稿批准后，将批准记录关联到对应 TestFlight/App Store build。
5. 重新导出最终 IPA，核对 Info.plist 与 App Store Connect 的批准结果一致。

Apple 官方参考：

- <https://developer.apple.com/cn/help/app-store-connect/manage-app-information/overview-of-export-compliance>
- <https://developer.apple.com/cn/help/app-store-connect/manage-app-information/determine-and-upload-app-encryption-documentation>
- <https://developer.apple.com/documentation/bundleresources/information-property-list/itsappusesnonexemptencryption>

## 负责人确认

- [ ] 最终支持的分发国家/地区已确定，特别是是否包含法国。
- [ ] SQLCipher 与 XChaCha20-Poly1305 的用途和实现已向合规负责人完整披露。
- [ ] App Store Connect 问卷结果已保存。
- [ ] 所需文稿已批准并关联到最终 build，或 Apple 明确判定无需文稿。
- [ ] 最终 Info.plist 没有与问卷结果矛盾的布尔值或旧批准代码。
