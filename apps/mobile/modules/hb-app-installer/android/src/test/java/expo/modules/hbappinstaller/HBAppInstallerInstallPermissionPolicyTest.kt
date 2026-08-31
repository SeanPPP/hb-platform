package expo.modules.hbappinstaller

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HBAppInstallerInstallPermissionPolicyTest {
  @Test
  fun `Android 7 does not require the API 26 app-specific permission`() {
    for (sdkInt in listOf(24, 25)) {
      assertFalse(HBAppInstallerInstallPermissionPolicy.requiresAppSpecificPermission(sdkInt))
    }
  }

  @Test
  fun `Android 8 and newer require the app-specific package install permission`() {
    assertTrue(HBAppInstallerInstallPermissionPolicy.requiresAppSpecificPermission(26))
  }

  @Test
  fun `Android 7 uses security settings instead of the API 26 app-specific settings page`() {
    assertEquals(InstallPermissionSettingsPage.SECURITY, HBAppInstallerInstallPermissionPolicy.settingsPage(24))
    assertEquals(InstallPermissionSettingsPage.SECURITY, HBAppInstallerInstallPermissionPolicy.settingsPage(25))
    assertEquals(InstallPermissionSettingsPage.APP_SPECIFIC, HBAppInstallerInstallPermissionPolicy.settingsPage(26))
  }
}
