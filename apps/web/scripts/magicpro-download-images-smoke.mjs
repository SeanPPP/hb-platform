import { access, mkdtemp, readFile, rm } from 'node:fs/promises';
import { constants } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

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
  const outputDir = await mkdtemp(path.join(tmpdir(), 'magicpro-smoke-'));
  const scriptPath = path.resolve('scripts/magicpro-download-images.mjs');

  try {
    const { stdout } = await runNodeScript(
      scriptPath,
      ['--output', outputDir, '--concurrency', '2', '--limit', '5'],
      {
        ...process.env,
        MAGICPRO_EMAIL: 'sean@hotbargain.com.au',
        MAGICPRO_PASSWORD: '12345',
      },
    );

    const summary = JSON.parse(stdout);
    if (summary.totalProducts < 1) {
      throw new Error('未拉取到商品');
    }

    if (summary.uniqueSkuSaved < 1) {
      throw new Error('未成功下载任何商品图片');
    }

    const reportPath = path.join(outputDir, 'download-report.csv');
    await access(reportPath, constants.F_OK);
    const reportContent = await readFile(reportPath, 'utf8');

    if (!reportContent.includes('sku,product_name,product_url,image_url,saved_file,status,note')) {
      throw new Error('报告头部不正确');
    }

    console.log(JSON.stringify({
      totalProducts: summary.totalProducts,
      uniqueSkuSaved: summary.uniqueSkuSaved,
      failed: summary.failed,
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
