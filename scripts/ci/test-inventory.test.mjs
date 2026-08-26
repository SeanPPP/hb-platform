import assert from 'node:assert/strict'
import test from 'node:test'

import { classifyTestFile, validateInventoryLanes } from './test-inventory.mjs'

test('Web 源测试与生成测试包分离', () => {
  assert.equal(classifyTestFile('web', 'apps/web/src/services/storeService.test.ts'), 'web-esbuild')
  assert.equal(classifyTestFile('web', 'apps/web/src/pages/System/AppDownloads/test-app-downloads.mjs'), 'node')
  assert.equal(classifyTestFile('web', 'apps/web/tmp/hbweb_rv_storeService.test.mjs'), 'ignored-generated')
})

test('Mobile 的类型测试不作为运行时测试执行', () => {
  assert.equal(classifyTestFile('mobile', 'apps/mobile/src/modules/auth/login-errors.test.ts'), 'tsx')
  assert.equal(classifyTestFile('mobile', 'apps/mobile/scripts/publish-ota-update.test.mjs'), 'node')
  assert.equal(classifyTestFile('mobile', 'apps/mobile/src/components/ui/EmptyState.types.test.tsx'), 'typecheck')
})

test('POS 按 Jest、TSX、Node 与 macOS native 唯一分流', () => {
  assert.equal(classifyTestFile('pos-ipad', 'apps/pos-ipad/src/ui/screen.rntl.test.tsx'), 'jest')
  assert.equal(
    classifyTestFile(
      'pos-ipad',
      'apps/pos-ipad/src/i18n/index.test.ts',
      'import { jest, test } from "@jest/globals"',
    ),
    'jest',
  )
  assert.equal(classifyTestFile('pos-ipad', 'apps/pos-ipad/src/core/runtime.test.ts'), 'tsx')
  assert.equal(classifyTestFile('pos-ipad', 'apps/pos-ipad/scripts/check-project.test.mjs'), 'node')
  assert.equal(
    classifyTestFile('pos-ipad', 'apps/pos-ipad/modules/hb-attendance-security/tests/native-interop.test.mjs'),
    'native',
  )
  assert.equal(
    classifyTestFile('pos-ipad', 'apps/pos-ipad/scripts/check-external-display-startup.test.mjs'),
    'native',
  )
})

test('未知测试后缀失败关闭', () => {
  assert.throws(
    () => classifyTestFile('mobile', 'apps/mobile/src/example.test.tsx'),
    /无法分类/,
  )
})

test('Web 与 Mobile 出现未消费的 Jest lane 时失败关闭', () => {
  for (const app of ['web', 'mobile']) {
    assert.throws(
      () => validateInventoryLanes(app, new Map([
        ['jest', [`apps/${app}/src/new.rntl.test.tsx`]],
      ])),
      /未被 CI 消费.*jest/,
    )
  }
})
