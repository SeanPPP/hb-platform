// src/pages/System/WpfVersions/logic.test.ts
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { webcrypto } from "node:crypto";

// src/pages/System/WpfVersions/logic.ts
function normalizeWpfReleaseChannel(channel) {
  const normalized = channel?.trim().toLowerCase();
  return normalized || "production";
}
function isSupportedWpfInstallerFile(fileName) {
  return /\.(exe|msi)$/i.test(fileName.trim());
}
function getWpfVersionErrorMessage(error, fallback) {
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }
  return fallback;
}
function inferWpfInstallerType(fileName) {
  const normalized = fileName.trim().toLowerCase();
  return normalized.endsWith(".msi") ? "msi" : "exe";
}
function getDefaultWpfInstallerArguments(fileName) {
  return inferWpfInstallerType(fileName) === "msi" ? "/qn /norestart" : "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS";
}
async function calculateFileSha256(file) {
  if (!globalThis.crypto?.subtle) {
    throw new Error("SHA-256 calculation is not available in this browser");
  }
  const digest = await globalThis.crypto.subtle.digest("SHA-256", await file.arrayBuffer());
  return Array.from(new Uint8Array(digest)).map((byte) => byte.toString(16).padStart(2, "0")).join("");
}
function buildWpfPolicyPayload(input) {
  return {
    channel: normalizeWpfReleaseChannel(input.channel),
    targetVersion: input.targetVersion.trim(),
    minimumSupportedVersion: input.minimumSupportedVersion.trim(),
    forceUpdate: input.forceUpdate,
    isRollback: input.isRollback,
    rollbackConfirmed: input.rollbackConfirmed
  };
}
function canSubmitWpfPolicy(input) {
  return Boolean(input.targetVersion.trim() && input.minimumSupportedVersion.trim());
}
function getEffectiveWpfMinimumSupportedVersion(input) {
  const targetVersion = input.targetVersion.trim();
  const minimumSupportedVersion = input.minimumSupportedVersion.trim();
  if (!targetVersion) {
    return minimumSupportedVersion;
  }
  if (!minimumSupportedVersion) {
    return targetVersion;
  }
  const comparison = compareWpfVersion(minimumSupportedVersion, targetVersion);
  if (comparison !== null && comparison > 0) {
    return targetVersion;
  }
  return minimumSupportedVersion;
}
function getWpfPolicyRangeError(input) {
  const targetVersion = input.targetVersion.trim();
  const minimumSupportedVersion = input.minimumSupportedVersion.trim();
  if (!targetVersion || !minimumSupportedVersion) {
    return null;
  }
  const comparison = compareWpfVersion(minimumSupportedVersion, targetVersion);
  return comparison !== null && comparison > 0 ? "INVALID_VERSION_RANGE" : null;
}
function isWpfRollbackTarget(targetVersion, releases) {
  const baselineVersion = releases.find((item) => item.targetVersion)?.targetVersion ?? releases.find((item) => item.isCurrent)?.version ?? null;
  if (!baselineVersion) {
    return false;
  }
  const comparison = compareWpfVersion(targetVersion, baselineVersion);
  return comparison !== null && comparison < 0;
}
function getWpfCurrentVersionText(releases) {
  const policyTarget = releases.find((item) => item.targetVersion?.trim())?.targetVersion?.trim();
  if (policyTarget) {
    return policyTarget;
  }
  return releases.find((item) => item.isCurrent)?.version ?? null;
}
function getWpfPolicySummary(releases) {
  const policyCarrier = releases.find((item) => item.targetVersion?.trim()) ?? releases.find((item) => item.isCurrent);
  if (!policyCarrier) {
    return null;
  }
  const targetVersion = policyCarrier.targetVersion?.trim() || (policyCarrier.isCurrent ? policyCarrier.version.trim() : "");
  if (!targetVersion) {
    return null;
  }
  const currentRelease = releases.find((item) => item.isCurrent || item.version.trim() === targetVersion);
  return {
    channel: normalizeWpfReleaseChannel((currentRelease ?? policyCarrier).channel),
    targetVersion,
    minimumSupportedVersion: policyCarrier.minimumSupportedVersion?.trim() || targetVersion,
    forceUpdate: Boolean(policyCarrier.forceUpdate || currentRelease?.forceUpdate)
  };
}
function compareWpfVersion(left, right) {
  const leftParts = parseWpfVersion(left);
  const rightParts = parseWpfVersion(right);
  if (!leftParts || !rightParts) {
    return null;
  }
  for (let index = 0; index < Math.max(leftParts.length, rightParts.length); index += 1) {
    const delta = (leftParts[index] ?? 0) - (rightParts[index] ?? 0);
    if (delta !== 0) {
      return delta;
    }
  }
  return 0;
}
function parseWpfVersion(version) {
  const match = version.trim().match(/^v?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$/i);
  if (!match) {
    return null;
  }
  return match.slice(1).map((part) => Number(part ?? 0));
}

// src/pages/System/WpfVersions/logic.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
function assertTruthy(value, message) {
  if (!value) {
    throw new Error(message);
  }
}
function getNestedValue(source, path) {
  return path.split(".").reduce((current, segment) => {
    if (!current || typeof current !== "object") {
      return void 0;
    }
    return current[segment];
  }, source);
}
function collectWpfVersionLocaleKeys() {
  const source = readFileSync(resolve(process.cwd(), "src/pages/System/WpfVersions/index.tsx"), "utf8");
  return [...new Set(
    [...source.matchAll(/t\('system\.wpfVersions\.([^']+)'/g)].map((match) => `system.wpfVersions.${match[1]}`)
  )].sort();
}
function loadLocale(localeName) {
  return JSON.parse(readFileSync(resolve(process.cwd(), `src/i18n/locales/${localeName}.json`), "utf8"));
}
assertEqual(normalizeWpfReleaseChannel(" Preview "), "preview", "Channel should trim and lower-case");
assertEqual(normalizeWpfReleaseChannel(""), "production", "Empty channel should fall back to production");
assertEqual(isSupportedWpfInstallerFile("hbpos-1.2.3.msi"), true, "MSI installers should be accepted");
assertEqual(isSupportedWpfInstallerFile("hbpos-1.2.3.exe"), true, "EXE installers should be accepted");
assertEqual(isSupportedWpfInstallerFile("hbpos-1.2.3.zip"), false, "Unsupported installer extensions should be rejected");
assertEqual(
  getDefaultWpfInstallerArguments("hbpos-1.2.3.msi"),
  "/qn /norestart",
  "MSI releases should default to msiexec silent arguments"
);
assertEqual(
  getDefaultWpfInstallerArguments("hbpos-1.2.3.exe"),
  "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS",
  "Inno EXE releases should default to Inno silent arguments"
);
if (!globalThis.crypto?.subtle) {
  Object.defineProperty(globalThis, "crypto", {
    value: webcrypto,
    configurable: true
  });
}
assertEqual(
  await calculateFileSha256(new Blob(["hbpos-installer"])),
  "bde82e8a4fb92ddaebe22918141ddbf90e926a2e24946c90da3be1d6fd18d6fc",
  "Installer SHA-256 should be calculated from file contents"
);
assertDeepEqual(
  buildWpfPolicyPayload({
    channel: " Preview ",
    targetVersion: " 1.2.3 ",
    minimumSupportedVersion: " 1.0.0 ",
    forceUpdate: true,
    isRollback: true,
    rollbackConfirmed: true
  }),
  {
    channel: "preview",
    targetVersion: "1.2.3",
    minimumSupportedVersion: "1.0.0",
    forceUpdate: true,
    isRollback: true,
    rollbackConfirmed: true
  },
  "Rollback policy payload should keep target, minimum version, force flag, rollback flag, and confirmation"
);
assertEqual(
  isWpfRollbackTarget("1.1.0", [
    { version: "1.2.0", isCurrent: true, targetVersion: "1.2.0" },
    { version: "1.1.0", isCurrent: false, targetVersion: "1.2.0" }
  ]),
  true,
  "Choosing a version lower than the current target should require rollback confirmation"
);
assertEqual(
  isWpfRollbackTarget("1.2.0", [
    { version: "1.2.0", isCurrent: true, targetVersion: "1.2.0" },
    { version: "1.1.0", isCurrent: false, targetVersion: "1.2.0" }
  ]),
  false,
  "Choosing the current target should not require rollback confirmation"
);
assertEqual(
  isWpfRollbackTarget("1.1.0", [
    { version: "1.3.0", isCurrent: false, targetVersion: null },
    { version: "1.1.0", isCurrent: false, targetVersion: null }
  ]),
  false,
  "Choosing below the latest active release should not require rollback confirmation without a current policy"
);
assertEqual(
  isWpfRollbackTarget("1.1.0", [
    { version: "1.3.0", isCurrent: false, targetVersion: "1.2.0" },
    { version: "1.1.0", isCurrent: false, targetVersion: "1.2.0" }
  ]),
  true,
  "Policy target metadata should allow rollback confirmation when the current target is not in the page"
);
assertEqual(
  isWpfRollbackTarget("1.2.0", [
    { version: "1.3.0", isCurrent: false, targetVersion: null },
    { version: "1.1.0", isCurrent: false, targetVersion: null }
  ]),
  false,
  "Paged release lists without current policy metadata should not infer rollback from active releases"
);
assertEqual(
  isWpfRollbackTarget("1.1.0", [
    { version: "1.0.0", isCurrent: true, targetVersion: "1.2.0" },
    { version: "1.2.0", isCurrent: false, targetVersion: "1.2.0" }
  ]),
  true,
  "When a current release row carries targetVersion metadata, rollback checks should prioritize the current policy target"
);
assertEqual(
  getWpfCurrentVersionText([
    { version: "1.3.0", isCurrent: false, targetVersion: "1.2.0" },
    { version: "1.1.0", isCurrent: false, targetVersion: "1.2.0" }
  ]),
  "1.2.0",
  "Summary should show policy target version when the current release is not in the page"
);
assertEqual(
  getWpfCurrentVersionText([
    { version: "1.3.0", isCurrent: false, targetVersion: null },
    { version: "1.1.0", isCurrent: false, targetVersion: null }
  ]),
  null,
  "Summary should not infer current version from the first paged release without policy metadata"
);
assertDeepEqual(
  getWpfPolicySummary([
    {
      channel: "Production",
      version: "1.3.0",
      isCurrent: false,
      targetVersion: "1.2.0",
      minimumSupportedVersion: "1.0.0",
      forceUpdate: true
    }
  ]),
  {
    channel: "production",
    targetVersion: "1.2.0",
    minimumSupportedVersion: "1.0.0",
    forceUpdate: true
  },
  "Policy summary should preserve force-update metadata when the current target is not in the page"
);
assertEqual(
  canSubmitWpfPolicy({
    targetVersion: "1.2.3",
    minimumSupportedVersion: "1.0.0"
  }),
  true,
  "Policy can be submitted when target and minimum versions are present"
);
assertEqual(
  canSubmitWpfPolicy({
    targetVersion: "1.2.3",
    minimumSupportedVersion: ""
  }),
  false,
  "Policy should require a minimum supported version"
);
assertEqual(
  getWpfPolicyRangeError({
    targetVersion: "1.2.0",
    minimumSupportedVersion: "1.2.1"
  }),
  "INVALID_VERSION_RANGE",
  "Policy should reject a minimum supported version above the target version"
);
assertEqual(
  getWpfPolicyRangeError({
    targetVersion: "1.2.0",
    minimumSupportedVersion: "1.2.0"
  }),
  null,
  "Policy should allow a minimum supported version equal to the target version"
);
assertEqual(
  getEffectiveWpfMinimumSupportedVersion({
    targetVersion: "1.1.0",
    minimumSupportedVersion: "1.2.0"
  }),
  "1.1.0",
  "Rollback to a lower target version should clamp the effective minimum supported version to the target version"
);
assertEqual(
  getWpfVersionErrorMessage(new Error("WPF_RELEASE_EXISTS: WPF release version already exists."), "fallback"),
  "WPF_RELEASE_EXISTS: WPF release version already exists.",
  "WPF versions page should surface backend code and message from service errors"
);
assertEqual(
  getWpfVersionErrorMessage("plain failure", "fallback"),
  "fallback",
  "WPF versions page should keep fallback text when the thrown value is not an Error"
);
var localeKeys = collectWpfVersionLocaleKeys();
var enLocale = loadLocale("en");
var zhLocale = loadLocale("zh");
assertTruthy(localeKeys.length > 0, "WPF Versions page should reference at least one locale key");
for (const localeKey of localeKeys) {
  assertTruthy(
    typeof getNestedValue(enLocale, localeKey) === "string",
    `English locale should define ${localeKey}`
  );
  assertTruthy(
    typeof getNestedValue(zhLocale, localeKey) === "string",
    `Chinese locale should define ${localeKey}`
  );
}
console.log("WpfVersions logic.test: ok");
