import assert from "node:assert/strict";
import test from "node:test";

import {
  DEFAULT_HBPOS_API_URL,
  resolveHbposApiUrl,
} from "./runtime-config";

test("runtime uses the WPF-compatible production Hbpos endpoint by default", () => {
  assert.equal(DEFAULT_HBPOS_API_URL, "https://hotbargain.vip/pos-api");
  assert.equal(resolveHbposApiUrl(""), DEFAULT_HBPOS_API_URL);
  assert.equal(resolveHbposApiUrl(undefined), DEFAULT_HBPOS_API_URL);
});

test("runtime normalizes an explicit API address without accepting credentials or query data", () => {
  assert.equal(
    resolveHbposApiUrl(" https://pos.example.test/hbpos/// "),
    "https://pos.example.test/hbpos",
  );
  assert.throws(
    () => resolveHbposApiUrl("https://user:secret@pos.example.test/pos-api"),
    /credentials/i,
  );
  assert.throws(
    () => resolveHbposApiUrl("https://pos.example.test/pos-api?token=secret"),
    /query or fragment/i,
  );
  assert.throws(() => resolveHbposApiUrl("file:///tmp/api"), /http/i);
  assert.throws(
    () => resolveHbposApiUrl("http://pos.example.test/pos-api"),
    /HTTPS/i,
  );
  assert.equal(
    resolveHbposApiUrl("http://127.0.0.1:5003/pos-api/"),
    "http://127.0.0.1:5003/pos-api",
  );
});
