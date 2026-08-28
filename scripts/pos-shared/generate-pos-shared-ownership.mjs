import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const baseline = "ddcca999b9d6a90e07b7b3d88c5b93a28d0c7b13";
const scriptRoot = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptRoot, "..", "..");
const manifestPath = join(scriptRoot, "pos-shared-ownership.json");
const allowlistPath = join(scriptRoot, "pos-app-local-allowlist.json");

const apiPaths = new Set([
  "core/api/index.ts",
  "generated/hbpos/schema.d.ts",
  "features/attendance-audit/hbpos-attendance-security-api.ts",
  "features/attendance-audit/hbpos-operation-audit-read-api.ts",
  "features/installments/hbpos-installments-api.ts",
  "features/remote-history/remote-history-api.ts",
  "features/shared-held-orders/shared-held-order-network-api.ts",
  "features/special-products/hbpos-special-products-api.ts",
]);

const receiptPaths = new Set([
  "features/local-history/receipt-code128.ts",
  "features/local-history/receipt-qr-matrix.ts",
]);

const explicitAppLocalPaths = new Map([
  [
    "core/contracts/device-reregistration.ts",
    "设备重注册合同属于第 13 项设备恢复，本轮保持 App-local",
  ],
  [
    "features/app-updates/update-transition-lease-coordinator.ts",
    "OTA 切换互斥状态机属于第 11 项，本轮保持 App-local",
  ],
  ["core/observability/sentry-config.ts", "Sentry 配置属于 App 运行时观测边界，本轮保持 App-local"],
  ["core/performance/business-startup-clock.ts", "启动性能计时绑定 App 生命周期，本轮保持 App-local"],
  ["core/performance/business-startup-origin.ts", "启动性能来源绑定 App 生命周期，本轮保持 App-local"],
  ["core/performance/business-timings.test.ts", "性能测试验证 App 级计时组合，本轮保持 App-local"],
  ["core/performance/client-metrics.ts", "客户端指标绑定 App 运行时上传，本轮保持 App-local"],
  ["core/performance/payment-performance.ts", "支付性能采样绑定 App 运行时，本轮保持 App-local"],
  ["core/runtime/local-device-state.test.ts", "本地设备状态测试属于 App 运行时边界，本轮保持 App-local"],
  ["core/runtime/local-device-state.ts", "本地设备状态属于 App 运行时边界，本轮保持 App-local"],
  ["core/security/cashier-authentication.ts", "收银员认证绑定 App 安全运行时，本轮保持 App-local"],
  ["core/security/device-activation-code.test.ts", "开通码测试属于第 13 项设备恢复，本轮保持 App-local"],
  ["core/security/device-activation-code.ts", "开通码属于第 13 项设备恢复，本轮保持 App-local"],
  ["core/security/device-registration-api-partition-guard.test.ts", "设备注册 API 分区测试属于 App 安全边界，本轮保持 App-local"],
  ["core/security/device-registration-api-partition-guard.ts", "设备注册 API 分区门禁属于 App 安全边界，本轮保持 App-local"],
  ["features/sales/runtime/scan-timing.test.ts", "扫码性能测试绑定 App 输入运行时，本轮保持 App-local"],
  ["features/sales/runtime/scan-timing.ts", "扫码性能采样绑定 App 输入运行时，本轮保持 App-local"],
  ["ui/scanner/scanner-route-bridge.tsx", "扫描路由桥接属于 App UI 与导航边界，本轮保持 App-local"],
]);

function listTree(root) {
  const output = execFileSync(
    "git",
    ["ls-tree", "-r", "--full-tree", baseline, "--", root],
    { cwd: repositoryRoot, encoding: "utf8" },
  );
  const prefix = `${root}/`;
  return new Map(
    output
      .trim()
      .split("\n")
      .filter(Boolean)
      .map((line) => {
        const [metadata, fullPath] = line.split("\t");
        const [, , blob] = metadata.split(" ");
        return [fullPath.slice(prefix.length), blob];
      }),
  );
}

function isTestPath(path) {
  return /(^|\/)(__tests__|__mocks__|fixtures)(\/|$)|\.(test|spec)\.[cm]?[jt]sx?$/.test(
    path,
  );
}

function classifyProduction(path) {
  if (
    path.endsWith(".tsx") ||
    path === "app-providers.tsx" ||
    path.startsWith("core/runtime/") ||
    path.startsWith("core/peripherals/") ||
    path === "core/db/expo-sqlite-driver.ts" ||
    path === "core/db/expo-keychain-database-key-provider.ts" ||
    path === "core/db/index.ts" ||
    path === "core/security/expo-installation-id.ts" ||
    path === "core/security/expo-secure-store.ts" ||
    path === "core/security/index.ts" ||
    path === "features/app-updates/expo-app-update-restart.ts" ||
    path.startsWith("features/payments/ui/") ||
    path.startsWith("features/sales/ui/") ||
    path.startsWith("i18n/") ||
    path.startsWith("ui/")
  ) {
    return {
      owner: "app-local",
      reason: "绑定 App 生命周期、React/Expo、原生适配器、平台 UI 或本地组合根",
    };
  }

  if (apiPaths.has(path)) {
    return {
      owner: "pos-api-client",
      reason: "公共 Hbpos API endpoint adapter 或 API 客户端出口",
    };
  }
  if (path.startsWith("core/db/")) {
    return {
      owner: "pos-db",
      reason: "driver-neutral SQLite 抽象、事务、迁移或 repository 实现",
    };
  }
  if (path.startsWith("core/sync/") || path.startsWith("features/sync-history/")) {
    return {
      owner: "pos-sync",
      reason: "同步协调、审计上传、重试或同步历史公共逻辑",
    };
  }
  if (path.startsWith("features/payments/")) {
    return {
      owner: "pos-payments-core",
      reason: "provider-neutral 支付状态、恢复、幂等或现金混合支付逻辑",
    };
  }
  if (path.startsWith("features/receipts/") || receiptPaths.has(path)) {
    return {
      owner: "pos-receipt-core",
      reason: "小票文档、编码、条码、渲染或重打准备逻辑",
    };
  }
  return {
    owner: "pos-domain",
    reason: "无平台直接依赖的 POS 合同、状态机、校验或应用领域逻辑",
  };
}

function classifyTest(path) {
  if (
    path.endsWith(".tsx") ||
    path.includes(".rntl.test.") ||
    path.startsWith("core/runtime/") ||
    path.startsWith("core/peripherals/") ||
    path.startsWith("core/security/") ||
    path.startsWith("features/app-updates/") ||
    path.startsWith("features/payments/ui/") ||
    path.startsWith("tests/routes/") ||
    path.startsWith("i18n/") ||
    path.startsWith("ui/")
  ) {
    return {
      owner: "app-local",
      reason: "验证 App 生命周期、React Native UI、Expo 或原生适配器行为",
    };
  }
  if (path.startsWith("core/db/") || path.includes("sqlite")) {
    return {
      owner: "pos-db",
      reason: "随 driver-neutral SQLite 或 repository 模块迁移的测试",
    };
  }
  if (path.startsWith("core/sync/") || path.startsWith("features/sync-history/")) {
    return {
      owner: "pos-sync",
      reason: "随同步协调、重试或同步历史模块迁移的测试",
    };
  }
  if (path.startsWith("features/payments/")) {
    return {
      owner: "pos-payments-core",
      reason: "随 provider-neutral 支付核心迁移的测试",
    };
  }
  if (path.startsWith("features/receipts/") || path.includes("receipt-code128")) {
    return {
      owner: "pos-receipt-core",
      reason: "随小票编码、渲染或重打准备模块迁移的测试",
    };
  }
  if (
    /(^|\/)(hbpos-[^/]+-api|remote-history-api|shared-held-order-network-api)\.(test|spec)\./.test(
      path,
    )
  ) {
    return {
      owner: "pos-api-client",
      reason: "共享 endpoint adapter 或 transport 契约测试",
    };
  }
  return {
    owner: "pos-domain",
    reason: "随 POS 合同、状态机、校验或应用领域模块迁移的测试",
  };
}

const ipad = listTree("apps/pos-ipad/src");
const handheld = listTree("apps/pos-handheld/src");
const files = [...ipad.entries()]
  .filter(([path, blob]) => handheld.get(path) === blob)
  .sort(([left], [right]) => left.localeCompare(right))
  .map(([path, blob]) => {
    const kind = isTestPath(path) ? "test" : "production";
    const explicitReason = explicitAppLocalPaths.get(path);
    const classification = explicitReason
      ? { owner: "app-local", reason: explicitReason }
      : kind === "test"
        ? classifyTest(path)
        : classifyProduction(path);
    return { path, blob, kind, ...classification };
  });

const productionByOwner = Object.fromEntries(
  [
    "app-local",
    "pos-api-client",
    "pos-db",
    "pos-domain",
    "pos-payments-core",
    "pos-receipt-core",
    "pos-sync",
  ].map((owner) => [
    owner,
    files.filter(({ kind, owner: fileOwner }) =>
      kind === "production" && fileOwner === owner
    ).length,
  ]),
);

const manifest = {
  schemaVersion: 1,
  baseline,
  generatedFrom: ["apps/pos-ipad/src", "apps/pos-handheld/src"],
  summary: {
    identical: files.length,
    production: files.filter(({ kind }) => kind === "production").length,
    tests: files.filter(({ kind }) => kind === "test").length,
    productionByOwner,
  },
  files,
};
const allowlist = {
  schemaVersion: 1,
  baseline,
  files: files
    .filter(({ owner }) => owner === "app-local")
    .map(({ path, reason }) => ({ path, reason })),
};

function serialized(value) {
  return `${JSON.stringify(value, null, 2)}\n`;
}

if (process.argv.includes("--check")) {
  const failures = [];
  for (const [path, expected] of [
    [manifestPath, manifest],
    [allowlistPath, allowlist],
  ]) {
    let actual = "";
    try {
      actual = readFileSync(path, "utf8");
    } catch {
      failures.push(`${path} 不存在`);
      continue;
    }
    if (actual !== serialized(expected)) failures.push(`${path} 已漂移`);
  }
  if (failures.length > 0) {
    throw new Error(failures.join("\n"));
  }
} else {
  writeFileSync(manifestPath, serialized(manifest));
  writeFileSync(allowlistPath, serialized(allowlist));
}
