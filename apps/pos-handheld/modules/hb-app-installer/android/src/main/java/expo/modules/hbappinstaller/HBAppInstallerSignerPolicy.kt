package expo.modules.hbappinstaller

import expo.modules.kotlin.exception.CodedException
import java.util.Locale

private val PLAIN_SHA256_FINGERPRINT = Regex("^[A-Fa-f0-9]{64}$")
private val COLON_SHA256_FINGERPRINT = Regex("^(?:[A-Fa-f0-9]{2}:){31}[A-Fa-f0-9]{2}$")

internal class InstallerException(
  code: String,
  message: String,
  cause: Throwable? = null,
) : CodedException(code, message, cause)

internal data class SignerEvidence(
  val hasMultipleSigners: Boolean,
  val currentSignerDigests: Set<String>,
  val signingCertificateHistory: Set<String>,
)

internal fun normalizeSigningCertificateSha256(value: String): String {
  if (!PLAIN_SHA256_FINGERPRINT.matches(value) && !COLON_SHA256_FINGERPRINT.matches(value)) {
    throw InstallerException(
      "APP_INSTALL_METADATA_INVALID",
      "已验证 APK 签名证书 SHA-256 无效。",
    )
  }
  return value.replace(":", "").uppercase(Locale.US)
}

internal object HBAppInstallerSignerPolicy {
  fun validate(
    expectedSigningCertificateSha256: String,
    installed: SignerEvidence,
    archive: SignerEvidence,
  ) {
    val expected = normalizeSigningCertificateSha256(expectedSigningCertificateSha256)
    validateEvidence(installed)
    validateEvidence(archive)

    if (installed.hasMultipleSigners || archive.hasMultipleSigners) {
      // 多签名不能套用轮换 lineage：两端都必须是多签名，且 signer set 一项不多、一项不少。
      if (
        !installed.hasMultipleSigners ||
        !archive.hasMultipleSigners ||
        installed.currentSignerDigests != archive.currentSignerDigests ||
        expected !in archive.currentSignerDigests
      ) {
        throw signerMismatch()
      }
      return
    }

    val installedCurrent = installed.currentSignerDigests.single()
    val archiveCurrent = archive.currentSignerDigests.single()

    // 后端指纹必须钉住 APK 当前 signer；仅命中旧历史证书不足以授权安装。
    if (
      expected != archiveCurrent ||
      installedCurrent !in archive.signingCertificateHistory ||
      expected !in archive.signingCertificateHistory
    ) {
      throw signerMismatch()
    }
  }

  private fun validateEvidence(evidence: SignerEvidence) {
    val malformedCurrent = evidence.currentSignerDigests.any {
      !PLAIN_SHA256_FINGERPRINT.matches(it)
    }
    val malformedHistory = evidence.signingCertificateHistory.any {
      !PLAIN_SHA256_FINGERPRINT.matches(it)
    }
    val ambiguous = if (evidence.hasMultipleSigners) {
      evidence.currentSignerDigests.size < 2 || evidence.signingCertificateHistory.isNotEmpty()
    } else {
      evidence.currentSignerDigests.size != 1 ||
        evidence.signingCertificateHistory.isEmpty() ||
        evidence.currentSignerDigests.singleOrNull() !in evidence.signingCertificateHistory
    }
    if (malformedCurrent || malformedHistory || ambiguous) {
      throw InstallerException(
        "APP_INSTALL_SIGNER_UNREADABLE",
        "无法无歧义地读取 APK 签名证书。",
      )
    }
  }

  private fun signerMismatch() = InstallerException(
    "APP_INSTALL_SIGNER_MISMATCH",
    "APK 签名与当前 HB POS 应用或已验证的服务端元数据不一致。",
  )
}
