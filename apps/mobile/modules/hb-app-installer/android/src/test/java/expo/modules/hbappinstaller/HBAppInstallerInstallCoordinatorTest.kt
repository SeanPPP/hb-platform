package expo.modules.hbappinstaller

import java.io.File
import java.nio.file.Files
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class HBAppInstallerInstallCoordinatorTest {
  private val root = Files.createTempDirectory("hb-installer-coordinator-test").toFile()

  @After
  fun tearDown() {
    root.deleteRecursively()
  }

  @Test
  fun `install re-verifies and never launches after the verified file is replaced`() {
    val target = File(root, "hb-release.apk").apply { writeText("verified") }
    val metadata = metadata(target)
    var verificationCount = 0
    var launchCount = 0
    val coordinator = HBAppInstallerInstallCoordinator(
      verifyIdentity = { file, _ ->
        verificationCount += 1
        if (file.readText() != "verified") {
          throw InstallerException("APP_INSTALL_SHA256_MISMATCH", "安装前复验失败。")
        }
        VerifiedApkIdentity("com.hbweb.expo", 17)
      },
      launchInstaller = {
        launchCount += 1
      },
    )

    coordinator.verifyApk(target, metadata)
    target.writeText("tampered")

    assertThrows(InstallerException::class.java) {
      coordinator.installVerifiedApk(target, metadata)
    }
    assertEquals(2, verificationCount)
    assertEquals(0, launchCount)
  }

  @Test
  fun `successful install verifies before launching exactly once`() {
    val target = File(root, "hb-release.apk").apply { writeText("verified") }
    val events = mutableListOf<String>()
    val coordinator = HBAppInstallerInstallCoordinator(
      verifyIdentity = { _, _ ->
        events += "verify"
        VerifiedApkIdentity("com.hbweb.expo", 17)
      },
      launchInstaller = {
        events += "launch"
      },
    )

    val result = coordinator.installVerifiedApk(target, metadata(target))

    assertEquals(listOf("verify", "launch"), events)
    assertEquals(VerifiedApkIdentity("com.hbweb.expo", 17), result)
  }

  private fun metadata(target: File) = VerifyMetadata(
    fileUri = target.toURI().toString(),
    expectedSizeBytes = target.length(),
    expectedSha256Hex = "AA".repeat(32),
    expectedPackageName = "com.hbweb.expo",
    expectedVersionCode = 17,
    expectedVersionName = "1.0.3",
  )
}
