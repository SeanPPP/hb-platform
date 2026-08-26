import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import {
  chmodSync,
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const workflowDirectoryPath = new URL('../../.github/workflows/', import.meta.url)
const workflowPath = new URL('../../.github/workflows/pr-ci.yml', import.meta.url)
const wpfWorkflowPath = new URL('../../.github/workflows/wpf-inno-smoke-build.yml', import.meta.url)
const nodeRunnerPath = new URL('./run-node-component.sh', import.meta.url)
const macosRunnerPath = new URL('./run-macos-component.sh', import.meta.url)
const testAllPath = new URL('../test-all.sh', import.meta.url)
const mobilePackagePath = new URL('../../apps/mobile/package.json', import.meta.url)
const posPricingPerformanceTestPaths = [
  new URL('../../apps/pos-ipad/src/features/sales/domain/pricing-cart.test.ts', import.meta.url),
  new URL('../../apps/pos-handheld/src/features/sales/domain/pricing-cart.test.ts', import.meta.url),
]
const fixedXcodePath = '/Applications/Xcode_26.5.app/Contents/Developer'
const fixedXcodeAvailable = existsSync(join(fixedXcodePath, 'usr/bin/xcodebuild'))

const expectedLanes = [
  'node:web',
  'node:mobile',
  'node:pos-ipad',
  'node:pos-handheld',
  'node:supplier-extension',
  'node:antpos-web',
  'dotnet:backend',
  'dotnet:pos-api',
  'dotnet:pos-contract',
  'windows:pos-wpf',
  'macos:pos-ipad-native',
  'macos:pos-handheld-native',
  'macos:supplier-safari',
  'android:pos-handheld-android',
]

function collectWorkflowUses() {
  return readdirSync(workflowDirectoryPath, { withFileTypes: true })
    .filter((entry) => entry.isFile() && /\.ya?ml$/i.test(entry.name))
    .sort((left, right) => left.name.localeCompare(right.name))
    .flatMap((entry) => {
      const source = readFileSync(new URL(entry.name, workflowDirectoryPath), 'utf8')
      return source.split(/\r?\n/).flatMap((line, index) => {
        const match = line.match(/^\s*(?:-\s*)?uses:\s*(['"]?)([^'"\s#]+)\1(?:\s+#.*)?\s*$/)
        return match
          ? [{ action: match[2], file: entry.name, line: index + 1, sourceLine: line }]
          : []
      })
    })
}

function writeExecutable(path, source) {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, source)
  chmodSync(path, 0o755)
}

function runMacosRunner({ xcodeVersion = '26.5', useValidDeveloperDir = true } = {}) {
  const temporaryRoot = mkdtempSync(join(tmpdir(), 'hb-macos-runner-'))
  const fakeBin = join(temporaryRoot, 'bin')
  const environmentDeveloperDir = join(temporaryRoot, 'EnvironmentXcode.app/Contents/Developer')
  const selectedDeveloperDir = join(temporaryRoot, 'SelectedXcode.app/Contents/Developer')
  const invalidDeveloperDir = join(temporaryRoot, 'InvalidXcode.app/Contents/Developer')
  const xcodeVersionScript = `#!/usr/bin/env bash\nprintf '%s\\n' 'Xcode ${xcodeVersion}'\nprintf '%s\\n' 'Build version TEST'\n`

  writeExecutable(join(environmentDeveloperDir, 'usr/bin/xcodebuild'), xcodeVersionScript)
  writeExecutable(join(selectedDeveloperDir, 'usr/bin/xcodebuild'), xcodeVersionScript)
  mkdirSync(invalidDeveloperDir, { recursive: true })
  writeExecutable(join(fakeBin, 'xcodebuild'), xcodeVersionScript)
  writeExecutable(join(fakeBin, 'xcode-select'), '#!/usr/bin/env bash\nprintf \'%s\\n\' "$FAKE_XCODE_SELECT_PATH"\n')
  writeExecutable(join(fakeBin, 'sudo'), '#!/usr/bin/env bash\necho "不应调用 sudo" >&2\nexit 97\n')
  for (const command of ['npm', 'node', 'git']) {
    writeExecutable(join(fakeBin, command), '#!/usr/bin/env bash\nexit 0\n')
  }

  let result
  try {
    result = spawnSync('bash', [fileURLToPath(macosRunnerPath), 'supplier-safari'], {
      encoding: 'utf8',
      env: {
        ...process.env,
        DEVELOPER_DIR: useValidDeveloperDir ? environmentDeveloperDir : invalidDeveloperDir,
        FAKE_XCODE_SELECT_PATH: selectedDeveloperDir,
        PATH: `${fakeBin}:${process.env.PATH ?? '/usr/bin:/bin'}`,
      },
      timeout: 10_000,
    })
  } finally {
    rmSync(temporaryRoot, { recursive: true, force: true })
  }

  return { environmentDeveloperDir, result, selectedDeveloperDir }
}

function runTestAll({ kernelName, osName = '' }) {
  const temporaryRoot = mkdtempSync(join(tmpdir(), 'hb-test-all-'))
  const fakeBin = join(temporaryRoot, 'bin')
  const copiedTestAllPath = join(temporaryRoot, 'scripts/test-all.sh')

  mkdirSync(dirname(copiedTestAllPath), { recursive: true })
  copyFileSync(testAllPath, copiedTestAllPath)
  chmodSync(copiedTestAllPath, 0o755)
  for (const runner of [
    'run-node-component.sh',
    'run-dotnet-component.sh',
    'run-macos-component.sh',
    'run-android-component.sh',
  ]) {
    writeExecutable(join(temporaryRoot, 'scripts/ci', runner), '#!/usr/bin/env bash\nexit 0\n')
  }
  writeExecutable(join(fakeBin, 'pwsh'), '#!/usr/bin/env bash\nexit 0\n')
  writeExecutable(join(fakeBin, 'uname'), '#!/usr/bin/env bash\nprintf \'%s\\n\' "$FAKE_UNAME"\n')

  let result
  try {
    result = spawnSync('bash', [copiedTestAllPath], {
      encoding: 'utf8',
      env: {
        ...process.env,
        FAKE_UNAME: kernelName,
        OS: osName,
        PATH: `${fakeBin}:${process.env.PATH ?? '/usr/bin:/bin'}`,
      },
      timeout: 10_000,
    })
  } finally {
    rmSync(temporaryRoot, { recursive: true, force: true })
  }

  return result
}

test('PR workflow 每个 PR 都启动，并按 Brisbane 周日 02:23 周更全量', () => {
  const source = readFileSync(workflowPath, 'utf8')
  assert.match(source, /pull_request:/)
  assert.match(source, /cron:\s*['"]23 16 \* \* 6['"]/) // UTC Saturday 16:23
  assert.doesNotMatch(source, /^\s{2}paths:/m)
  assert.doesNotMatch(source, /nightly/i)
})

test('PR/weekly 使用 15/45 分钟端到端预算并为稳定 gate 预留时间', () => {
  const source = readFileSync(workflowPath, 'utf8')
  assert.match(source, /timeout-minutes:\s*\$\{\{ matrix\.timeout \}\}/)
  assert.match(source, /started_at_epoch:\s*\$\{\{ steps\.budget\.outputs\.started_at_epoch \}\}/)
  assert.match(source, /budget_seconds:\s*\$\{\{ steps\.plan\.outputs\.budget_seconds \}\}/)
  assert.match(source, /CI_RUN_STARTED_AT_EPOCH:\s*\$\{\{ needs\.plan\.outputs\.started_at_epoch \}\}/g)
  assert.match(source, /CI_RUN_BUDGET_SECONDS:\s*\$\{\{ needs\.plan\.outputs\.budget_seconds \}\}/g)
  assert.match(source, /timeout-minutes:\s*2/)
  assert.match(source, /timeout-minutes:\s*40/g)
  assert.match(source, /name:\s*PR CI \/ required/)
  assert.match(source, /node scripts\/ci\/required-gate\.mjs/)
  assert.match(source, /name:\s*Weekly full \/ required/)
  assert.match(source, /weekly_required:[\s\S]*?needs:\s*\n\s*- plan\s*\n/)
})

test('所有 workflow yml/yaml 的第三方 uses（含条件步骤）全部固定 40 位提交 SHA', () => {
  const uses = collectWorkflowUses()
  assert.ok(uses.length >= 20)
  assert.ok(uses.some(({ sourceLine }) => !/^\s*-\s*uses:/.test(sourceLine)), '必须覆盖条件步骤中的 uses')

  for (const { action, file, line } of uses) {
    if (action.startsWith('./') || action.startsWith('docker://')) {
      continue
    }
    assert.match(action, /^[^@]+@[0-9a-f]{40}$/, `${file}:${line} 未固定为 40 位提交 SHA`)
  }
})

test('WPF workflow 与 PR CI 使用相同的 checkout/setup-dotnet/upload-artifact SHA', () => {
  const source = readFileSync(wpfWorkflowPath, 'utf8')
  const expectedActions = [
    ['actions/checkout', '11d5960a326750d5838078e36cf38b85af677262', 1],
    ['actions/setup-dotnet', '67a3573c9a986a3f9c594539f4ab511d57bb3ce9', 1],
    ['actions/upload-artifact', 'ea165f8d65b6e75b540449e92b4886f43607fa02', 2],
  ]

  for (const [action, sha, count] of expectedActions) {
    const references = [...source.matchAll(new RegExp(`^\\s*uses:\\s*${action.replace('/', '\\/')}@([^\\s]+)$`, 'gm'))]
      .map((match) => match[1])
    assert.equal(references.length, count, `${action} 引用数量不符`)
    assert.deepEqual([...new Set(references)], [sha], `${action} 必须复用 PR CI 的固定 SHA`)
  }
})

test('macOS runner 按固定路径、DEVELOPER_DIR、xcode-select 顺序选择并只导出 Xcode 26.5 目录', () => {
  const source = readFileSync(macosRunnerPath, 'utf8')
  const fixedPathIndex = source.indexOf(fixedXcodePath)
  const developerDirIndex = source.indexOf('${DEVELOPER_DIR:-}', fixedPathIndex + 1)
  const xcodeSelectIndex = source.indexOf('xcode-select -p', developerDirIndex + 1)

  assert.ok(fixedPathIndex >= 0, '缺少固定 Xcode 26.5 路径')
  assert.ok(developerDirIndex > fixedPathIndex, '固定路径之后必须检查现有 DEVELOPER_DIR')
  assert.ok(xcodeSelectIndex > developerDirIndex, 'DEVELOPER_DIR 之后必须回退到 xcode-select -p')
  assert.deepEqual(
    [...source.matchAll(/^\s*export\s+([A-Za-z_][A-Za-z0-9_]*)=/gm)].map((match) => match[1]),
    ['DEVELOPER_DIR'],
  )
  assert.match(source, /xcodebuild -version/)
  assert.match(source, /26\.5/)
  assert.doesNotMatch(source, /\bsudo\b/)
  assert.doesNotMatch(source, /xcode-select\s+(?:-s|--switch)\b/)
})

test('macOS runner 在固定路径不可用时优先有效 DEVELOPER_DIR，再回退 xcode-select -p', {
  skip: fixedXcodeAvailable ? '本机固定 Xcode 26.5 路径存在，无法隔离验证后备顺序' : false,
}, () => {
  const fromEnvironment = runMacosRunner({ useValidDeveloperDir: true })
  assert.equal(fromEnvironment.result.error, undefined)
  assert.equal(fromEnvironment.result.status, 0, fromEnvironment.result.stderr)
  assert.match(fromEnvironment.result.stdout, new RegExp(`使用 Xcode 开发目录: ${fromEnvironment.environmentDeveloperDir}`))

  const fromXcodeSelect = runMacosRunner({ useValidDeveloperDir: false })
  assert.equal(fromXcodeSelect.result.error, undefined)
  assert.equal(fromXcodeSelect.result.status, 0, fromXcodeSelect.result.stderr)
  assert.match(fromXcodeSelect.result.stdout, new RegExp(`使用 Xcode 开发目录: ${fromXcodeSelect.selectedDeveloperDir}`))
})

test('macOS runner 拒绝非 Xcode 26.5', {
  skip: fixedXcodeAvailable ? '本机固定 Xcode 26.5 路径存在，无法注入错误版本' : false,
}, () => {
  const { result } = runMacosRunner({ xcodeVersion: '26.4' })
  assert.equal(result.error, undefined)
  assert.notEqual(result.status, 0)
  assert.match(`${result.stdout}\n${result.stderr}`, /要求 Xcode 26\.5/)
})

test('test-all 在 Linux、macOS、Windows 都逐 lane 打印 executed/skipped 与原因', () => {
  const scenarios = [
    {
      name: 'Linux',
      kernelName: 'Linux',
      statuses: {
        'android:pos-handheld-android': 'executed',
        'windows:pos-wpf': 'skipped',
        'macos:pos-ipad-native': 'skipped',
        'macos:pos-handheld-native': 'skipped',
        'macos:supplier-safari': 'skipped',
      },
    },
    {
      name: 'macOS',
      kernelName: 'Darwin',
      statuses: {
        'android:pos-handheld-android': 'skipped',
        'windows:pos-wpf': 'skipped',
        'macos:pos-ipad-native': 'executed',
        'macos:pos-handheld-native': 'executed',
        'macos:supplier-safari': 'executed',
      },
    },
    {
      name: 'Windows',
      kernelName: 'Windows_NT',
      osName: 'Windows_NT',
      statuses: {
        'android:pos-handheld-android': 'skipped',
        'windows:pos-wpf': 'executed',
        'macos:pos-ipad-native': 'skipped',
        'macos:pos-handheld-native': 'skipped',
        'macos:supplier-safari': 'skipped',
      },
    },
  ]

  for (const scenario of scenarios) {
    const result = runTestAll(scenario)
    assert.equal(result.error, undefined, `${scenario.name}: ${result.error?.message ?? ''}`)
    assert.equal(result.status, 0, `${scenario.name}: ${result.stderr}`)
    const reports = result.stdout
      .split(/\r?\n/)
      .map((line) => line.match(/^(executed|skipped) lane=(\S+) reason=(.+)$/))
      .filter(Boolean)
      .map((match) => ({ status: match[1], lane: match[2], reason: match[3] }))
    const byLane = new Map(reports.map((report) => [report.lane, report]))

    assert.deepEqual([...byLane.keys()].sort(), [...expectedLanes].sort(), `${scenario.name}: lane 清单不完整`)
    assert.equal(reports.length, expectedLanes.length, `${scenario.name}: 每个 lane 必须且只能报告一次`)
    for (const lane of expectedLanes.slice(0, 9)) {
      assert.equal(byLane.get(lane)?.status, 'executed', `${scenario.name}: ${lane}`)
    }
    for (const [lane, expectedStatus] of Object.entries(scenario.statuses)) {
      assert.equal(byLane.get(lane)?.status, expectedStatus, `${scenario.name}: ${lane}`)
    }
    for (const report of reports) {
      assert.ok(report.reason.trim().length > 0, `${scenario.name}: ${report.lane} 缺少原因`)
    }
  }
})

test('Weekly SQL 使用独立容器且不会接入 LiveE2e', () => {
  const source = readFileSync(workflowPath, 'utf8')
  assert.match(source, /mcr\.microsoft\.com\/mssql\/server@sha256:[0-9a-f]{64}/)
  assert.match(source, /run-weekly-sql\.sh/)
  assert.doesNotMatch(source, /Category=LiveE2e/)
})

test('Mobile CI 同时执行 iOS 与 Android bundle 构建', () => {
  const runner = readFileSync(nodeRunnerPath, 'utf8')
  const mobilePackage = JSON.parse(readFileSync(mobilePackagePath, 'utf8'))
  assert.match(runner, /npm --prefix apps\/mobile run build:ci/)
  assert.match(mobilePackage.scripts['build:ci'], /expo export --platform ios/)
  assert.match(mobilePackage.scripts['build:ci'], /expo export --platform android/)
})

test('环境敏感的 POS 微基准只在 weekly profile 执行', () => {
  const workflowSource = readFileSync(workflowPath, 'utf8')
  const testAllSource = readFileSync(testAllPath, 'utf8')
  assert.match(
    workflowSource,
    /HBPOS_RUN_PERF_TESTS:\s*\$\{\{ needs\.plan\.outputs\.profile == 'weekly' && '1' \|\| '0' \}\}/,
  )
  assert.match(testAllSource, /export HBPOS_RUN_PERF_TESTS="\$\{HBPOS_RUN_PERF_TESTS:-1\}"/)

  for (const path of posPricingPerformanceTestPaths) {
    const source = readFileSync(path, 'utf8')
    assert.match(source, /const performanceTest = process\.env\.HBPOS_RUN_PERF_TESTS === "1" \? test : test\.skip;/)
    assert.match(source, /performanceTest\("300 行不可合并百分比折扣的按钮预测保持近线性"/)
  }
})
