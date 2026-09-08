import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { chmodSync, copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const root = fileURLToPath(new URL('../../', import.meta.url))
const prWorkflow = readFileSync(join(root, '.github/workflows/pr-ci.yml'), 'utf8')
const qualityWorkflow = readFileSync(join(root, '.github/workflows/quality-baseline.yml'), 'utf8')

function job(name) {
  const match = prWorkflow.match(new RegExp(`^  ${name}:\\n[\\s\\S]*?(?=^  \\w+:\\n|$(?![\\s\\S]))`, 'm'))
  assert.ok(match, `缺少 ${name} job`)
  return match[0]
}

test('质量 lane 只从本次实际执行的组件派生，文档和 WPF 不等待缺失报告', () => {
  for (const [args, expected] of [
    [['--component', 'backend'], ['backend']],
    [['--component', 'web'], ['web']],
    [['--component', 'pos-ipad'], ['pos-ipad']],
    [['--component', 'pos-wpf'], []],
    [['--base', 'HEAD', '--head', 'HEAD'], []],
    [['--full', '--profile', 'weekly'], ['backend', 'web', 'pos-ipad', 'pos-handheld']],
  ]) {
    const output = execFileSync(process.execPath, ['scripts/ci/plan.mjs', ...args], {
      cwd: root, encoding: 'utf8', env: { ...process.env, GITHUB_OUTPUT: '' },
    })
    const value = output.split('\n').find(line => line.startsWith('quality_lanes='))
    assert.ok(value, '规划器必须输出质量报告的精确预期范围')
    assert.deepEqual(JSON.parse(value.slice('quality_lanes='.length)), expected)
  }
})

test('PR 只执行一次组件验证，质量汇总复用其 artifact 并纳入 required', () => {
  assert.doesNotMatch(qualityWorkflow, /^  pull_request:/m)
  assert.match(job('plan'), /node --test scripts\/ci\/\*\.test\.mjs scripts\/performance\/\*\.test\.mjs/)
  for (const lane of ['linux_node', 'linux_dotnet']) {
    const source = job(lane)
    assert.equal([...source.matchAll(/run-(?:node|dotnet)-component\.sh/g)].length, 1)
    assert.doesNotMatch(source, /quality-lane\.mjs run/)
    assert.match(source, /quality-lane\.mjs start/)
    assert.match(source, /quality-lane\.mjs finish/)
    assert.match(source, /steps\.verify\.outcome/)
    assert.match(source, /quality-lane-\$\{\{ github\.run_id \}\}-\$\{\{ matrix\.component \}\}/)
  }
  const report = job('quality_report')
  assert.match(report, /needs: \[plan, linux_node, linux_dotnet\]/)
  assert.match(report, /if: \$\{\{ always\(\) \}\}/)
  assert.match(report, /quality_lanes != '\[\]'/)
  assert.match(report, /build-metric-batch\.mjs/)
  assert.match(report, /compare-json-budget\.mjs/)
  assert.doesNotMatch(report, /secrets\.|quality-lane\.mjs run|npm ci|dotnet build/)
  assert.match(job('required'), /- quality_report/)
  assert.match(job('required'), /"quality_report":"\$\{\{ needs\.quality_report\.result \}\}"/)
})

test('POS 的原有 Metro 验证迁入单次安装的 Node lane，失败仍立即退出', () => {
  const temporaryRoot = mkdtempSync(join(tmpdir(), 'hb-ci-quality-reuse-'))
  try {
    const runner = join(temporaryRoot, 'scripts/ci/run-node-component.sh')
    const bin = join(temporaryRoot, 'bin')
    mkdirSync(dirname(runner), { recursive: true })
    mkdirSync(bin)
    copyFileSync(join(root, 'scripts/ci/run-node-component.sh'), runner)
    const npm = join(bin, 'npm')
    writeFileSync(npm, '#!/usr/bin/env bash\nprintf "%s\\n" "$*" >> "$CI_COMMAND_LOG"\nif [[ "$*" == *verify:metro-bundle* ]]; then exit "${CI_METRO_EXIT:-0}"; fi\n')
    chmodSync(npm, 0o755)
    for (const component of ['pos-ipad', 'pos-handheld']) {
      for (const exitCode of [0, 17]) {
        const log = join(temporaryRoot, `${component}-${exitCode}.log`)
        const result = spawnSync('bash', [runner, component], {
          encoding: 'utf8', env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, CI_COMMAND_LOG: log, CI_METRO_EXIT: String(exitCode) },
        })
        assert.equal(result.status, exitCode, result.stderr)
        const commands = readFileSync(log, 'utf8').trim().split('\n')
        assert.equal(commands.filter(line => line.startsWith('ci ')).length, 1)
        assert.equal(commands.filter(line => line === `run test:ci --workspace=@hb/${component}`).length, 1)
        assert.equal(commands.filter(line => line === `run verify:metro-bundle --workspace=@hb/${component}`).length, 1)
      }
    }
  } finally {
    rmSync(temporaryRoot, { recursive: true, force: true })
  }
})

test('质量汇总 CLI 对部分 artifact 缺失或失败返回非零，并保留诊断报告', () => {
  const temporaryRoot = mkdtempSync(join(tmpdir(), 'hb-ci-quality-missing-'))
  const result = {
    schemaVersion: 'QualityLaneResultV1', lane: 'backend',
    startedAtUtc: '2026-09-08T00:00:00.000Z', finishedAtUtc: '2026-09-08T00:00:01.000Z',
    durationMs: 1000, conclusion: 'accepted',
  }
  try {
    for (const mode of ['missing', 'failed']) {
      const resultsDir = join(temporaryRoot, mode)
      mkdirSync(resultsDir)
      writeFileSync(join(resultsDir, 'backend.json'), JSON.stringify(result))
      if (mode === 'failed') {
        writeFileSync(join(resultsDir, 'pos-ipad.json'), JSON.stringify({
          ...result, lane: 'pos-ipad', conclusion: 'failed', errorCode: 'verification_failed',
        }))
      }
      const output = join(temporaryRoot, `${mode}.json`)
      const child = spawnSync(process.execPath, [
        'scripts/performance/build-metric-batch.mjs', '--results-dir', resultsDir, '--output', output,
      ], {
        cwd: root, encoding: 'utf8',
        env: {
          ...process.env, QUALITY_EXPECTED_LANES: '["backend","pos-ipad"]',
          GITHUB_REPOSITORY: 'SeanPPP/hb-platform', GITHUB_EVENT_NAME: 'pull_request',
          GITHUB_REF: 'refs/pull/87/merge', GITHUB_SHA: 'a'.repeat(40),
          GITHUB_WORKFLOW: 'PR CI', GITHUB_RUN_ID: 'test-quality-missing', GITHUB_RUN_ATTEMPT: '1',
        },
      })
      assert.equal(child.status, 1, `${mode}: ${child.stdout}\n${child.stderr}`)
      assert.ok(JSON.parse(readFileSync(output, 'utf8')).events.length > 0)
      assert.match(child.stderr, /pos-ipad/)
    }
  } finally {
    rmSync(temporaryRoot, { recursive: true, force: true })
  }
})
