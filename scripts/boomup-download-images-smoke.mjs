#!/usr/bin/env node

import { access, mkdtemp, readFile, rm } from 'node:fs/promises';
import { constants } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

function requireEnv(name) {
  const value = process.env[name];

  if (!value) {
    throw new Error(`缺少环境变量: ${name}`);
  }

  return value;
}

function runNodeScript(scriptPath, args, env) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [scriptPath, ...args], {
      env,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    let stdout = '';
    let stderr = '';

    child.stdout.on('data', (chunk) => {
      stdout += chunk.toString();
    });

    child.stderr.on('data', (chunk) => {
      stderr += chunk.toString();
    });

    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) {
        resolve({ stdout, stderr });
        return;
      }

      reject(new Error(stderr || stdout));
    });
  });
}

async function main() {
  const outputDir = await mkdtemp(path.join(tmpdir(), 'boomup-smoke-'));
  const scriptPath = path.resolve('scripts/boomup-download-images.mjs');

  try {
    const { stdout } = await runNodeScript(
      scriptPath,
      ['--output-dir', outputDir, '--limit', '5', '--resume'],
      {
        ...process.env,
        BOOMUP_EMAIL: requireEnv('BOOMUP_EMAIL'),
        BOOMUP_PASSWORD: requireEnv('BOOMUP_PASSWORD'),
      },
    );

    const summary = JSON.parse(stdout);
    if (summary.totalProducts < 1) {
      throw new Error('未拉取到商品');
    }

    if (summary.manifestCount < 1) {
      throw new Error('未成功产出任何图片');
    }

    const manifestPath = path.join(outputDir, 'manifest.csv');
    const errorsPath = path.join(outputDir, 'errors.csv');
    await access(manifestPath, constants.F_OK);
    await access(errorsPath, constants.F_OK);

    const manifestContent = await readFile(manifestPath, 'utf8');
    if (!manifestContent.includes('sku,product_url,image_url,saved_path')) {
      throw new Error('manifest.csv 头部不正确');
    }

    console.log(JSON.stringify({
      totalProducts: summary.totalProducts,
      manifestCount: summary.manifestCount,
      downloadedCount: summary.downloadedCount,
      alreadyExistsCount: summary.alreadyExistsCount,
      errorCount: summary.errorCount,
      sampleChecks: summary.sampleChecks,
    }, null, 2));
  } finally {
    await rm(outputDir, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
