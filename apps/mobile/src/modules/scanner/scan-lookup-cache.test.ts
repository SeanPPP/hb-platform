import assert from "node:assert/strict";
import { createScanLookupInFlight, runScanLookupInFlight } from "./scan-lookup-cache";

async function run() {
  const inFlight = createScanLookupInFlight<string>();
  let lookupCount = 0;
  let resolveLookup: ((value: string) => void) | undefined;

  const first = runScanLookupInFlight(inFlight, "S001", "8058617603635", () => {
    lookupCount += 1;
    return new Promise<string>((resolve) => {
      resolveLookup = resolve;
    });
  });
  const second = runScanLookupInFlight(inFlight, " S001 ", "8058617603635", async () => {
    lookupCount += 1;
    return "unexpected";
  });

  assert.equal(lookupCount, 1, "同一门店同一条码的并发冷扫码只应执行一次 factory");
  resolveLookup?.("P-SHARED");
  assert.deepEqual(await first, { result: "P-SHARED", shared: false });
  assert.deepEqual(await second, { result: "P-SHARED", shared: true });

  const otherStore = await runScanLookupInFlight(inFlight, "S002", "8058617603635", async () => {
    lookupCount += 1;
    return "P-OTHER-STORE";
  });
  assert.equal(otherStore.shared, false, "不同门店同条码不能共享 in-flight");
  assert.equal(lookupCount, 2);

  let rejectCount = 0;
  await assert.rejects(
    runScanLookupInFlight(inFlight, "S001", "ERR-BARCODE", async () => {
      rejectCount += 1;
      throw new Error("lookup failed");
    }),
    /lookup failed/,
    "factory reject 应原样抛出"
  );
  const retry = await runScanLookupInFlight(inFlight, "S001", "ERR-BARCODE", async () => {
    rejectCount += 1;
    return "P-RETRY";
  });
  assert.deepEqual(retry, { result: "P-RETRY", shared: false });
  assert.equal(rejectCount, 2, "reject 后必须清理 in-flight，后续扫码才能重试");

  console.log("scan-lookup-cache.test.ts: ok");
}

void run();
