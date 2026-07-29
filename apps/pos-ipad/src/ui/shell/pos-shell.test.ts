import assert from "node:assert/strict";
import test from "node:test";

import { mapReachabilityToConnectivity } from "./network-status";
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
