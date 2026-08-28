import assert from "node:assert/strict";
import { createHidScannerModeState } from "./hid-scanner-mode-state";

async function main() {
  const modeState = createHidScannerModeState();
  const observedValues: boolean[] = [];
  modeState.subscribe((forceTextInput) => {
    observedValues.push(forceTextInput);
  });

  let resolvePersistedRead!: () => void;
  const persistedRead = new Promise<void>((resolve) => {
    resolvePersistedRead = resolve;
  });
  const applyStalePersistedValue = (async () => {
    await persistedRead;
    modeState.setIfUnset(false);
  })();

  modeState.set(true);
  resolvePersistedRead();
  await applyStalePersistedValue;

  assert.equal(modeState.get(), true, "旧的 persisted false 不得覆盖运行时较新的 true");
  assert.deepEqual(observedValues, [true], "旧 persisted 读取不应向订阅者广播回切 native");

  console.log("hid-scanner-mode-state-race.test.ts: ok");
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
