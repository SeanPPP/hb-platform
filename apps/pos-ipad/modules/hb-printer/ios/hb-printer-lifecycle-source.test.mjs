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

console.log("HbPrinterModule Promise 生命周期源码约束验证通过。");
