import { readFileSync, readdirSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const TARGETS = ['chrome', 'edge', 'safari']
const ALLOWED_PERMISSIONS = new Set(['storage', 'sidePanel', 'scripting'])

function referencedFiles(manifest) {
  const files = new Set()
  const add = (value) => {
    if (typeof value === 'string' && value.length > 0) files.add(value)
  }
  add(manifest.background?.service_worker)
  add(manifest.side_panel?.default_path)
  add(manifest.options_ui?.page)
  for (const script of manifest.content_scripts || []) {
    for (const file of script.js || []) add(file)
    for (const file of script.css || []) add(file)
  }
  for (const file of Object.values(manifest.icons || {})) add(file)
  for (const file of Object.values(manifest.action?.default_icon || {})) add(file)
  return files
}

export function validateBuiltManifest({
  manifest,
  manifestText,
  target,
  expectedVersion,
  files,
}) {
  const errors = []
  if (manifest.manifest_version !== 3) {
    errors.push(`${target}: manifest_version 必须为 3`)
  }
  if (manifest.name !== 'HB Supplier Order') {
    errors.push(`${target}: 扩展名称不匹配`)
  }
  if (manifest.version !== expectedVersion) {
    errors.push(`${target}: manifest 版本 ${manifest.version || '<missing>'} 与 package ${expectedVersion} 不一致`)
  }
  if (/__[A-Z0-9_]+__/.test(manifestText)) {
    errors.push(`${target}: manifest 仍包含未替换占位符`)
  }
  for (const permission of manifest.permissions || []) {
    if (!ALLOWED_PERMISSIONS.has(permission)) {
      errors.push(`${target}: 未允许的权限 ${permission}`)
    }
  }
  if (!Array.isArray(manifest.host_permissions) || manifest.host_permissions.length === 0) {
    errors.push(`${target}: host_permissions 不能为空`)
  }
  if (target === 'safari') {
    if (manifest.browser_specific_settings?.safari?.strict_min_version !== '16.4') {
      errors.push('safari: strict_min_version 必须为 16.4')
    }
  } else if (manifest.minimum_chrome_version !== '116') {
    errors.push(`${target}: minimum_chrome_version 必须为 116`)
  }
  for (const file of referencedFiles(manifest)) {
    if (!files.has(file)) {
      errors.push(`${target}: manifest 引用文件不存在: ${file}`)
    }
  }
  return errors
}

function listFiles(root, current = root) {
  return readdirSync(current, { withFileTypes: true }).flatMap((entry) => {
    const path = join(current, entry.name)
    if (entry.isDirectory()) return listFiles(root, path)
    return [relative(root, path).replaceAll('\\', '/')]
  })
}

function main() {
  const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
  const extensionRoot = resolve(repositoryRoot, 'apps/supplier-order-extension')
  const expectedVersion = JSON.parse(
    readFileSync(join(extensionRoot, 'package.json'), 'utf8'),
  ).version
  const errors = []
  for (const target of TARGETS) {
    const root = join(extensionRoot, 'dist', target)
    const manifestText = readFileSync(join(root, 'manifest.json'), 'utf8')
    errors.push(
      ...validateBuiltManifest({
        manifest: JSON.parse(manifestText),
        manifestText,
        target,
        expectedVersion,
        files: new Set(listFiles(root)),
      }),
    )
  }
  if (errors.length > 0) {
    for (const error of errors) console.error(error)
    process.exitCode = 1
    return
  }
  console.log(`供应商扩展 manifest 校验通过: ${TARGETS.join(', ')} v${expectedVersion}`)
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main()
}
