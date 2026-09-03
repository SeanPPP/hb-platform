package expo.modules.hbappinstaller

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class HBAppInstallerVersionCodeTest {
  @Test fun `Android 7 and 8 use the legacy version field`() {
    assertEquals(17L, resolveLegacyPackageVersionCode(24, 17))
    assertEquals(17L, resolveLegacyPackageVersionCode(27, 17))
  }

  @Test fun `Android 9 and newer cannot fall back to the legacy version field`() {
    assertThrows(IllegalArgumentException::class.java) {
      resolveLegacyPackageVersionCode(28, 17)
    }
  }
}
