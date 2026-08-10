import { cp, mkdir, readdir, rm } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const publicRoot = path.join(projectRoot, 'public');
const distRoot = path.join(projectRoot, 'dist');

const checkResult = spawnSync(process.execPath, [path.join(projectRoot, 'scripts/check.mjs')], {
  cwd: projectRoot,
  encoding: 'utf8',
  stdio: 'inherit'
});

if (checkResult.status !== 0) process.exit(checkResult.status ?? 1);

await rm(distRoot, { recursive: true, force: true });
await mkdir(distRoot, { recursive: true });
await cp(publicRoot, distRoot, { recursive: true });

async function listFiles(directory, prefix = '') {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const relativePath = path.posix.join(prefix, entry.name);
    if (entry.isDirectory()) {
      files.push(...await listFiles(path.join(directory, entry.name), relativePath));
    } else {
      files.push(relativePath);
    }
  }

  return files.sort();
}

const files = await listFiles(distRoot);
console.log(`ANTPOS production build complete: dist/ (${files.length} files)`);
for (const file of files) console.log(`- ${file}`);
