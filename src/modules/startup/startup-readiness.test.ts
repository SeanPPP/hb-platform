import assert from "node:assert/strict";
import { waitForStartupReadiness } from "./startup-readiness";

async function run() {
  const success = await waitForStartupReadiness([Promise.resolve()], 0);
  assert.deepEqual(success, { ok: true });

  const error = new Error("hydrate failed");
  const failure = await waitForStartupReadiness([Promise.reject(error)], 0);
  assert.equal(failure.ok, false);
  assert.equal(failure.ok === false ? failure.error : undefined, error);
}

void run();
