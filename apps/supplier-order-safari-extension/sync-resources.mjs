import { cpSync, existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { synchronizeXcodeProjectVersions } from './project-version.mjs';

const ROOT = dirname(fileURLToPath(import.meta.url));
const SOURCE = resolve(ROOT, '..', 'supplier-order-extension', 'dist', 'safari');
const TARGET = resolve(
  ROOT,
  'xcode',
  'HB Supplier Order Safari',
  'HB Supplier Order Safari Extension',
  'Resources',
);
const XCODE_ROOT = resolve(ROOT, 'xcode');
const XCODE_PROJECT = resolve(
  XCODE_ROOT,
  'HB Supplier Order Safari',
  'HB Supplier Order Safari.xcodeproj',
  'project.pbxproj',
);

if (!existsSync(join(SOURCE, 'manifest.json'))) {
  throw new Error('缺少 dist/safari，请先运行 npm --prefix ../supplier-order-extension run build');
}
if (!TARGET.startsWith(`${XCODE_ROOT}${sep}`) || relative(XCODE_ROOT, TARGET).startsWith('..')) {
  throw new Error('拒绝同步到 Xcode 项目以外的路径');
}

const manifest = JSON.parse(readFileSync(join(SOURCE, 'manifest.json'), 'utf8'));
if (manifest.browser_specific_settings?.safari?.strict_min_version !== '16.4') {
  throw new Error('Safari manifest 缺少 strict_min_version=16.4');
}
const projectSource = readFileSync(XCODE_PROJECT, 'utf8');
const updatedProject = synchronizeXcodeProjectVersions(projectSource, manifest.version);

// Resources 是 packager 生成的快照；每次完整替换可清除已从共享源码删除的旧资源。
rmSync(TARGET, { recursive: true, force: true });
cpSync(SOURCE, TARGET, { recursive: true });
if (updatedProject !== projectSource) {
  writeFileSync(XCODE_PROJECT, updatedProject);
}
console.log(`已同步 ${SOURCE} -> ${TARGET}`);
console.log(`已同步 Xcode 版本: ${manifest.version}`);
