import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const sourcePath = resolve(fileURLToPath(new URL(".", import.meta.url)), "HbPrinterModule.swift");
const source = readFileSync(sourcePath, "utf8");

// 原生 XCTest target 尚未建立；这里锁定 Promise 生命周期的不变量，避免维护时回退为悬挂连接请求。
assert.match(
  source,
  /let pendingConnect = connectPromise[\s\S]*?connectPromise = nil[\s\S]*?connectTimeout\?\.cancel\(\)[\s\S]*?connectTimeout = nil/,
);
assert.match(
  source,
  /if let pendingConnect \{[\s\S]*?pendingConnect\.reject\(PrinterException\(connectFailureCode, connectFailureMessage\)\)/,
);
assert.match(
  source,
  /finishPendingOperation\(state: "unknown", message: operationMessage\)/,
);
assert.match(
  source,
  /OnDestroy \{[\s\S]*?cancelScan\([\s\S]*?disconnectInternal\([\s\S]*?PRINTER_MODULE_DESTROYED/,
);
assert.match(
  source,
  /handlePeripheralDisconnect[\s\S]*?disconnectInternal\([\s\S]*?PRINTER_CONNECT_INTERRUPTED/,
);
// writeWithoutResponse 只能证明字节已交给 CoreBluetooth 队列，不能证明打印机已经执行。
// 只有逐块收到 didWriteValueFor ACK 的 withResponse 路径才能报告 completed。
assert.match(
  source,
  /if writeType == \.withoutResponse \{[\s\S]*?finishPendingOperation\([\s\S]*?state: "unknown",[\s\S]*?return[\s\S]*?\}[\s\S]*?finishPendingOperation\(state: "completed",/,
);

// N160 的广播名固定为 printer001：只允许 trim 后的大小写不敏感精确匹配，
// 同时保留原有 Xprinter 品牌规则，不能把其他 BLE 设备误列为小票打印机。
const printerMatcherStart = source.indexOf("  private func isXprinter(_ name: String) -> Bool {");
const printerMatcherEnd = source.indexOf("\n  private func encode", printerMatcherStart);
assert.notEqual(printerMatcherStart, -1);
assert.notEqual(printerMatcherEnd, -1);
const printerMatcherSource = source.slice(printerMatcherStart, printerMatcherEnd);

assert.match(
  printerMatcherSource,
  /name\s*\.trimmingCharacters\(in:\s*\.whitespacesAndNewlines\)\s*\.lowercased\(\)/,
);
assert.match(printerMatcherSource, /normalized == "printer001"/);
assert.doesNotMatch(printerMatcherSource, /normalized\.contains\("printer001"\)/);
assert.match(printerMatcherSource, /normalized\.contains\("xprinter"\)/);
assert.match(printerMatcherSource, /normalized\.contains\("x-printer"\)/);
assert.match(printerMatcherSource, /normalized\.contains\("芯烨"\)/);

// 广告名比 CBPeripheral 的缓存名称更接近本次扫描；两者应分别保留为匹配候选。
// 这样广告名为 printer001、缓存名称不匹配时，仍能按广告名展示并识别为芯烨打印机。
const discoveryHandlerStart = source.indexOf("  fileprivate func handlePeripheralDiscovery(");
const discoveryHandlerEnd = source.indexOf(
  "\n  fileprivate func handlePeripheralConnected",
  discoveryHandlerStart,
);
assert.notEqual(discoveryHandlerStart, -1);
assert.notEqual(discoveryHandlerEnd, -1);
const discoveryHandlerSource = source.slice(discoveryHandlerStart, discoveryHandlerEnd);

assert.match(
  discoveryHandlerSource,
  /let advertisementName = advertisementData\[CBAdvertisementDataLocalNameKey\] as\? String/,
  "应独立读取广告名，不能因 peripheral.name 有缓存值而跳过广告名",
);
assert.match(discoveryHandlerSource, /let peripheralName = peripheral\.name/);
assert.match(
  discoveryHandlerSource,
  /let candidateNames = \[advertisementName, peripheralName\][\s\S]*?compactMap[\s\S]*?trimmingCharacters\(in:\s*\.whitespacesAndNewlines\)[\s\S]*?filter \{ !\$0\.isEmpty \}/,
);
assert.match(discoveryHandlerSource, /let name = candidateNames\.first \?\? "Bluetooth Printer"/);
assert.match(
  discoveryHandlerSource,
  /"isXprinter": candidateNames\.contains\(where: self\.isXprinter\)/,
);
assert.match(discoveryHandlerSource, /let id = peripheral\.identifier\.uuidString/);
assert.doesNotMatch(discoveryHandlerSource, /\.connect\(/);

const discoveryScenarioCandidates = [" Printer001 ", "Cached BLE Device"]
  .map((name) => name.trim())
  .filter(Boolean);
assert.equal(discoveryScenarioCandidates[0], "Printer001");
assert.equal(
  discoveryScenarioCandidates.some((name) => {
    const normalized = name.trim().toLowerCase();
    return (
      normalized === "printer001" ||
      normalized.includes("xprinter") ||
      normalized.includes("x-printer") ||
      normalized.includes("芯烨")
    );
  }),
  true,
);

// 扫描 Promise 必须用稳定 code 区分关机、拒绝、系统限制、等待授权和其他不可用状态。
// 关机是用户当前最直接可修复的状态，即使授权尚未完成或已拒绝，也必须优先提示开启蓝牙。
const readinessHelperStart = source.indexOf(
  "  private func bluetoothReadinessFailure(",
);
const readinessHelperEnd = source.indexOf(
  "\n  private func isXprinter",
  readinessHelperStart,
);
assert.notEqual(readinessHelperStart, -1);
assert.notEqual(readinessHelperEnd, -1);
const readinessHelperSource = source.slice(readinessHelperStart, readinessHelperEnd);

const poweredOffIndex = readinessHelperSource.indexOf("centralManager?.state == .poweredOff");
const authorizationIndex = readinessHelperSource.indexOf("CBCentralManager.authorization");
assert.notEqual(poweredOffIndex, -1, "必须先检查蓝牙是否关闭");
assert.notEqual(authorizationIndex, -1, "必须检查 CoreBluetooth 授权状态");
assert.ok(poweredOffIndex < authorizationIndex, "蓝牙关闭必须优先于授权状态返回");
assert.match(
  readinessHelperSource,
  /centralManager\?\.state == \.poweredOff[\s\S]*?PRINTER_BLUETOOTH_POWERED_OFF/,
);
assert.match(
  readinessHelperSource,
  /case \.denied:[\s\S]*?PRINTER_BLUETOOTH_PERMISSION_REQUIRED/,
);
assert.equal(
  readinessHelperSource.match(/PRINTER_BLUETOOTH_PERMISSION_REQUIRED/g)?.length,
  1,
  "只有 authorization.denied 才能返回权限拒绝码",
);
assert.match(
  readinessHelperSource,
  /case \.restricted:[\s\S]*?PRINTER_BLUETOOTH_RESTRICTED[\s\S]*?系统限制[\s\S]*?无法在设置中解除/,
);
assert.match(
  readinessHelperSource,
  /case \.notDetermined:[\s\S]*?PRINTER_BLUETOOTH_AUTHORIZATION_PENDING[\s\S]*?完成系统授权弹窗后重试/,
);
const notDeterminedCase = readinessHelperSource.match(
  /case \.notDetermined:([\s\S]*?)(?=\n\s*case )/,
)?.[1];
assert.ok(notDeterminedCase, "必须单独处理尚未决定的授权状态");
assert.doesNotMatch(notDeterminedCase, /设置/, "等待系统授权时不得误导用户前往设置");
assert.match(
  readinessHelperSource,
  /case \.allowedAlways:[\s\S]*?break[\s\S]*?switch centralManager\.state/,
);
assert.match(
  readinessHelperSource,
  /case \.unknown, \.resetting, \.unsupported, \.unauthorized, \.poweredOn:[\s\S]*?PRINTER_BLUETOOTH_UNAVAILABLE/,
);
assert.match(
  source,
  /handleCentralManagerStateChange[\s\S]*?bluetoothReadinessFailure\(central\)[\s\S]*?failScan\(code: failure\.code, message: failure\.message\)/,
);
assert.match(
  source,
  /ensureBluetoothReady[\s\S]*?bluetoothReadinessFailure\(centralManager\)[\s\S]*?promise\.reject\(PrinterException\(failure\.code, failure\.message\)\)/,
);

console.log("HbPrinterModule Promise 生命周期源码约束验证通过。");
