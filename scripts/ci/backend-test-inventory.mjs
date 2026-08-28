import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const TEST_PROJECT_DIR = 'services/backend/BlazorApp.Api.Tests'

export const ALLOWED_EXCLUSIONS = new Map()

export function parseCompileIncludes(projectXml) {
  const includes = new Set()
  const pattern = /<Compile\s+Include="([^"]+)"\s*\/>/g
  for (const match of projectXml.matchAll(pattern)) {
    includes.add(match[1].replaceAll('\\', '/'))
  }
  return includes
}

export function auditBackendTestInventory({
  projectXml,
  trackedFiles,
  allowedExclusions = ALLOWED_EXCLUSIONS,
}) {
  const compileIncludes = parseCompileIncludes(projectXml)
  // 显式 Compile 模式必须审计全部 tracked C# 文件，避免自定义 Fact/Theory 属性绕过清单。
  const candidates = trackedFiles
  const errors = []
  const excluded = []
  let compiledCount = 0

  for (const file of candidates) {
    if (compileIncludes.has(file)) {
      compiledCount += 1
      continue
    }
    if (allowedExclusions.has(file)) {
      const reason = allowedExclusions.get(file)
      if (typeof reason !== 'string' || reason.trim().length === 0) {
        errors.push(`隔离文件缺少原因: ${file}`)
      } else {
        excluded.push(file)
      }
      continue
    }
    errors.push(`测试文件未进入 csproj 编译清单: ${file}`)
  }

  for (const file of allowedExclusions.keys()) {
    if (!candidates.includes(file)) {
      errors.push(`隔离文件已不存在: ${file}`)
    }
  }

  return {
    candidateCount: candidates.length,
    compiledCount,
    errors,
    excluded,
  }
}

function main() {
  const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
  const projectPath = resolve(repositoryRoot, TEST_PROJECT_DIR, 'BlazorApp.Api.Tests.csproj')
  const trackedOutput = execFileSync(
    'git',
    ['ls-files', '-z', '--', `${TEST_PROJECT_DIR}/*.cs`],
    { cwd: repositoryRoot },
  ).toString('utf8')
  const trackedFiles = trackedOutput
    .split('\0')
    .filter(Boolean)
    .map((file) => file.slice(`${TEST_PROJECT_DIR}/`.length))
  const result = auditBackendTestInventory({
    projectXml: readFileSync(projectPath, 'utf8'),
    trackedFiles,
  })

  if (result.errors.length > 0) {
    for (const error of result.errors) {
      console.error(error)
    }
    process.exitCode = 1
    return
  }

  console.log(
    `Backend 测试清单: ${result.compiledCount}/${result.candidateCount} 已编译，` +
      `${result.excluded.length} 个显式隔离`,
  )
  for (const file of result.excluded) {
    console.log(`- ${file}: ${ALLOWED_EXCLUSIONS.get(file)}`)
  }
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main()
}
