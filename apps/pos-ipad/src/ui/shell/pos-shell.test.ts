import assert from "node:assert/strict";
import test from "node:test";

import {
  mapReachabilityToConnectivity,
  resolveBackendAwareConnectivity,
} from "./network-status";
import { usePosShellStore } from "./pos-shell-store";

test("network state distinguishes known offline from unresolved reachability", () => {
  assert.equal(
    mapReachabilityToConnectivity({
      isConnected: false,
      isInternetReachable: null,
    }),
    "offline",
  );
  assert.equal(
    mapReachabilityToConnectivity({
      isConnected: true,
      isInternetReachable: null,
    }),
    "online",
  );
  assert.equal(mapReachabilityToConnectivity({}), "checking");
});

test("backend-aware connectivity: 设备在线但后端停止时判定为离线", () => {
  // 后端停止（health 探测失败）：设备在线也应显示离线。
  assert.equal(resolveBackendAwareConnectivity("online", false), "offline");
  // 后端可达：在线。
  assert.equal(resolveBackendAwareConnectivity("online", true), "online");
  // 尚未探测：保持乐观在线，避免启动闪烁。
  assert.equal(resolveBackendAwareConnectivity("online", null), "online");
});

test("backend-aware connectivity: 设备断网时恒为离线，不受后端探测影响", () => {
  assert.equal(resolveBackendAwareConnectivity("offline", true), "offline");
  assert.equal(resolveBackendAwareConnectivity("offline", null), "offline");
  assert.equal(resolveBackendAwareConnectivity("checking", false), "checking");
  assert.equal(resolveBackendAwareConnectivity("checking", true), "checking");
});

test("shell store rejects invalid pending sync counts", () => {
  usePosShellStore.getState().reset();
  usePosShellStore.getState().setPendingSyncCount(4);
  assert.equal(usePosShellStore.getState().pendingSyncCount, 4);
  assert.throws(
    () => usePosShellStore.getState().setPendingSyncCount(-1),
    /non-negative safe integer/,
  );
  usePosShellStore.getState().reset();
});

test("shell store keeps the public terminal presentation and reset clears it", () => {
  usePosShellStore.getState().reset();
  assert.equal(usePosShellStore.getState().terminalPresentation, null);

  usePosShellStore.getState().setTerminalPresentation({
    storeName: "Brisbane CBD",
    deviceCode: "IPAD-07",
  });
  assert.deepEqual(usePosShellStore.getState().terminalPresentation, {
    storeName: "Brisbane CBD",
    deviceCode: "IPAD-07",
  });

  usePosShellStore.getState().reset();
  assert.equal(usePosShellStore.getState().terminalPresentation, null);
});
