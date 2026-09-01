package expo.modules.hbappinstaller

import org.junit.Assert.assertThrows
import org.junit.Test

class HBAppInstallerSignerPolicyTest {
  @Test fun `accepts certificate rotation only when installed signer is in archive lineage`() {
    HBAppInstallerSignerPolicy.validate(single(OLD), single(NEW, setOf(OLD, NEW)))
  }

  @Test fun `rejects an archive signed by an unrelated certificate`() {
    assertThrows(InstallerException::class.java) {
      HBAppInstallerSignerPolicy.validate(single(OLD), single(NEW))
    }
  }

  @Test fun `requires equal signer sets for multiple signers`() {
    assertThrows(InstallerException::class.java) {
      HBAppInstallerSignerPolicy.validate(multi(OLD, NEW), multi(NEW, THIRD))
    }
  }

  private fun single(current: String, history: Set<String> = setOf(current)) = SignerEvidence(false, setOf(current), history)
  private fun multi(vararg values: String) = SignerEvidence(true, values.toSet(), emptySet())
  private companion object { val OLD = "11".repeat(32); val NEW = "AA".repeat(32); val THIRD = "F0".repeat(32) }
}
