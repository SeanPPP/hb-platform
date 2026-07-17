import assert from "node:assert/strict";
import { createAttendanceQrScanSessionGate } from "./attendance-qr-scan-session";

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((next) => { resolve = next; });
  return { promise, resolve };
}

async function main() {
  const cancelledGate = createAttendanceQrScanSessionGate();
  const cancelledSession = cancelledGate.begin();
  const gps = deferred();
  let cancelledSubmitCount = 0;
  const cancelledFlow = (async () => {
    await gps.promise;
    if (!cancelledGate.isActive(cancelledSession)) return;
    if (cancelledGate.tryStartSubmitting(cancelledSession)) cancelledSubmitCount += 1;
  })();
  cancelledGate.invalidate();
  gps.resolve();
  await cancelledFlow;
  assert.equal(cancelledSubmitCount, 0, "GPS 或权限等待期间关闭后不得提交");

  const onceGate = createAttendanceQrScanSessionGate();
  const onceSession = onceGate.begin();
  assert.equal(onceGate.tryStartSubmitting(onceSession), true);
  assert.equal(onceGate.isSubmitting(onceSession), true,
    "resolve 开始前必须同步锁定当前扫码会话");
  assert.equal(onceGate.tryStartSubmitting(onceSession), false,
    "同一会话即使收到多次扫码事件也只能进入一次 resolve");

  const resolveRequest = deferred();
  let resolveCount = 1;
  let punchCount = 0;
  const firstScan = (async () => {
    await resolveRequest.promise;
    if (onceGate.isActive(onceSession) && onceGate.isSubmitting(onceSession)) {
      punchCount += 1;
    }
  })();
  if (onceGate.tryStartSubmitting(onceSession)) resolveCount += 1;
  assert.equal(resolveCount, 1, "resolve 等待期间重复扫码不得发起第二个 resolve");
  assert.equal(punchCount, 0, "resolve 完成前不得调用 punch");
  resolveRequest.resolve();
  await firstScan;
  assert.equal(punchCount, 1, "同一 gate 内 resolve 完成后只能进入一次 punch");

  onceGate.finishSubmitting(onceSession);
  assert.equal(onceGate.isSubmitting(onceSession), false,
    "当前有效会话失败后必须释放提交锁");
  onceGate.finishSubmitting(onceSession);
  assert.equal(onceGate.isSubmitting(onceSession), false, "重复释放必须安全");

  const retrySession = onceGate.begin();
  assert.equal(onceGate.tryStartSubmitting(retrySession), true,
    "显式重试创建的新会话可以再次提交");

  const staleSession = retrySession;
  onceGate.invalidate();
  const currentSession = onceGate.begin();
  assert.equal(onceGate.tryStartSubmitting(currentSession), true);
  onceGate.finishSubmitting(staleSession);
  assert.equal(onceGate.isSubmitting(currentSession), true,
    "旧会话的 finally 不得释放新会话的提交锁");
  onceGate.finishSubmitting(currentSession);
  assert.equal(onceGate.isSubmitting(currentSession), false);

  onceGate.invalidate();
  const nextSession = onceGate.begin();
  assert.equal(onceGate.isActive(onceSession), false);
  assert.equal(onceGate.tryStartSubmitting(nextSession), true,
    "关闭后显式重新扫描创建的新会话可以提交");

  console.log("attendance-qr-scan-session.test.ts: ok");
}

void main();
