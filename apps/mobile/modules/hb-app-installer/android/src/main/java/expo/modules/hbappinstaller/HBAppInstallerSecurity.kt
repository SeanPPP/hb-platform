package expo.modules.hbappinstaller

import expo.modules.kotlin.exception.CodedException
import java.io.File
import java.security.MessageDigest
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap

internal const val APK_DOWNLOAD_MAX_SIZE_BYTES = 300L * 1024L * 1024L
internal val SHA256_HEX = Regex("^[A-Fa-f0-9]{64}$")
private const val ANDROID_O_API_LEVEL = 26
private val MANAGED_APK_FILE_NAME = Regex("^hb-[A-Za-z0-9._-]+\\.apk$", RegexOption.IGNORE_CASE)

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

internal fun normalizedSha256(value: String, code: String): String {
  if (!SHA256_HEX.matches(value)) {
    throw InstallerException(code, "APK SHA-256 元数据无效。")
  }
  return value.uppercase(Locale.US)
}

/** API 24–27 只允许读取旧 versionCode 字段；API 28+ 必须走平台长整型字段。 */
internal fun resolveLegacyPackageVersionCode(
  sdkInt: Int,
  legacyVersionCode: Int,
): Long {
  require(sdkInt < 28) { "API 28+ 不得回退读取旧 versionCode 字段。" }
  return legacyVersionCode.toLong()
}

internal enum class InstallPermissionSettingsPage {
  SECURITY,
  APP_SPECIFIC,
}

/** Android 7 没有按来源授权 API；不得链接或调用 Android 8 才提供的方法。 */
internal object HBAppInstallerInstallPermissionPolicy {
  fun requiresAppSpecificPermission(sdkInt: Int): Boolean = sdkInt >= ANDROID_O_API_LEVEL

  fun settingsPage(sdkInt: Int): InstallPermissionSettingsPage =
    if (requiresAppSpecificPermission(sdkInt)) InstallPermissionSettingsPage.APP_SPECIFIC
    else InstallPermissionSettingsPage.SECURITY
}

internal data class VerifiedApkIdentity(
  val packageName: String,
  val versionCode: Long,
)

/**
 * 将身份复验与系统安装器启动串成不可跳过的顺序，并允许 JVM 测试注入边界实现。
 */
internal class HBAppInstallerInstallCoordinator(
  private val verifyIdentity: (File, VerifyMetadata) -> VerifiedApkIdentity,
  private val launchInstaller: (File) -> Unit,
) {
  fun verifyApk(apk: File, metadata: VerifyMetadata): VerifiedApkIdentity = verifyIdentity(apk, metadata)

  fun installVerifiedApk(apk: File, metadata: VerifyMetadata): VerifiedApkIdentity {
    // 展示弹窗前的旧校验不能替代此处复验；失败时绝不能触发 Intent。
    val identity = verifyIdentity(apk, metadata)
    launchInstaller(apk)
    return identity
  }
}

/** FileProvider 前置边界：只接受受控目录的直接子文件和固定 HB APK 命名。 */
internal fun isManagedApkPath(file: File, allowedDirectories: Set<File>): Boolean {
  val canonicalFile = try {
    file.canonicalFile
  } catch (_: Exception) {
    return false
  }
  val parent = canonicalFile.parentFile ?: return false
  val canonicalDirectories = allowedDirectories.mapNotNull {
    try {
      it.canonicalFile
    } catch (_: Exception) {
      null
    }
  }.toSet()
  return parent in canonicalDirectories && MANAGED_APK_FILE_NAME.matches(canonicalFile.name)
}

/**
 * Android 11+ 的 package visibility 会让 resolveActivity 产生假阴性；直接启动并捕获真实失败。
 */
internal fun launchSystemActivity(
  unavailableCode: String,
  unavailableMessage: String,
  startActivity: () -> Unit,
) {
  try {
    startActivity()
  } catch (error: Exception) {
    throw InstallerException(unavailableCode, unavailableMessage, error)
  }
}

/**
 * 一个 APK 的下载、校验、安装不能并发交错。锁只在本进程生效，文件仍必须在每次使用前复验。
 */
internal object HBAppInstallerTargetLock {
  private val activeTargets = ConcurrentHashMap<String, Any>()

  fun <T> withLock(target: File, action: () -> T): T {
    val key = target.canonicalPath
    val token = Any()
    if (activeTargets.putIfAbsent(key, token) != null) {
      throw InstallerException("APP_INSTALL_OPERATION_IN_PROGRESS", "同一 APK 更新正在处理中。")
    }
    try {
      return action()
    } finally {
      activeTargets.remove(key, token)
    }
  }

  fun tryWithLock(target: File, action: () -> Unit): Boolean {
    val key = target.canonicalPath
    val token = Any()
    if (activeTargets.putIfAbsent(key, token) != null) return false
    try {
      action()
      return true
    } finally {
      activeTargets.remove(key, token)
    }
  }
}

internal object HBAppInstallerSignerPolicy {
  fun validate(installed: SignerEvidence, archive: SignerEvidence) {
    validateEvidence(installed)
    validateEvidence(archive)
    if (installed.hasMultipleSigners || archive.hasMultipleSigners) {
      if (
        !installed.hasMultipleSigners ||
        !archive.hasMultipleSigners ||
        installed.currentSignerDigests != archive.currentSignerDigests
      ) throw signerMismatch()
      return
    }
    val installedCurrent = installed.currentSignerDigests.single()
    val archiveCurrent = archive.currentSignerDigests.single()
    // 单签名轮换时，新 APK 的 lineage 必须包含当前已安装 signer；历史旧 signer 不能授权新包。
    if (
      installedCurrent !in archive.signingCertificateHistory ||
      archiveCurrent !in archive.signingCertificateHistory
    ) throw signerMismatch()
  }

  private fun validateEvidence(evidence: SignerEvidence) {
    val malformed = (evidence.currentSignerDigests + evidence.signingCertificateHistory).any {
      !SHA256_HEX.matches(it)
    }
    val ambiguous = if (evidence.hasMultipleSigners) {
      evidence.currentSignerDigests.size < 2 || evidence.signingCertificateHistory.isNotEmpty()
    } else {
      evidence.currentSignerDigests.size != 1 ||
        evidence.signingCertificateHistory.isEmpty() ||
        evidence.currentSignerDigests.singleOrNull() !in evidence.signingCertificateHistory
    }
    if (malformed || ambiguous) {
      throw InstallerException("APP_INSTALL_SIGNER_UNREADABLE", "无法无歧义地读取 APK 签名证书。")
    }
  }

  private fun signerMismatch() = InstallerException(
    "APP_INSTALL_SIGNER_MISMATCH",
    "APK 签名与当前应用或已验证的服务端元数据不一致。",
  )
}

internal fun sha256File(file: File): String {
  val digest = MessageDigest.getInstance("SHA-256")
  file.inputStream().buffered().use { input ->
    val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
    while (true) {
      val count = input.read(buffer)
      if (count < 0) break
      if (count > 0) digest.update(buffer, 0, count)
    }
  }
  return digest.digest().joinToString("") { "%02X".format(Locale.US, it.toInt() and 0xff) }
}
