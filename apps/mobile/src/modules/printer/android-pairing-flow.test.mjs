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

test("Android 扫描结果同时暴露真实蓝牙传输类型和系统设备类别", () => {
  assert.match(moduleSource, /BluetoothDevice\.DEVICE_TYPE_CLASSIC\s*->\s*"classic"/);
  assert.match(moduleSource, /BluetoothDevice\.DEVICE_TYPE_LE\s*->\s*"ble"/);
  assert.match(moduleSource, /BluetoothDevice\.DEVICE_TYPE_DUAL\s*->\s*"dual"/);
  assert.match(moduleSource, /else\s*->\s*"unknown"/);
  assert.match(moduleSource, /device\.bluetoothClass\?\.deviceClass/);
  assert.match(moduleSource, /map\.putString\("transport", printer\.transport\)/);
  assert.match(moduleSource, /map\.putInt\("deviceClass", printer\.deviceClass\)/);

  const scanStart = moduleSource.indexOf("fun scanPrinters(durationMs: Int, promise: Promise)");
  const connectStart = moduleSource.indexOf("fun connect(address: String, promise: Promise)", scanStart);
  const scanSource = moduleSource.slice(scanStart, connectStart);
  assert.equal((scanSource.match(/transport\s*=\s*bluetoothTransport\(device\)/g) ?? []).length, 2);
  assert.equal((scanSource.match(/deviceClass\s*=\s*device\.bluetoothClass\?\.deviceClass/g) ?? []).length, 2);
});

test("Android 在配对或替换现有连接前拒绝 BLE-only 地址", () => {
  assert.match(moduleSource, /BluetoothDevice\.DEVICE_TYPE_LE/);
  assert.match(moduleSource, /PRINTER_BLE_UNSUPPORTED/);

  const pairStart = moduleSource.indexOf("fun pair(address: String, promise: Promise)");
  const addListenerStart = moduleSource.indexOf("fun addListener(eventName: String)", pairStart);
  const pairSource = moduleSource.slice(pairStart, addListenerStart);
  const pairGuard = pairSource.indexOf("rejectBleOnlyDevice(device, promise)");
  assert.ok(pairGuard >= 0, "pair 应检查 BLE-only 设备");
  assert.ok(pairGuard < pairSource.indexOf("device.createBond()"), "BLE 检查必须早于 createBond");

  const connectStart = moduleSource.indexOf("fun connect(address: String, promise: Promise)");
  const disconnectStart = moduleSource.indexOf("fun disconnect(promise: Promise)", connectStart);
  const connectSource = moduleSource.slice(connectStart, disconnectStart);
  const connectGuard = connectSource.indexOf("rejectBleOnlyDevice(device, promise)");
  assert.ok(connectGuard >= 0, "connect 应检查 BLE-only 设备");
  assert.ok(connectGuard < connectSource.indexOf("beginConnectionAttempt()"), "BLE 检查必须早于断开原会话");
  assert.ok(
    connectGuard < connectSource.indexOf("device.createRfcommSocketToServiceRecord"),
    "BLE 检查必须早于创建 RFCOMM socket"
  );
});
