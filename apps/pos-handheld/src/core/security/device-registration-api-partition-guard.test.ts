import assert from "node:assert/strict";
import test from "node:test";

import { DeviceRegistrationApiPartitionGuard } from "./device-registration-api-partition-guard";

test("开通或重置在途时拒绝服务器切换且不运行保存闭包", async () => {
  const subject = new DeviceRegistrationApiPartitionGuard();
  const lease = subject.beginMutation();
  let operationCalls = 0;

  assert.deepEqual(
    await subject.runSwitch(async () => {
      operationCalls += 1;
      return "saved";
    }),
    { blocked: true },
  );
  assert.equal(operationCalls, 0);

  lease.release();
  assert.deepEqual(
    await subject.runSwitch(async () => "saved"),
    { blocked: false, value: "saved" },
  );
});

test("服务器切换闭包在途时拒绝启动开通或重置请求", async () => {
  const subject = new DeviceRegistrationApiPartitionGuard();
  let finishSwitch: (() => void) | undefined;
  const switching = subject.runSwitch(
    () => new Promise<void>((resolve) => {
      finishSwitch = resolve;
    }),
  );

  assert.throws(
    () => subject.beginMutation(),
    /DEVICE_REGISTRATION_API_PARTITION_SWITCH_ACTIVE/,
  );
  finishSwitch?.();
  assert.deepEqual(await switching, { blocked: false, value: undefined });
});
