import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import test from "node:test";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const moduleSource = readFileSync(
  path.resolve(
    testDirectory,
    "../../../android/app/src/main/java/com/hbweb/expo/HbPrinterModule.kt"
  ),
  "utf8"
);

test("Android 未配对打印机先等待系统配对结果，再允许 RFCOMM 连接", () => {
  assert.match(moduleSource, /fun pair\(address: String, promise: Promise\)/);
  assert.match(moduleSource, /BluetoothDevice\.ACTION_BOND_STATE_CHANGED/);
  assert.match(moduleSource, /device\.createBond\(\)/);
  assert.match(moduleSource, /BluetoothDevice\.BOND_BONDED/);
  assert.match(moduleSource, /PRINTER_PAIRING_START_FAILED/);
  assert.match(moduleSource, /PRINTER_PAIRING_REJECTED/);
  assert.match(moduleSource, /PRINTER_PAIRING_TIMEOUT/);

  const connectStart = moduleSource.indexOf("fun connect(address: String, promise: Promise)");
  const disconnectStart = moduleSource.indexOf("fun disconnect(promise: Promise)", connectStart);
  const connectSource = moduleSource.slice(connectStart, disconnectStart);
  assert.match(connectSource, /bondState\s*!=\s*BluetoothDevice\.BOND_BONDED/);
  assert.match(connectSource, /PRINTER_PAIRING_REQUIRED/);
});
