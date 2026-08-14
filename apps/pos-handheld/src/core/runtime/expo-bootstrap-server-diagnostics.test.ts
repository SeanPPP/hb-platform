import assert from "node:assert/strict";
import test from "node:test";

import { createBootstrapServerDiagnostics } from "./bootstrap-server-diagnostics";

test("准备页显示已解析的持久地址并只探测精确 health 路径", async () => {
  const calls: Readonly<{ url: string; signal: AbortSignal }>[] = [];
  const abort = new AbortController();
  const subject = createBootstrapServerDiagnostics({
    currentApiBaseUrl: "https://hotbargain.top/pos-api",
    trustedApiOrigins: [
      "https://hotbargain.vip",
      "https://hotbargain.top",
    ],
    probe: async (url, signal) => {
      calls.push({ url, signal });
      return true;
    },
  });

  assert.equal(
    subject.currentApiBaseUrl,
    "https://hotbargain.top/pos-api",
  );
  assert.equal(
    await subject.test(
      "https://hotbargain.vip/pos-api/",
      abort.signal,
    ),
    true,
  );
  assert.deepEqual(calls, [
    {
      url: "https://hotbargain.vip/pos-api/api/v1/health",
      signal: abort.signal,
    },
  ]);
});

test("准备页在发起网络请求前拒绝白名单外地址", async () => {
  let probeCount = 0;
  const subject = createBootstrapServerDiagnostics({
    currentApiBaseUrl: "https://hotbargain.vip/pos-api",
    trustedApiOrigins: ["https://hotbargain.vip"],
    probe: async () => {
      probeCount += 1;
      return true;
    },
  });

  assert.throws(
    () => subject.test(
      "https://evil.example.test/pos-api",
      new AbortController().signal,
    ),
    /trusted build allowlist/u,
  );
  assert.equal(probeCount, 0);
});
