export type CashierPrintSecureStorageAdapter = {
  getCashierBarcodePrintPending(): Promise<string | null>;
  setCashierBarcodePrintPending(value: string): Promise<void>;
  removeCashierBarcodePrintPending(): Promise<void>;
};

export type CashierPrintSecureSession = {
  generation: number;
  userIdentity: string;
};

let generation = 0;
let currentIdentity: string | null = null;
let storageQueue: Promise<void> = Promise.resolve();

function enqueue<T>(task: () => Promise<T>) {
  const result = storageQueue.then(task, task);
  storageQueue = result.then(() => undefined, () => undefined);
  return result;
}

function isCurrent(session: CashierPrintSecureSession) {
  return session.generation === generation && session.userIdentity === currentIdentity;
}

export function activateCashierPrintSecureSession(userIdentity: string): CashierPrintSecureSession {
  const normalizedIdentity = userIdentity.trim();
  if (currentIdentity !== normalizedIdentity) {
    generation += 1;
    currentIdentity = normalizedIdentity;
  }
  return { generation, userIdentity: normalizedIdentity };
}

export function queueCashierPrintSecureRead(
  storage: CashierPrintSecureStorageAdapter,
  session: CashierPrintSecureSession
) {
  return enqueue(async () => isCurrent(session)
    ? storage.getCashierBarcodePrintPending()
    : null);
}

export function queueCashierPrintSecureWrite(
  storage: CashierPrintSecureStorageAdapter,
  session: CashierPrintSecureSession,
  value: string | null
) {
  return enqueue(async () => {
    // 真正执行 I/O 时再次核对 generation 与账号，过期任务禁止覆盖新会话。
    if (!isCurrent(session)) {
      return false;
    }
    if (value === null) {
      await storage.removeCashierBarcodePrintPending();
    } else {
      await storage.setCashierBarcodePrintPending(value);
    }
    return true;
  });
}

export function clearCashierPrintSecureSession(storage: CashierPrintSecureStorageAdapter) {
  // 函数调用同步阶段立即失效所有已捕获任务，随后把删除排入同一串行队列。
  generation += 1;
  currentIdentity = null;
  return enqueue(() => storage.removeCashierBarcodePrintPending());
}

export function resetCashierPrintSecureSessionForTests() {
  generation = 0;
  currentIdentity = null;
  storageQueue = Promise.resolve();
}
