package expo.modules.hbappinstaller

import java.io.File
import java.nio.file.Files
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import org.junit.After
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HBAppInstallerTargetLockTest {
  private val root = Files.createTempDirectory("hb-installer-lock-test").toFile()

  @After
  fun tearDown() {
    root.deleteRecursively()
  }

  @Test
  fun `only one operation can own a target APK at a time`() {
    val target = File(root, "hb-release.apk")
    val entered = CountDownLatch(1)
    val release = CountDownLatch(1)

    val first = Thread {
      HBAppInstallerTargetLock.withLock(target) {
        entered.countDown()
        assertTrue(release.await(2, TimeUnit.SECONDS))
      }
    }
    first.start()
    assertTrue(entered.await(2, TimeUnit.SECONDS))

    assertFalse(HBAppInstallerTargetLock.tryWithLock(target) { })

    release.countDown()
    first.join(2_000)
    assertFalse(first.isAlive)
    assertTrue(HBAppInstallerTargetLock.tryWithLock(target) { })
  }
}
