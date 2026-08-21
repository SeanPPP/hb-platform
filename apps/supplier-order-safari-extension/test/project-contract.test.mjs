import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { synchronizeXcodeProjectVersions } from '../project-version.mjs';

const read = (path) => readFileSync(path, 'utf8');
const readJson = (path) => JSON.parse(read(path));

test('iOS Safari 项目版本、bundle ID 与部署目标保持一致', () => {
  const pkg = readJson('package.json');
  const sharedPkg = readJson('../supplier-order-extension/package.json');
  const project = read('xcode/HB Supplier Order Safari/HB Supplier Order Safari.xcodeproj/project.pbxproj');
  const viewController = read('xcode/HB Supplier Order Safari/HB Supplier Order Safari/ViewController.swift');

  assert.equal(pkg.version, sharedPkg.version);
  assert.equal(project.match(new RegExp(`MARKETING_VERSION = ${pkg.version.replaceAll('.', '\\.')};`, 'g'))?.length, 4);
  assert.equal(project.match(new RegExp(`CURRENT_PROJECT_VERSION = ${pkg.version.replaceAll('.', '\\.')};`, 'g'))?.length, 4);
  assert.ok(!project.includes('MACOSX_DEPLOYMENT_TARGET'));
  assert.ok(!project.includes('SDKROOT = macosx'));
  assert.ok(!project.includes('IPHONEOS_DEPLOYMENT_TARGET = 15.0'));
  assert.ok(!project.includes('IPHONEOS_DEPLOYMENT_TARGET = 26.5'));
  assert.ok(project.includes('IPHONEOS_DEPLOYMENT_TARGET = 16.4'));
  assert.equal(project.match(/TARGETED_DEVICE_FAMILY = "1,2";/g)?.length, 4);
  assert.equal(
    project.match(/PRODUCT_BUNDLE_IDENTIFIER = com\.hotbargain\.supplierorder\.safari;/g)?.length,
    2,
  );
  assert.equal(
    project.match(/PRODUCT_BUNDLE_IDENTIFIER = com\.hotbargain\.supplierorder\.safari\.Extension;/g)?.length,
    2,
  );
  assert.ok(viewController.includes('import UIKit'));
  assert.ok(!viewController.includes('import Cocoa'));
});

test('宿主启用说明页按系统语言提供中英文资源', () => {
  const project = read('xcode/HB Supplier Order Safari/HB Supplier Order Safari.xcodeproj/project.pbxproj');
  const basePath = 'xcode/HB Supplier Order Safari/HB Supplier Order Safari/Resources/Base.lproj/Main.html';
  const simplifiedPath = 'xcode/HB Supplier Order Safari/HB Supplier Order Safari/Resources/zh-Hans.lproj/Main.html';
  const traditionalPath = 'xcode/HB Supplier Order Safari/HB Supplier Order Safari/Resources/zh-Hant.lproj/Main.html';

  assert.ok(existsSync(simplifiedPath), '必须提供简体中文宿主说明页');
  assert.ok(existsSync(traditionalPath), '必须提供繁体中文宿主说明页');

  const base = read(basePath);
  const simplified = read(simplifiedPath);
  const traditional = read(traditionalPath);

  assert.ok(base.includes('Enable HB Supplier Order'));
  assert.ok(!base.includes('请在'));
  assert.ok(simplified.includes('请在“设置 → Safari → 扩展”中启用'));
  assert.equal(traditional, simplified);
  assert.ok(project.includes('"zh-Hans"'));
  assert.ok(project.includes('"zh-Hant"'));
  assert.ok(project.includes('zh-Hans.lproj/Main.html'));
  assert.ok(project.includes('zh-Hant.lproj/Main.html'));
});

test('资源同步会把 manifest 版本写入全部 Xcode 构建配置', () => {
  const source = [
    'MARKETING_VERSION = 1.2.0;',
    'CURRENT_PROJECT_VERSION = 7;',
    'MARKETING_VERSION = 1.2.0;',
    'CURRENT_PROJECT_VERSION = 7;',
  ].join('\n');

  const updated = synchronizeXcodeProjectVersions(source, '1.3.4');

  assert.equal(updated.match(/MARKETING_VERSION = 1\.3\.4;/g)?.length, 2);
  assert.equal(updated.match(/CURRENT_PROJECT_VERSION = 1\.3\.4;/g)?.length, 2);
  assert.throws(
    () => synchronizeXcodeProjectVersions('MARKETING_VERSION = 1.0.0;', '1.3.4'),
    /CURRENT_PROJECT_VERSION/,
  );
  assert.throws(() => synchronizeXcodeProjectVersions(source, '1.3.4-beta'), /版本格式/);
});

test('Xcode Resources 是最新 Safari 构建快照', () => {
  const builtManifest = readJson('../supplier-order-extension/dist/safari/manifest.json');
  const xcodeManifest = readJson(
    'xcode/HB Supplier Order Safari/HB Supplier Order Safari Extension/Resources/manifest.json',
  );
  const worker = read(
    'xcode/HB Supplier Order Safari/HB Supplier Order Safari Extension/Resources/background/service-worker.js',
  );
  const listContent = read(
    'xcode/HB Supplier Order Safari/HB Supplier Order Safari Extension/Resources/content/list.js',
  );
  const shopBridge = read(
    'xcode/HB Supplier Order Safari/HB Supplier Order Safari Extension/Resources/content/shop-bridge.js',
  );

  assert.deepEqual(xcodeManifest, builtManifest);
  assert.equal(xcodeManifest.browser_specific_settings.safari.strict_min_version, '16.4');
  assert.equal(xcodeManifest.options_ui.page, 'sidepanel/sidepanel.html');
  assert.ok(!('side_panel' in xcodeManifest));
  assert.ok(!xcodeManifest.permissions.includes('sidePanel'));
  assert.ok(!('web_accessible_resources' in xcodeManifest));
  assert.ok(!/^\s*import\s/m.test(worker), 'Safari service worker 必须是 classic bundle');
  assert.ok(!/\bimport\s*\(/.test(listContent), 'Safari 列表内容脚本必须内联模块依赖');
  assert.ok(!/\bimport\s*\(/.test(shopBridge), 'Safari /shop 桥接必须内联模块依赖');
  assert.ok(!listContent.includes('chrome.runtime.getURL('));
  assert.ok(!shopBridge.includes('chrome.runtime.getURL('));
});

test('项目提供统一 Run 入口与 Codex 动作', () => {
  const pkg = readJson('package.json');
  const runScript = read('script/build_and_run.sh');
  const environment = read('.codex/environments/environment.toml');

  assert.equal(pkg.scripts.pretest, 'npm run build:resources');
  assert.ok(pkg.scripts['build:xcode'].includes('platform=iOS Simulator'));
  assert.ok(!pkg.scripts['build:xcode'].includes('platform=macOS'));
  assert.ok(runScript.includes('npm run build:resources'));
  assert.ok(runScript.includes('xcodebuild'));
  assert.ok(runScript.includes('--verify|verify'));
  assert.ok(runScript.includes('wait_for_app'));
  assert.ok(!runScript.includes('sleep 1'));
  assert.ok(environment.includes('command = "./script/build_and_run.sh"'));
});
