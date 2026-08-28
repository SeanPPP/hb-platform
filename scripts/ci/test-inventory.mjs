import { execFileSync, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(scriptDirectory, '..', '..')
const APP_ROOTS = Object.freeze({
  web: 'apps/web',
  mobile: 'apps/mobile',
  'pos-ipad': 'apps/pos-ipad',
  'pos-handheld': 'apps/pos-handheld',
})
const CONSUMED_LANES = Object.freeze({
  web: new Set(['ignored-generated', 'typecheck', 'web-esbuild', 'node']),
  mobile: new Set(['typecheck', 'tsx', 'node']),
  'pos-ipad': new Set(['typecheck', 'tsx', 'jest', 'node', 'native']),
  'pos-handheld': new Set(['typecheck', 'tsx', 'jest', 'node', 'native']),
})

export function classifyTestFile(app, file, source = '') {
  const normalized = file.replaceAll('\\', '/')
  if (app === 'web' && normalized.startsWith('apps/web/tmp/hbweb_rv_')) {
    return 'ignored-generated'
  }
  if (/\.types\.test\.tsx$/.test(normalized)) {
    return 'typecheck'
  }
  if (/\/native-interop\.test\.mjs$/.test(normalized) ||
      normalized === 'apps/pos-ipad/scripts/check-external-display-startup.test.mjs') {
    return 'native'
  }
  if (/\.rntl\.test\.tsx?$/.test(normalized) || source.includes('@jest/globals')) {
    return 'jest'
  }
  if (/\.test\.ts$/.test(normalized)) {
    return app === 'web' ? 'web-esbuild' : 'tsx'
  }
  if (/\.test\.tsx$/.test(normalized)) {
    if (app === 'web') {
      return 'web-esbuild'
    }
    throw new Error(`无法分类 ${app} 测试: ${normalized}`)
  }
  if (/\.test\.(?:mjs|cjs|js)$/.test(normalized) || /\/test-[^/]+\.(?:mjs|cjs|js)$/.test(normalized)) {
    return 'node'
  }
  throw new Error(`无法分类 ${app} 测试: ${normalized}`)
}

export function validateInventoryLanes(app, inventory) {
  const consumed = CONSUMED_LANES[app]
  if (!consumed) {
    throw new Error(`未知测试应用: ${app}`)
  }
  const unconsumed = [...inventory.entries()]
    .filter(([lane, files]) => files.length > 0 && !consumed.has(lane))
    .map(([lane]) => lane)
  if (unconsumed.length > 0) {
    throw new Error(`${app} 存在未被 CI 消费的测试 lane: ${unconsumed.join(', ')}`)
  }
}

function isTestCandidate(file) {
  return /\.test\.(?:ts|tsx|mjs|cjs|js)$/.test(file) ||
    /\/test-[^/]+\.(?:mjs|cjs|js)$/.test(file)
}

export function discoverTestInventory(app) {
  const appRoot = APP_ROOTS[app]
  if (!appRoot) {
    throw new Error(`未知测试应用: ${app}`)
  }
  const tracked = execFileSync('git', ['ls-files', '-z', '--', appRoot], {
    cwd: repositoryRoot,
  }).toString('utf8').split('\0').filter(Boolean)

  const inventory = new Map()
  for (const file of tracked.filter(isTestCandidate).sort()) {
    const source = /\.tsx?$/.test(file)
      ? readFileSync(resolve(repositoryRoot, file), 'utf8')
      : ''
    const lane = classifyTestFile(app, file, source)
    const files = inventory.get(lane) ?? []
    files.push(file)
    inventory.set(lane, files)
  }
  validateInventoryLanes(app, inventory)
  if ([...inventory.values()].reduce((sum, files) => sum + files.length, 0) === 0) {
    throw new Error(`${app} 没有发现任何跟踪中的测试文件`)
  }
  return inventory
}

function parseArgs(argv) {
  const options = { shardIndex: 0, shardCount: 1 }
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index]
    if (['--app', '--run', '--list', '--shard-index', '--shard-count'].includes(arg)) {
      const value = argv[index + 1]
      if (!value) {
        throw new Error(`${arg} 缺少参数`)
      }
      const key = arg.slice(2).replace(/-([a-z])/g, (_, character) => character.toUpperCase())
      options[key] = value
      index += 1
    } else if (arg === '--check') {
      options.check = true
    } else {
      throw new Error(`未知参数: ${arg}`)
    }
  }
  options.shardIndex = Number(options.shardIndex)
  options.shardCount = Number(options.shardCount)
  if (!Number.isInteger(options.shardIndex) || !Number.isInteger(options.shardCount) ||
      options.shardCount < 1 || options.shardIndex < 0 || options.shardIndex >= options.shardCount) {
    throw new Error('分片参数必须满足 0 <= shard-index < shard-count')
  }
  if (!options.app) {
    throw new Error('缺少 --app')
  }
  return options
}

function shardFiles(files, shardIndex, shardCount) {
  return files.filter((file) => {
    const digest = createHash('sha256').update(file).digest()
    return digest.readUInt32BE(0) % shardCount === shardIndex
  })
}

function run(command, argumentsList, cwd) {
  const result = spawnSync(command, argumentsList, {
    cwd,
    env: process.env,
    encoding: 'utf8',
    stdio: 'inherit',
  })
  if (result.error) {
    throw result.error
  }
  if (result.status !== 0) {
    throw new Error(`${command} 执行失败，退出码 ${result.status}`)
  }
}

async function runWebEsbuild(files, appRoot) {
  const require = createRequire(resolve(appRoot, 'package.json'))
  const esbuild = require('esbuild')
  const cacheRoot = resolve(appRoot, 'node_modules', '.cache')
  mkdirSync(cacheRoot, { recursive: true })
  const temporaryDirectory = mkdtempSync(resolve(cacheRoot, 'hbweb-ci-'))
  try {
    for (const [index, file] of files.entries()) {
      const output = resolve(temporaryDirectory, `${String(index).padStart(4, '0')}.mjs`)
      console.log(`[web-esbuild ${index + 1}/${files.length}] ${file}`)
      await esbuild.build({
        absWorkingDir: repositoryRoot,
        entryPoints: [file],
        outfile: output,
        bundle: true,
        platform: 'node',
        format: 'esm',
        jsx: 'automatic',
        define: { 'import.meta.env': '{}' },
        external: ['vite', '@vitejs/plugin-react'],
        banner: {
          js: 'import { createRequire as __ciCreateRequire } from "node:module"; const require = __ciCreateRequire(import.meta.url);',
        },
        logLevel: 'silent',
      })
      run(process.execPath, [output], appRoot)
    }
  } finally {
    rmSync(temporaryDirectory, { recursive: true, force: true })
  }
}

async function runLane(app, lane, files) {
  if (files.length === 0) {
    throw new Error(`${app}/${lane} 分片没有测试文件`)
  }
  const appRoot = resolve(repositoryRoot, APP_ROOTS[app])
  const relativeFiles = files.map((file) => relative(appRoot, resolve(repositoryRoot, file)))
  if (lane === 'web-esbuild') {
    await runWebEsbuild(files, appRoot)
  } else if (lane === 'tsx') {
    run(resolve(appRoot, 'node_modules/.bin/tsx'), [
      '--test',
      '--test-concurrency=4',
      ...relativeFiles,
    ], appRoot)
  } else if (lane === 'jest') {
    run(resolve(appRoot, 'node_modules/.bin/jest'), [...relativeFiles, '--runInBand'], appRoot)
  } else if (lane === 'node' || lane === 'native') {
    run(process.execPath, ['--test', ...relativeFiles], appRoot)
  } else {
    throw new Error(`lane ${lane} 不能作为运行时测试执行`)
  }
}

async function main() {
  const options = parseArgs(process.argv.slice(2))
  const inventory = discoverTestInventory(options.app)
  const summary = Object.fromEntries(
    [...inventory.entries()].map(([lane, files]) => [lane, files.length]),
  )
  if (options.check) {
    console.log(`${options.app} test inventory: ${JSON.stringify(summary)}`)
  }
  const selectedLane = options.run || options.list
  if (!selectedLane) {
    if (!options.check) {
      throw new Error('必须指定 --check、--list 或 --run')
    }
    return
  }
  const selected = shardFiles(
    inventory.get(selectedLane) ?? [],
    options.shardIndex,
    options.shardCount,
  )
  if (options.list) {
    process.stdout.write(`${selected.join('\n')}${selected.length ? '\n' : ''}`)
    return
  }
  await runLane(options.app, selectedLane, selected)
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main()
}
