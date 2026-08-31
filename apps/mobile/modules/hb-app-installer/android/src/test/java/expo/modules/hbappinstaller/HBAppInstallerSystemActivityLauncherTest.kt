package expo.modules.hbappinstaller

import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class HBAppInstallerSystemActivityLauncherTest {
  @Test
  fun `launch attempts the system activity without a package visibility preflight`() {
    var launched = false

    launchSystemActivity("APP_INSTALLER_UNAVAILABLE", "系统安装器不可用。") {
      launched = true
    }

    assertTrue(launched)
  }

  @Test
  fun `launch failure is converted to an installer error`() {
    assertThrows(InstallerException::class.java) {
      launchSystemActivity("APP_INSTALLER_UNAVAILABLE", "系统安装器不可用。") {
        throw IllegalStateException("activity missing")
      }
    }
  }
}
