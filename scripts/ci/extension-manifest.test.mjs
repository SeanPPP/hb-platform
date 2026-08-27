import assert from 'node:assert/strict'
import test from 'node:test'

import { validateBuiltManifest } from './extension-manifest.mjs'

const validManifest = {
  manifest_version: 3,
  name: 'HB Supplier Order',
  version: '1.2.0',
  permissions: ['storage', 'scripting'],
  host_permissions: ['https://hotbargain.vip/*'],
  background: { service_worker: 'background/service-worker.js' },
  content_scripts: [{ js: ['content/shop-bridge.js'] }],
  action: { default_icon: { 16: 'icons/icon16.png' } },
  icons: { 16: 'icons/icon16.png' },
  browser_specific_settings: { safari: { strict_min_version: '16.4' } },
}

test('已构建 manifest 的版本、权限与引用文件完整时通过', () => {
  assert.deepEqual(
    validateBuiltManifest({
      manifest: validManifest,
      manifestText: JSON.stringify(validManifest),
      target: 'safari',
      expectedVersion: '1.2.0',
      files: new Set([
        'background/service-worker.js',
        'content/shop-bridge.js',
        'icons/icon16.png',
      ]),
    }),
    [],
  )
})

test('版本漂移、占位符和缺失引用文件全部失败关闭', () => {
  const errors = validateBuiltManifest({
    manifest: { ...validManifest, version: '0.0.0' },
    manifestText: `${JSON.stringify(validManifest)} __API_ORIGIN__`,
    target: 'chrome',
    expectedVersion: '1.2.0',
    files: new Set(),
  })
  assert.ok(errors.some((error) => error.includes('版本')))
  assert.ok(errors.some((error) => error.includes('占位符')))
  assert.ok(errors.some((error) => error.includes('引用文件不存在')))
})
