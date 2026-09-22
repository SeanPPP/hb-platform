import assert from "node:assert/strict";
import test from "node:test";

import { orderPrinterDevices } from "./device-list";
import type { PrinterDevice } from "./types";

test("蓝牙扫描列表将已配对设备稳定排在未配对设备之前", () => {
  const devices: PrinterDevice[] = [
    { name: "XP-P326B-75A3", address: "D0:23:81:3F:75:A3", bonded: false, connected: false },
    { name: "XP-P326B-75A3", address: "10:23:81:3F:75:A3", bonded: true, connected: true },
    { name: "XP-365B", address: "30:00:00:00:00:01", bonded: true, connected: false },
    { name: "Receipt", address: "40:00:00:00:00:01", bonded: false, connected: false },
  ];

  const ordered = orderPrinterDevices(devices);

  assert.deepEqual(
    ordered.map((device) => device.address),
    ["10:23:81:3F:75:A3", "30:00:00:00:00:01", "D0:23:81:3F:75:A3", "40:00:00:00:00:01"]
  );
  assert.deepEqual(
    devices.map((device) => device.address),
    ["D0:23:81:3F:75:A3", "10:23:81:3F:75:A3", "30:00:00:00:00:01", "40:00:00:00:00:01"],
    "排序不得修改原始扫描结果"
  );
});
