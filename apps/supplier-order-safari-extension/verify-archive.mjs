import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = dirname(fileURLToPath(import.meta.url));
const archivePath = resolve(process.argv[2] || join(ROOT, 'build', 'HB Supplier Order.xcarchive'));
const appPath = join(
  archivePath,
  'Products',
  'Applications',
  'HB Supplier Order Safari.app',
);
const extensionPath = join(
  appPath,
  'PlugIns',
  'HB Supplier Order Safari Extension.appex',
);
const appInfoPath = join(appPath, 'Info.plist');
const extensionInfoPath = join(extensionPath, 'Info.plist');
const metadata = JSON.parse(
  readFileSync(join(ROOT, 'release', 'app-store-connect.json'), 'utf8'),
);

function plistValue(path, key) {
  return execFileSync('/usr/libexec/PlistBuddy', ['-c', `Print :${key}`, path], {
    encoding: 'utf8',
  }).trim();
}

assert.ok(existsSync(appInfoPath), '归档缺少 iOS App');
assert.ok(existsSync(extensionInfoPath), '归档缺少 Safari Extension');
assert.deepEqual(
  readdirSync(join(archivePath, 'Products', 'Applications')).filter((name) =>
    name.endsWith('.app'),
  ),
  ['HB Supplier Order Safari.app'],
);
assert.deepEqual(
  readdirSync(join(appPath, 'PlugIns')).filter((name) => name.endsWith('.appex')),
  ['HB Supplier Order Safari Extension.appex'],
);
assert.equal(plistValue(appInfoPath, 'CFBundleDisplayName'), 'HB Supplier Order');
assert.equal(plistValue(appInfoPath, 'CFBundleIdentifier'), metadata.bundleId);
assert.equal(plistValue(appInfoPath, 'CFBundleShortVersionString'), metadata.version);
assert.equal(plistValue(appInfoPath, 'CFBundleVersion'), String(metadata.buildNumber));
assert.equal(plistValue(appInfoPath, 'ITSAppUsesNonExemptEncryption'), 'false');
assert.equal(
  plistValue(extensionInfoPath, 'CFBundleIdentifier'),
  metadata.extensionBundleId,
);
assert.equal(plistValue(extensionInfoPath, 'CFBundleDisplayName'), 'HB Supplier Order');

execFileSync('/usr/bin/codesign', ['--verify', '--deep', '--strict', appPath]);
console.log(
  `归档验证通过: ${metadata.version} (${metadata.buildNumber})，仅包含 iOS App 与 Safari Extension`,
);
