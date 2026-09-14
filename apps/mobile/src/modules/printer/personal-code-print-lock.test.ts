import assert from "node:assert/strict";
import { test } from "node:test";
import { PersonalCodePrintBusyError, runPersonalCodePrintExclusive } from "./personal-code-print-lock";

test("个人码打印拒绝重入并在失败后释放互斥", async () => {
  let release!: () => void;
  const first = runPersonalCodePrintExclusive(() => new Promise<void>((resolve) => { release = resolve; }));
  let duplicateCalls = 0;
  await assert.rejects(runPersonalCodePrintExclusive(async () => { duplicateCalls++; }), PersonalCodePrintBusyError);
  assert.equal(duplicateCalls, 0);
  release();
  await first;
  await assert.rejects(runPersonalCodePrintExclusive(async () => { throw new Error("printer disconnected"); }));
  assert.equal(await runPersonalCodePrintExclusive(async () => "recovered"), "recovered");
});
