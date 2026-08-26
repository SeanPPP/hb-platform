import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'

import {
  ALL_COMPONENTS,
  PROFILE_BUDGETS,
  buildMatrices,
  changedFiles,
  selectComponents,
} from './plan.mjs'

test('profile 为端到端 15/45 分钟预算预留 plan 与聚合时间', () => {
  assert.deepEqual(PROFILE_BUDGETS, {
    pr: { budgetSeconds: 15 * 60, matrixTimeoutMinutes: 12 },
    weekly: { budgetSeconds: 45 * 60, matrixTimeoutMinutes: 40 },
  })
})

function selectedFor(files, options = {}) {
  return [...selectComponents(files, options)].sort()
}

function renamedPaths(sourcePath, targetPath) {
  const repository = mkdtempSync(join(tmpdir(), 'hb-ci-plan-rename-'))
  const git = (...args) => execFileSync('git', args, { cwd: repository, encoding: 'utf8' }).trim()
  try {
    git('init', '--quiet')
    git('config', 'user.email', 'ci@example.invalid')
    git('config', 'user.name', 'CI Test')
    mkdirSync(dirname(join(repository, sourcePath)), { recursive: true })
    writeFileSync(join(repository, sourcePath), 'export const value = 1\n')
    git('add', sourcePath)
    git('commit', '--quiet', '-m', '初始文件')
    const base = git('rev-parse', 'HEAD')

    mkdirSync(dirname(join(repository, targetPath)), { recursive: true })
    git('mv', sourcePath, targetPath)
    git('commit', '--quiet', '-m', '移动文件')
    const head = git('rev-parse', 'HEAD')
    return changedFiles(base, head, repository).sort()
  } finally {
    rmSync(repository, { recursive: true, force: true })
  }
}

test('weekly 与手动全量检查都选择完整组件集', () => {
  assert.deepEqual(
    selectedFor([], { full: true }),
    [...ALL_COMPONENTS].sort(),
  )
})

test('组件路径只选择自身及固定跨目录依赖', () => {
  assert.deepEqual(selectedFor(['services/backend/BlazorApp.Api/Program.cs']), ['backend'])
  assert.deepEqual(selectedFor(['apps/web/src/App.tsx']), ['web'])
  assert.deepEqual(selectedFor(['apps/mobile/src/App.tsx']), ['mobile'])
  assert.deepEqual(selectedFor(['apps/pos-ipad/src/app.ts']), ['pos-contract', 'pos-ipad'])
  assert.deepEqual(selectedFor(['apps/pos-handheld/src/app.ts']), ['pos-contract', 'pos-handheld'])
  assert.deepEqual(
    selectedFor(['apps/pos-wpf/src/Hbpos.Api/Program.cs']),
    ['pos-contract', 'pos-wpf'],
  )
  assert.deepEqual(
    selectedFor(['apps/supplier-order-extension/src/background.js']),
    ['supplier-extension', 'supplier-safari'],
  )
  assert.deepEqual(
    selectedFor(['apps/supplier-order-safari-extension/HB Supplier Order/AppDelegate.swift']),
    ['supplier-safari'],
  )
  assert.deepEqual(selectedFor(['apps/antpos-web/index.html']), ['antpos-web'])
})

test('BlazorApp.Shared 任意路径触发 backend、POS WPF 与契约检查', () => {
  assert.deepEqual(
    selectedFor(['services/backend/BlazorApp.Shared/Models/HBweb/Product.cs']),
    ['backend', 'pos-contract', 'pos-wpf'],
  )
})

test('共享 fixture 与浏览器扩展契约文件触发所有消费者', () => {
  assert.deepEqual(
    selectedFor(['test-fixtures/shared-held-orders/example.json']),
    ['pos-contract', 'pos-ipad', 'pos-wpf'],
  )
  assert.deepEqual(
    selectedFor([
      'apps/web/src/components/SupplierOrderingExtensionEntry/supplierOrderingExtensionLogic.ts',
    ]),
    ['supplier-extension', 'supplier-safari', 'web'],
  )
  assert.deepEqual(
    selectedFor(['services/backend/BlazorApp.Shared/DTOs/BrowserExtensionDtos.cs']),
    ['backend', 'pos-contract', 'pos-wpf', 'supplier-extension', 'supplier-safari'],
  )
})

test('CI 基础文件触发全量，纯文档不分配组件', () => {
  assert.deepEqual(
    selectedFor(['.github/workflows/ci.yml']),
    [...ALL_COMPONENTS].sort(),
  )
  assert.deepEqual(selectedFor(['docs/ci.md', 'README.md']), [])
})

test('未知非文档路径 fail-safe 到全量', () => {
  assert.deepEqual(
    selectedFor(['tools/new-runner/config.toml']),
    [...ALL_COMPONENTS].sort(),
  )
})

test('跨组件 rename 同时选择源与目标组件', () => {
  const paths = renamedPaths('apps/web/src/value.ts', 'apps/mobile/src/value.ts')
  assert.deepEqual(paths, ['apps/mobile/src/value.ts', 'apps/web/src/value.ts'])
  assert.deepEqual(selectedFor(paths), ['mobile', 'web'])
})

test('组件文件 rename 到文档目录仍检查源组件', () => {
  const paths = renamedPaths('apps/web/src/value.ts', 'docs/value.ts')
  assert.deepEqual(paths, ['apps/web/src/value.ts', 'docs/value.ts'])
  assert.deepEqual(selectedFor(paths), ['web'])
})

test('真实 PR 入口默认从仓库根目录读取 diff', () => {
  assert.deepEqual(changedFiles('HEAD', 'HEAD'), [])
})

test('各 runner 矩阵始终非空，未选择时使用 noop sentinel', () => {
  const empty = buildMatrices(new Set())
  for (const matrix of Object.values(empty)) {
    assert.deepEqual(matrix, {
      include: [{ component: 'noop', runner: 'ubuntu-24.04', timeout: 15 }],
    })
  }

  const selected = buildMatrices(
    new Set(['web', 'backend', 'pos-wpf', 'pos-contract', 'supplier-safari']),
  )
  assert.deepEqual(selected.linuxNode, {
    include: [{ component: 'web', runner: 'ubuntu-24.04', timeout: 15 }],
  })
  assert.deepEqual(selected.linuxDotnet, {
    include: [
      { component: 'backend', runner: 'ubuntu-24.04', timeout: 15 },
      { component: 'pos-api', runner: 'ubuntu-24.04', timeout: 15 },
      { component: 'pos-contract', runner: 'ubuntu-24.04', timeout: 15 },
    ],
  })
  assert.deepEqual(selected.windows, {
    include: [{ component: 'pos-wpf', runner: 'windows-2025', timeout: 15 }],
  })
  assert.deepEqual(selected.macos, {
    include: [{ component: 'supplier-safari', runner: 'macos-26', timeout: 15 }],
  })
  assert.deepEqual(selected.android, {
    include: [{ component: 'noop', runner: 'ubuntu-24.04', timeout: 15 }],
  })

  const weekly = buildMatrices(new Set(['web']), { timeout: 45 })
  assert.deepEqual(weekly.linuxNode, {
    include: [{ component: 'web', runner: 'ubuntu-24.04', timeout: 45 }],
  })
})
