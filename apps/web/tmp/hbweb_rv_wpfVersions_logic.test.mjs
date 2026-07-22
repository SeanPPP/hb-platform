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

// src/utils/latestRequestGuard.ts
function createLatestRequestGuard() {
  let latestRequestId = 0;
  return {
    begin() {
      latestRequestId += 1;
      return latestRequestId;
    },
    isLatest(requestId) {
      return latestRequestId === requestId;
    },
    invalidate() {
      latestRequestId += 1;
    }
  };
}
async function runLatestGuardedRequest(guard, operation, handlers) {
  const requestId = guard.begin();
  handlers.onStart?.();
  try {
    const result = await operation();
    if (guard.isLatest(requestId)) handlers.onSuccess(result);
  } catch (error) {
    if (guard.isLatest(requestId)) handlers.onError?.(error);
  } finally {
    if (guard.isLatest(requestId)) handlers.onSettled?.();
  }
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
function createDeferred() {
  let resolve2;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve2 = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve: resolve2 };
}
async function verifyWpfScopeChangeUsesOneFirstPageRequest() {
  const requestGuard = createLatestRequestGuard();
  let desiredQuery = {
    page: 3,
    pageSize: 10,
    channel: "production",
    includeDisabled: false,
    scopeRevision: 0
  };
  const state = { channel: "", page: 0 };
  const startedQueries = [];
  const load = (query, request) => {
    startedQueries.push(`${query.channel}:${query.page}`);
    return runLatestGuardedRequest(requestGuard, () => request, {
      onSuccess: (result) => {
        state.channel = result.channel;
        state.page = result.page;
      }
    });
  };
  const resetScope = (channel, includeDisabled) => {
    desiredQuery = {
      page: 1,
      pageSize: desiredQuery.pageSize,
      channel,
      includeDisabled,
      scopeRevision: desiredQuery.scopeRevision + 1
    };
    requestGuard.invalidate();
  };
  const refreshAfterMutation = (expectedQuery, targetChannel) => {
    const scopeChanged = expectedQuery.scopeRevision !== desiredQuery.scopeRevision;
    if (scopeChanged) {
      if (!targetChannel || targetChannel !== desiredQuery.channel) {
        return false;
      }
    }
    if (targetChannel && targetChannel !== desiredQuery.channel) {
      resetScope(targetChannel, desiredQuery.includeDisabled);
      return false;
    }
    return true;
  };
  const staleList = createDeferred();
  const staleListTask = load(desiredQuery, staleList.promise);
  const mutation = createDeferred();
  const mutationTask = mutation.promise.then(() => refreshAfterMutation({
    page: 3,
    pageSize: 10,
    channel: "production",
    includeDisabled: false,
    scopeRevision: 0
  }));
  resetScope("preview", false);
  const scopeList = createDeferred();
  const scopeListTask = load(desiredQuery, scopeList.promise);
  mutation.resolve();
  assertEqual(await mutationTask, false, "\u65E7 mutation \u4E0D\u5F97\u5728\u901A\u9053\u5207\u6362\u540E\u91CD\u65B0\u5F00\u59CB\u5217\u8868\u8BF7\u6C42");
  assertDeepEqual(startedQueries, ["production:3", "preview:1"], "\u901A\u9053\u5207\u6362\u53EA\u5E94\u8865\u53D1\u4E00\u6B21\u7B2C\u4E00\u9875\u8BF7\u6C42");
  scopeList.resolve({ ...desiredQuery });
  await scopeListTask;
  staleList.resolve({ page: 3, pageSize: 10, channel: "production", includeDisabled: false, scopeRevision: 0 });
  await staleListTask;
  assertEqual(state.channel, "preview", "\u65E7\u5217\u8868\u54CD\u5E94\u4E0D\u5F97\u8986\u76D6\u65B0\u901A\u9053");
  assertEqual(state.page, 1, "\u65E7\u5217\u8868\u54CD\u5E94\u4E0D\u5F97\u8986\u76D6\u65B0\u901A\u9053\u7684\u7B2C\u4E00\u9875");
  desiredQuery = {
    page: 3,
    pageSize: 10,
    channel: "production",
    includeDisabled: false,
    scopeRevision: 0
  };
  startedQueries.length = 0;
  const targetMutation = createDeferred();
  const targetMutationTask = targetMutation.promise.then(() => refreshAfterMutation({
    page: 3,
    pageSize: 10,
    channel: "production",
    includeDisabled: false,
    scopeRevision: 0
  }, "preview"));
  targetMutation.resolve();
  assertEqual(await targetMutationTask, false, "\u76EE\u6807\u901A\u9053\u53D8\u5316\u5E94\u4EA4\u7531 scope effect \u5355\u6B21\u52A0\u8F7D");
  assertEqual(desiredQuery.channel, "preview", "\u76EE\u6807\u901A\u9053\u53D8\u5316\u5E94\u540C\u6B65\u66F4\u65B0 desired query");
  assertEqual(desiredQuery.page, 1, "\u76EE\u6807\u901A\u9053\u53D8\u5316\u5FC5\u987B\u539F\u5B50\u91CD\u7F6E\u4E3A\u7B2C\u4E00\u9875");
  const targetScopeList = createDeferred();
  const targetScopeTask = load(desiredQuery, targetScopeList.promise);
  assertDeepEqual(startedQueries, ["preview:1"], "\u76EE\u6807\u901A\u9053\u53D8\u5316\u4E0D\u5F97\u518D\u989D\u5916\u8BF7\u6C42\u65E7\u9875\u7801");
  targetScopeList.resolve({ ...desiredQuery });
  await targetScopeTask;
  desiredQuery = {
    page: 3,
    pageSize: 10,
    channel: "production",
    includeDisabled: false,
    scopeRevision: 10
  };
  startedQueries.length = 0;
  const conflictingMutation = createDeferred();
  const mutationStartQuery = { ...desiredQuery };
  const conflictingMutationTask = conflictingMutation.promise.then(() => refreshAfterMutation(mutationStartQuery, "preview"));
  resetScope("beta", false);
  const betaScopeList = createDeferred();
  const betaScopeTask = load(desiredQuery, betaScopeList.promise);
  conflictingMutation.resolve();
  assertEqual(await conflictingMutationTask, false, "\u65E7 mutation \u4E0D\u5F97\u8986\u76D6\u7528\u6237\u540E\u6765\u9009\u62E9\u7684\u5176\u4ED6\u901A\u9053");
  assertEqual(desiredQuery.channel, "beta", "\u7528\u6237\u5DF2\u5207\u5230 beta \u65F6\u4E0D\u5F97\u88AB\u65E7 mutation \u5207\u56DE preview");
  assertEqual(desiredQuery.page, 1, "\u7528\u6237\u5207\u6362 beta \u540E\u4ECD\u5E94\u505C\u7559\u7B2C\u4E00\u9875");
  assertDeepEqual(startedQueries, ["beta:1"], "\u65E7 mutation \u4E0D\u5F97\u989D\u5916\u53D1\u51FA preview \u8BF7\u6C42");
  betaScopeList.resolve({ ...desiredQuery });
  await betaScopeTask;
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
await verifyWpfScopeChangeUsesOneFirstPageRequest();
var wpfVersionsPageSource = readFileSync(resolve(process.cwd(), "src/pages/System/WpfVersions/index.tsx"), "utf8");
var wpfQrActionGuardPattern = /\{record\.downloadUrl \? \(\s*<>\s*<Button[\s\S]*?href=\{record\.downloadUrl\}[\s\S]*?<Button[\s\S]*?onClick=\{\(\) => setQrRelease\(record\)\}[\s\S]*?<\/>\s*\) : null\}/;
assertTruthy(
  wpfVersionsPageSource.includes("const resetReleaseScope = useCallback((nextChannel: string, nextIncludeDisabled: boolean) =>") && wpfVersionsPageSource.includes("page: 1,") && wpfVersionsPageSource.includes("latestReleaseQueryRef.current = nextQuery") && wpfVersionsPageSource.includes("releasesRequestGuardRef.current.invalidate()") && wpfVersionsPageSource.includes("scopeRevision: currentQuery.scopeRevision + 1"),
  "WPF \u901A\u9053\u6216\u7B5B\u9009\u53D8\u66F4\u5FC5\u987B\u540C\u6B65\u53D1\u5E03\u7B2C\u4E00\u9875 desired query \u5E76\u4F7F\u65E7\u8BF7\u6C42\u5931\u6548"
);
assertTruthy(
  wpfVersionsPageSource.includes("onChange={handleChannelChange}") && wpfVersionsPageSource.includes("onChange={handleIncludeDisabledChange}") && wpfVersionsPageSource.includes("onChange: handlePageChange,") && !wpfVersionsPageSource.includes("onChange={setChannel}") && !wpfVersionsPageSource.includes("onChange={setIncludeDisabled}"),
  "WPF \u901A\u9053\u3001\u7B5B\u9009\u548C\u5206\u9875\u5FC5\u987B\u901A\u8FC7\u539F\u5B50 desired query handler \u66F4\u65B0"
);
assertTruthy(
  wpfVersionsPageSource.includes("await refreshLatestReleaseQuery(payload.channel, refreshQuery)") && wpfVersionsPageSource.includes("await refreshLatestReleaseQuery(undefined, refreshQuery)") && wpfVersionsPageSource.includes("await refreshLatestReleaseQuery(uploadedChannel, refreshQuery)"),
  "WPF mutation \u5237\u65B0\u5FC5\u987B\u643A\u5E26\u542F\u52A8\u65F6 query\uFF0C\u907F\u514D\u665A\u5B8C\u6210\u8986\u76D6\u65B0 scope"
);
assertTruthy(
  wpfVersionsPageSource.includes("expectedQuery.scopeRevision !== currentQuery.scopeRevision") && wpfVersionsPageSource.includes("if (!targetChannel || targetChannel !== currentQuery.channel) {"),
  "WPF \u65E7 mutation \u7684\u76EE\u6807\u901A\u9053\u4E0E\u5F53\u524D scope \u4E0D\u540C\u65F6\u5FC5\u987B\u76F4\u63A5\u653E\u5F03\u5237\u65B0"
);
assertTruthy(
  wpfVersionsPageSource.includes("const [qrRelease, setQrRelease] = useState<WpfAppRelease | null>(null)") && wpfVersionsPageSource.includes("onClick={() => setQrRelease(record)}") && wpfVersionsPageSource.includes("t('system.wpfVersions.viewQrCode', '\u67E5\u770B\u4E8C\u7EF4\u7801')") && wpfQrActionGuardPattern.test(wpfVersionsPageSource),
  "\u53EA\u6709\u5E26\u4E0B\u8F7D\u5730\u5740\u7684 WPF \u7248\u672C\u64CD\u4F5C\u533A\u624D\u80FD\u6253\u5F00\u5F53\u524D\u884C\u7684\u4E0B\u8F7D\u4E8C\u7EF4\u7801"
);
assertTruthy(
  wpfVersionsPageSource.includes("open={Boolean(qrRelease)}") && wpfVersionsPageSource.includes("onCancel={() => setQrRelease(null)}") && wpfVersionsPageSource.includes("<QRCode value={qrRelease.downloadUrl} size={220} />") && wpfVersionsPageSource.includes("<Text strong>{qrRelease.version}</Text>") && wpfVersionsPageSource.includes("<Text>{qrRelease.fileName}</Text>") && wpfVersionsPageSource.includes("copyable={{ text: qrRelease.downloadUrl }}") && wpfVersionsPageSource.includes("{qrRelease.downloadUrl}"),
  "WPF \u4E0B\u8F7D\u4E8C\u7EF4\u7801\u5F39\u7A97\u5FC5\u987B\u5C55\u793A\u5F53\u524D\u7248\u672C\u3001\u6587\u4EF6\u540D\u3001\u4E8C\u7EF4\u7801\u548C\u53EF\u590D\u5236\u7684\u540C\u4E00\u4E0B\u8F7D\u94FE\u63A5"
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
assertEqual(
  getNestedValue(zhLocale, "system.wpfVersions.setCurrent"),
  "\u8BBE\u4E3A\u53D1\u5E03\u76EE\u6807",
  "WPF \u4E2D\u6587\u64CD\u4F5C\u6587\u6848\u5E94\u660E\u786E\u8868\u793A\u4FEE\u6539\u53D1\u5E03\u76EE\u6807"
);
assertEqual(
  getNestedValue(enLocale, "system.wpfVersions.setCurrent"),
  "Set as Release Target",
  "WPF English action copy should identify the release target"
);
assertEqual(
  getNestedValue(zhLocale, "system.wpfVersions.setCurrentConfirm"),
  "\u5C06\u6B64\u7248\u672C\u8BBE\u4E3A\u53D1\u5E03\u76EE\u6807\uFF1F\u5BA2\u6237\u7AEF\u5C06\u5728\u4E0B\u6B21\u68C0\u67E5\u66F4\u65B0\u65F6\u83B7\u53D6\u8BE5\u7248\u672C\u3002",
  "WPF \u4E2D\u6587\u786E\u8BA4\u6587\u6848\u5E94\u8BF4\u660E\u5BA2\u6237\u7AEF\u83B7\u53D6\u7248\u672C\u7684\u65F6\u673A"
);
assertEqual(
  getNestedValue(enLocale, "system.wpfVersions.setCurrentConfirm"),
  "Set this version as the release target? Clients will receive it the next time they check for updates.",
  "WPF English confirmation copy should explain when clients receive the target"
);
assertTruthy(
  wpfVersionsPageSource.includes("t('system.wpfVersions.setCurrent', '\u8BBE\u4E3A\u53D1\u5E03\u76EE\u6807')") && wpfVersionsPageSource.includes("t('system.wpfVersions.setCurrentConfirm', '\u5C06\u6B64\u7248\u672C\u8BBE\u4E3A\u53D1\u5E03\u76EE\u6807\uFF1F\u5BA2\u6237\u7AEF\u5C06\u5728\u4E0B\u6B21\u68C0\u67E5\u66F4\u65B0\u65F6\u83B7\u53D6\u8BE5\u7248\u672C\u3002')"),
  "WPF \u53D1\u5E03\u76EE\u6807\u6309\u94AE\u7684\u9875\u9762 fallback \u6587\u6848\u5FC5\u987B\u4E0E locale \u8BED\u4E49\u4E00\u81F4"
);
console.log("WpfVersions logic.test: ok");
