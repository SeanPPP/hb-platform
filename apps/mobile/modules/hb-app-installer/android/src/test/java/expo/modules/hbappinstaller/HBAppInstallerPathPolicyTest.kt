package expo.modules.hbappinstaller

import java.io.File
import java.nio.file.Files
import org.junit.After
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HBAppInstallerPathPolicyTest {
  private val root = Files.createTempDirectory("hb-installer-path-test").toFile()
  private val updateDirectory = File(root, "hb-app-updates").apply { mkdirs() }

  @After
  fun tearDown() {
    root.deleteRecursively()
  }

  @Test
  fun `FileProvider candidate must be a direct child with an HB APK filename`() {
    val allowedDirectories = setOf(updateDirectory)

    assertTrue(isManagedApkPath(File(updateDirectory, "hb-release-17.apk"), allowedDirectories))
    assertFalse(isManagedApkPath(File(updateDirectory, "release-17.apk"), allowedDirectories))
    assertFalse(isManagedApkPath(File(updateDirectory, "hb-release-17.apk.part"), allowedDirectories))
    assertFalse(isManagedApkPath(File(updateDirectory, "nested/hb-release-17.apk"), allowedDirectories))
  }

  @Test
  fun `canonical traversal cannot escape the FileProvider update directory`() {
    val escaped = File(updateDirectory, "../outside/hb-release-17.apk")

    assertFalse(isManagedApkPath(escaped, setOf(updateDirectory)))
  }
}
