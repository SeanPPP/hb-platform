import assert from "node:assert/strict";
import test from "node:test";

import {
  DEFAULT_LOCAL_HBPOS_API_BASE_URL,
  LEGACY_LOCAL_HBPOS_API_BASE_URL,
} from "../security/pos-api-addresses";

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
  assert.equal(
    resolveHbposApiUrl(DEFAULT_LOCAL_HBPOS_API_BASE_URL),
    DEFAULT_LOCAL_HBPOS_API_BASE_URL,
  );
});

test("runtime accepts the exact build-declared LAN API address over HTTP", () => {
  assert.equal(
    resolveHbposApiUrl(`${DEFAULT_LOCAL_HBPOS_API_BASE_URL}/`),
    DEFAULT_LOCAL_HBPOS_API_BASE_URL,
  );
  assert.throws(
    () => resolveHbposApiUrl("http://192.168.31.247:5159"),
    /HTTPS/i,
  );
});

test("runtime accepts both trusted local 5159 and 5003 endpoints", () => {
  assert.equal(
    resolveHbposApiUrl(`${DEFAULT_LOCAL_HBPOS_API_BASE_URL}/pos-api/`),
    `${DEFAULT_LOCAL_HBPOS_API_BASE_URL}/pos-api`,
  );
  assert.equal(
    resolveHbposApiUrl(LEGACY_LOCAL_HBPOS_API_BASE_URL),
    LEGACY_LOCAL_HBPOS_API_BASE_URL,
  );
  assert.equal(
    resolveHbposApiUrl(`${LEGACY_LOCAL_HBPOS_API_BASE_URL}/pos-api/`),
    `${LEGACY_LOCAL_HBPOS_API_BASE_URL}/pos-api`,
  );
  assert.throws(
    () => resolveHbposApiUrl("http://192.168.31.246:5004"),
    /HTTPS/i,
  );
  assert.throws(
    () => resolveHbposApiUrl("http://192.168.31.246:5158"),
    /HTTPS/i,
  );
});
