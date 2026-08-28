import assert from "node:assert/strict";
import test from "node:test";

import { resolveTrustedProductImageUri } from "./trusted-product-image-uri";

const httpsApiBaseUrl = "https://api.example.test/v1/";

test("相对图片路径按 API base 解析并保留端口语义", () => {
  assert.equal(
    resolveTrustedProductImageUri(
      "  ../media/products/sku-1.png  ",
      "https://api.example.test:8443/v1/catalog/",
    ),
    "https://api.example.test:8443/v1/media/products/sku-1.png",
  );
});

test("允许同源 HTTP(S) 与外部 HTTPS 图片", () => {
  assert.equal(
    resolveTrustedProductImageUri(
      "https://api.example.test/media/sku-1.png",
      httpsApiBaseUrl,
    ),
    "https://api.example.test/media/sku-1.png",
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "https://cdn.example.test/products/sku-1.png",
      httpsApiBaseUrl,
    ),
    "https://cdn.example.test/products/sku-1.png",
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "http://api.example.test:8080/media/sku-1.png",
      "http://api.example.test:8080/v1/",
    ),
    "http://api.example.test:8080/media/sku-1.png",
  );
});

test("拒绝非同源的外部 HTTP 图片", () => {
  assert.equal(
    resolveTrustedProductImageUri(
      "http://cdn.example.test/products/sku-1.png",
      httpsApiBaseUrl,
    ),
    null,
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "http://api.example.test:8081/media/sku-1.png",
      "http://api.example.test:8080/v1/",
    ),
    null,
  );
});

test("允许 localhost、127.0.0.1 与 IPv6 loopback 的 HTTP 开发图片", () => {
  assert.equal(
    resolveTrustedProductImageUri(
      "http://localhost:19000/assets/sku-1.png",
      httpsApiBaseUrl,
    ),
    "http://localhost:19000/assets/sku-1.png",
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "http://127.0.0.1:19000/assets/sku-1.png",
      httpsApiBaseUrl,
    ),
    "http://127.0.0.1:19000/assets/sku-1.png",
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "http://[::1]:19000/assets/sku-1.png",
      httpsApiBaseUrl,
    ),
    "http://[::1]:19000/assets/sku-1.png",
  );
});

test("拒绝图片或 API base 中的 URL 凭据", () => {
  assert.equal(
    resolveTrustedProductImageUri(
      "https://user:password@cdn.example.test/sku-1.png",
      httpsApiBaseUrl,
    ),
    null,
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "/media/sku-1.png",
      "https://user:password@api.example.test/v1/",
    ),
    null,
  );
});

test("拒绝非 HTTP(S) 协议", () => {
  for (const image of [
    "data:image/png;base64,AAAA",
    "file:///tmp/sku-1.png",
    "javascript:alert(1)",
  ]) {
    assert.equal(resolveTrustedProductImageUri(image, httpsApiBaseUrl), null);
  }

  assert.equal(
    resolveTrustedProductImageUri("/media/sku-1.png", "file:///tmp/api/"),
    null,
  );
});

test("拒绝控制字符、超长地址及空值", () => {
  assert.equal(
    resolveTrustedProductImageUri(
      "https://cdn.example.test/sku-\n1.png",
      httpsApiBaseUrl,
    ),
    null,
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "\thttps://cdn.example.test/sku-1.png",
      httpsApiBaseUrl,
    ),
    null,
  );
  assert.equal(
    resolveTrustedProductImageUri(
      `https://cdn.example.test/${"a".repeat(2_049)}`,
      httpsApiBaseUrl,
    ),
    null,
  );
  assert.equal(resolveTrustedProductImageUri("   ", httpsApiBaseUrl), null);
  assert.equal(resolveTrustedProductImageUri(null, httpsApiBaseUrl), null);
  assert.equal(resolveTrustedProductImageUri(undefined, httpsApiBaseUrl), null);
});

test("无效 image 或 API base 输入返回 null", () => {
  assert.equal(
    resolveTrustedProductImageUri("https://[invalid", httpsApiBaseUrl),
    null,
  );
  assert.equal(
    resolveTrustedProductImageUri("/media/sku-1.png", "not-a-url"),
    null,
  );
  assert.equal(
    resolveTrustedProductImageUri(
      "/media/sku-1.png",
      "https://api.example.test/\nignored",
    ),
    null,
  );
  assert.equal(
    resolveTrustedProductImageUri(
      123 as unknown as string,
      httpsApiBaseUrl,
    ),
    null,
  );
});
