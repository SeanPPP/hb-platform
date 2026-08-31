import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const nginxSource = readFileSync(resolve(repositoryRoot, "apps/web/nginx.conf"), "utf8");

function locationBlock(pattern) {
  const match = nginxSource.match(pattern);
  assert.ok(match, `缺少 Nginx location：${pattern}`);
  return match[0];
}

test("hashed assets 只命中精确文件并使用一年 immutable 缓存", () => {
  const assets = locationBlock(/location\s+\^~\s+\/assets\/\s*\{[^}]*\}/u);
  assert.match(assets, /try_files\s+\$uri\s+=404;/u);
  assert.match(assets, /expires\s+1y;/u);
  assert.match(
    assets,
    /Cache-Control\s+"public,\s*max-age=31536000,\s*immutable"\s+always;/u,
  );
  assert.doesNotMatch(nginxSource, /\.[0-9a-f]\+.*\(js\|css\)/u);
});

test("HTML、service worker 与 manifest 保持 no-store，manifest MIME 正确", () => {
  for (const pattern of [
    /location\s+=\s+\/index\.html\s*\{[^}]*\}/u,
    /location\s+=\s+\/sw\.js\s*\{[^}]*\}/u,
    /location\s+=\s+\/manifest\.webmanifest\s*\{[^}]*\}/u,
    /location\s+\/\s*\{[^}]*\}/u,
  ]) {
    assert.match(locationBlock(pattern), /Cache-Control\s+"no-store"\s+always;/u);
  }
  const manifest = locationBlock(
    /location\s+=\s+\/manifest\.webmanifest\s*\{[^}]*\}/u,
  );
  assert.match(manifest, /default_type\s+application\/manifest\+json;/u);
  assert.match(manifest, /try_files\s+\$uri\s+=404;/u);
});

test("API、Hangfire 与健康检查代理契约保持原样", () => {
  assert.match(nginxSource, /location\s+\/api\/\s*\{[\s\S]*?proxy_pass\s+http:\/\/hb-platform-api:5002;/u);
  assert.match(nginxSource, /location\s+\/hangfire\/\s*\{[\s\S]*?proxy_pass\s+http:\/\/hb-platform-api:5002;/u);
  assert.match(nginxSource, /location\s+\/health\s*\{[\s\S]*?return\s+200\s+"healthy\\n";/u);
  assert.doesNotMatch(nginxSource, /pos-api|:5003|:8888/u);
});
