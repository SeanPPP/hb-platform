import assert from "node:assert/strict";
import {
  activateCashierPrintSecureSession,
  clearCashierPrintSecureSession,
  queueCashierPrintSecureWrite,
  resetCashierPrintSecureSessionForTests,
} from "./cashier-print-secure-session";

async function main() {
  resetCashierPrintSecureSessionForTests();
  let value: string | null = null;
  let releaseWrite!: () => void;
  const writeGate = new Promise<void>((resolve) => { releaseWrite = resolve; });
  const storage = {
    getCashierBarcodePrintPending: async () => value,
    setCashierBarcodePrintPending: async (next: string) => { await writeGate; value = next; },
    removeCashierBarcodePrintPending: async () => { value = null; },
  };

  const first = activateCashierPrintSecureSession("user-a");
  const oldWrite = queueCashierPrintSecureWrite(storage, first, "old");
  await Promise.resolve();
  const clearing = clearCashierPrintSecureSession(storage);
  releaseWrite();
  assert.equal(await oldWrite, true, "已经进入安全存储I/O的write由后续串行delete覆盖");
  await clearing;
  assert.equal(value, null, "logout delete必须最终获胜");

  assert.equal(await queueCashierPrintSecureWrite(storage, first, "late-old"), false,
    "logout后旧任务新发write必须拒绝");
  const second = activateCashierPrintSecureSession("user-b");
  assert.equal(await queueCashierPrintSecureWrite(storage, first, "old-overwrite"), false);
  assert.equal(await queueCashierPrintSecureWrite(storage, second, "new-user"), true);
  assert.equal(value, "new-user", "新账号pending不得被旧账号覆盖");

  console.log("cashier-print-secure-session.test.ts: ok");
}

void main();
