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

test("已连接局域网但无公网时仍允许探测 POS 后端", () => {
  assert.equal(
    mapReachabilityToConnectivity({
      isConnected: true,
      isInternetReachable: false,
    }),
    "online",
  );
});

test("backend-aware connectivity: 设备在线但后端停止时判定为离线", () => {
  // 后端停止（health 探测失败）：设备在线也应显示离线。
  assert.equal(resolveBackendAwareConnectivity("online", false), "offline");
  // 后端可达：在线。
  assert.equal(resolveBackendAwareConnectivity("online", true), "online");
  // 尚未探测：不能仅凭系统网络状态乐观宣称后端在线。
  assert.equal(resolveBackendAwareConnectivity("online", null), "checking");
});

test("backend-aware connectivity: 后端实测可达优先于系统网络误报", () => {
  assert.equal(resolveBackendAwareConnectivity("offline", true), "online");
  assert.equal(resolveBackendAwareConnectivity("offline", null), "offline");
  assert.equal(resolveBackendAwareConnectivity("checking", false), "offline");
  assert.equal(resolveBackendAwareConnectivity("checking", true), "online");
});

test("shell store starts in checking and validates ready pending sync counts", () => {
  usePosShellStore.getState().reset();
  assert.deepEqual(usePosShellStore.getState().pendingSync, {
    kind: "checking",
  });
  usePosShellStore.getState().setPendingSync({ kind: "ready", count: 4 });
  assert.deepEqual(usePosShellStore.getState().pendingSync, {
    kind: "ready",
    count: 4,
  });
  assert.throws(
    () =>
      usePosShellStore.getState().setPendingSync({
        kind: "ready",
        count: -1,
      }),
    /non-negative safe integer/,
  );
  usePosShellStore.getState().setPendingSync({ kind: "unavailable" });
  assert.deepEqual(usePosShellStore.getState().pendingSync, {
    kind: "unavailable",
  });
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
