package expo.modules.hbappinstaller

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class HBAppInstallerSignerPolicyTest {
  @Test
  fun `normalizes supported fingerprint spelling`() {
    val colonSeparated = NEW_SIGNER.chunked(2).joinToString(":").lowercase()

    assertEquals(NEW_SIGNER, normalizeSigningCertificateSha256(NEW_SIGNER.lowercase()))
    assertEquals(NEW_SIGNER, normalizeSigningCertificateSha256(colonSeparated))
  }

  @Test
  fun `rejects malformed fingerprint spelling`() {
    assertThrows(InstallerException::class.java) {
      normalizeSigningCertificateSha256("$NEW_SIGNER:")
    }
    assertThrows(InstallerException::class.java) {
      normalizeSigningCertificateSha256(" ${NEW_SIGNER.lowercase()}")
    }
  }

  @Test
  fun `accepts a single signer update without rotation`() {
    HBAppInstallerSignerPolicy.validate(
      expectedSigningCertificateSha256 = OLD_SIGNER,
      installed = singleSigner(OLD_SIGNER),
      archive = singleSigner(OLD_SIGNER),
    )
  }

  @Test
  fun `accepts legitimate single signer rotation`() {
    HBAppInstallerSignerPolicy.validate(
      expectedSigningCertificateSha256 = NEW_SIGNER,
      installed = singleSigner(OLD_SIGNER),
      archive = singleSigner(NEW_SIGNER, setOf(OLD_SIGNER, NEW_SIGNER)),
    )
  }

  @Test
  fun `rejects backend fingerprint that is only an old history entry`() {
    assertThrows(InstallerException::class.java) {
      HBAppInstallerSignerPolicy.validate(
        expectedSigningCertificateSha256 = OLD_SIGNER,
        installed = singleSigner(OLD_SIGNER),
        archive = singleSigner(NEW_SIGNER, setOf(OLD_SIGNER, NEW_SIGNER)),
      )
    }
  }

  @Test
  fun `rejects single signer archive without installed signer in history`() {
    assertThrows(InstallerException::class.java) {
      HBAppInstallerSignerPolicy.validate(
        expectedSigningCertificateSha256 = NEW_SIGNER,
        installed = singleSigner(OLD_SIGNER),
        archive = singleSigner(NEW_SIGNER),
      )
    }
  }

  @Test
  fun `accepts only an unchanged exact multiple signer set`() {
    HBAppInstallerSignerPolicy.validate(
      expectedSigningCertificateSha256 = OLD_SIGNER,
      installed = multipleSigners(OLD_SIGNER, NEW_SIGNER),
      archive = multipleSigners(NEW_SIGNER, OLD_SIGNER),
    )
  }

  @Test
  fun `rejects a changed multiple signer set`() {
    assertThrows(InstallerException::class.java) {
      HBAppInstallerSignerPolicy.validate(
        expectedSigningCertificateSha256 = NEW_SIGNER,
        installed = multipleSigners(OLD_SIGNER, NEW_SIGNER),
        archive = multipleSigners(NEW_SIGNER, THIRD_SIGNER),
      )
    }
  }

  @Test
  fun `rejects ambiguous transition between single and multiple signer modes`() {
    assertThrows(InstallerException::class.java) {
      HBAppInstallerSignerPolicy.validate(
        expectedSigningCertificateSha256 = NEW_SIGNER,
        installed = singleSigner(OLD_SIGNER),
        archive = multipleSigners(OLD_SIGNER, NEW_SIGNER),
      )
    }
  }

  @Test
  fun `rejects unreadable signer evidence`() {
    assertThrows(InstallerException::class.java) {
      HBAppInstallerSignerPolicy.validate(
        expectedSigningCertificateSha256 = NEW_SIGNER,
        installed = SignerEvidence(
          hasMultipleSigners = false,
          currentSignerDigests = emptySet(),
          signingCertificateHistory = emptySet(),
        ),
        archive = singleSigner(NEW_SIGNER),
      )
    }
  }

  private fun singleSigner(
    current: String,
    history: Set<String> = setOf(current),
  ) = SignerEvidence(
    hasMultipleSigners = false,
    currentSignerDigests = setOf(current),
    signingCertificateHistory = history,
  )

  private fun multipleSigners(vararg signers: String) = SignerEvidence(
    hasMultipleSigners = true,
    currentSignerDigests = signers.toSet(),
    signingCertificateHistory = emptySet(),
  )

  private companion object {
    val OLD_SIGNER = "11".repeat(32)
    val NEW_SIGNER = "AA".repeat(32)
    val THIRD_SIGNER = "F0".repeat(32)
  }
}
