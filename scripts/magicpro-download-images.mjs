#!/usr/bin/env node

import { access, mkdir, mkdtemp, readdir, rm, unlink, writeFile } from 'node:fs/promises';
import { constants } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

const MAGICPRO_BASE_URL = 'https://www.magicpro.com.au';
const MAGICPRO_LOGIN_URL = `${MAGICPRO_BASE_URL}/api/authentification/login`;
const MAGICPRO_PRODUCTS_URL = `${MAGICPRO_BASE_URL}/api/products`;
const MAGICPRO_CATEGORIES_URL = `${MAGICPRO_BASE_URL}/api/categories`;
const MAGICPRO_IMAGE_SERVER = 'https://d134dqmgicvbpa.cloudfront.net';
const DEFAULT_OUTPUT_DIR = '/Users/sean/Downloads/magicpro-product-images';
const DEFAULT_CONCURRENCY = 8;

function parseArgs(argv) {
  const options = {};

  for (let index = 0; index < argv.length; index += 1) {
    const current = argv[index];

    if (!current.startsWith('--')) {
      continue;
    }

    const [rawKey, inlineValue] = current.slice(2).split('=');
    const key = rawKey.trim();
    const nextValue = inlineValue ?? argv[index + 1];

    switch (key) {
      case 'email':
      case 'password':
      case 'output':
      case 'concurrency':
      case 'limit':
        options[key] = inlineValue ?? nextValue;
        if (inlineValue == null) {
          index += 1;
        }
        break;
      default:
        throw new Error(`不支持的参数: --${key}`);
    }
  }

  return options;
}

function requireOption(value, label) {
  if (!value) {
    throw new Error(`缺少必填参数: ${label}`);
  }

  return value;
}

function sanitizeSku(rawSku, fallbackId) {
  const sku = String(rawSku ?? '').trim();

  if (!sku) {
    return `missing-sku-${fallbackId}`;
  }

  return sku.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_');
}

function escapeCsv(value) {
  const normalized = value == null ? '' : String(value);
  return `"${normalized.replace(/"/g, '""')}"`;
}

function buildCookieHeader(headers) {
  const rawCookies = typeof headers.getSetCookie === 'function'
    ? headers.getSetCookie()
    : headers.get('set-cookie')
      ? [headers.get('set-cookie')]
      : [];

  return rawCookies
    .map((cookie) => cookie.split(';')[0]?.trim())
    .filter(Boolean)
    .join('; ');
}

async function login(email, password) {
  const response = await fetch(MAGICPRO_LOGIN_URL, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
    },
    body: JSON.stringify({
      username: email,
      password,
    }),
    redirect: 'manual',
  });

  const cookieHeader = buildCookieHeader(response.headers);

  if (!response.ok && response.status !== 302) {
    const bodyText = await response.text();
    throw new Error(`登录失败: HTTP ${response.status} ${bodyText}`);
  }

  if (!cookieHeader) {
    throw new Error('登录成功但未拿到会话 Cookie');
  }

  return cookieHeader;
}

async function fetchProducts(cookieHeader) {
  const response = await fetch(MAGICPRO_PRODUCTS_URL, {
    headers: {
      cookie: cookieHeader,
      accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`拉取商品失败: HTTP ${response.status}`);
  }

  return response.json();
}

async function fetchCategories(cookieHeader) {
  const response = await fetch(MAGICPRO_CATEGORIES_URL, {
    headers: {
      cookie: cookieHeader,
      accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`拉取分类失败: HTTP ${response.status}`);
  }

  return response.json();
}

function buildTypeLookup(categories) {
  const lookup = new Map();

  for (const category of categories) {
    for (const type of category?.types ?? []) {
      lookup.set(String(type._id), {
        ...type,
        category: {
          _id: category._id,
          slug: category.slug,
          title: category.title,
        },
      });
    }
  }

  return lookup;
}

function hydrateProducts(products, typeLookup) {
  return products.map((product) => {
    const typeId = typeof product.type === 'string' ? product.type : product.type?._id;
    const type = typeLookup.get(String(typeId)) ?? product.type;

    return {
      ...product,
      type,
    };
  });
}

function chooseImage(product) {
  const designWithImage = Array.isArray(product.designs)
    ? product.designs.find((design) => design?.image?.path)
    : null;

  if (product?.image?.path) {
    return {
      image: product.image,
      source: 'product.image',
    };
  }

  if (designWithImage?.image?.path) {
    return {
      image: designWithImage.image,
      source: `design.image:${designWithImage.identifier ?? 'unknown'}`,
    };
  }

  return null;
}

function buildImageUrl(image) {
  const width = Number(image?.width) > 0 ? Number(image.width) : 1200;
  return `${MAGICPRO_IMAGE_SERVER}/w=${width}/${image.path}`;
}

async function streamToFile(readable, filePath) {
  const chunks = [];

  for await (const chunk of readable) {
    chunks.push(chunk);
  }

  await writeFile(filePath, Buffer.concat(chunks));
}

function runCommand(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
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

      reject(new Error(`${command} ${args.join(' ')} 执行失败: ${stderr || stdout}`));
    });
  });
}

async function convertToJpeg(sourcePath, targetPath) {
  await runCommand('sips', ['-s', 'format', 'jpeg', sourcePath, '--out', targetPath]);
}

async function inspectJpeg(filePath) {
  const { stdout } = await runCommand('sips', ['-g', 'format', filePath]);
  return /format:\s+jpeg/i.test(stdout);
}

async function fileExists(filePath) {
  try {
    await access(filePath, constants.F_OK);
    return true;
  } catch {
    return false;
  }
}

async function downloadOneProduct(product, outputDir, tempDir, seenSkus) {
  const sku = sanitizeSku(product.identifier, product._id);
  const report = {
    sku,
    product_name: product.title ?? '',
    product_url: `${MAGICPRO_BASE_URL}/products/${product.type?.category?.slug ?? 'unknown'}/${product.type?.slug ?? 'unknown'}/${product.slug ?? ''}`,
    image_url: '',
    saved_file: '',
    status: 'failed',
    note: '',
  };

  if (seenSkus.has(sku)) {
    report.status = 'skipped';
    report.note = '重复货号，按规则只保留第一张主图';
    return report;
  }

  const chosen = chooseImage(product);

  if (!chosen) {
    report.note = '未找到可用主图';
    return report;
  }

  const imageUrl = buildImageUrl(chosen.image);
  const tempSourcePath = path.join(tempDir, `${sku}-${product._id}.source`);
  const targetPath = path.join(outputDir, `${sku}.jpg`);

  report.image_url = imageUrl;
  report.saved_file = targetPath;

  try {
    // 已有可复用文件时直接记为成功，便于只修复报告或补跑失败项。
    if (await fileExists(targetPath)) {
      seenSkus.add(sku);
      report.status = 'success';
      report.note = `${chosen.source}; reused-existing`;
      return report;
    }

    const response = await fetch(imageUrl);

    if (!response.ok || !response.body) {
      throw new Error(`图片下载失败: HTTP ${response.status}`);
    }

    await streamToFile(response.body, tempSourcePath);
    await convertToJpeg(tempSourcePath, targetPath);
    seenSkus.add(sku);
    report.status = 'success';
    report.note = chosen.source;
    await unlink(tempSourcePath).catch(() => {});
    return report;
  } catch (error) {
    report.note = error instanceof Error ? error.message : String(error);
    await unlink(tempSourcePath).catch(() => {});
    return report;
  }
}

async function runWithConcurrency(items, worker, concurrency) {
  const results = new Array(items.length);
  let cursor = 0;

  async function consume() {
    while (cursor < items.length) {
      const currentIndex = cursor;
      cursor += 1;
      results[currentIndex] = await worker(items[currentIndex], currentIndex);
    }
  }

  await Promise.all(Array.from({ length: Math.min(concurrency, items.length) }, () => consume()));
  return results;
}

async function writeReport(reportPath, rows) {
  const header = ['sku', 'product_name', 'product_url', 'image_url', 'saved_file', 'status', 'note'];
  const lines = [
    header.join(','),
    ...rows.map((row) => header.map((column) => escapeCsv(row[column])).join(',')),
  ];

  await writeFile(reportPath, `${lines.join('\n')}\n`, 'utf8');
}

async function verifyRandomSamples(outputDir, sampleSize = 10) {
  const files = (await readdir(outputDir))
    .filter((name) => name.endsWith('.jpg'))
    .sort()
    .slice(0, sampleSize);

  const checks = [];

  for (const file of files) {
    const filePath = path.join(outputDir, file);
    const isJpeg = await inspectJpeg(filePath);
    checks.push({
      file,
      isJpeg,
    });
  }

  return checks;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const email = requireOption(args.email ?? process.env.MAGICPRO_EMAIL, 'email / MAGICPRO_EMAIL');
  const password = requireOption(args.password ?? process.env.MAGICPRO_PASSWORD, 'password / MAGICPRO_PASSWORD');
  const outputDir = args.output ?? process.env.MAGICPRO_OUTPUT_DIR ?? DEFAULT_OUTPUT_DIR;
  const concurrency = Number(args.concurrency ?? process.env.MAGICPRO_CONCURRENCY ?? DEFAULT_CONCURRENCY);
  const limit = args.limit == null && process.env.MAGICPRO_LIMIT == null
    ? null
    : Number(args.limit ?? process.env.MAGICPRO_LIMIT);

  if (!Number.isFinite(concurrency) || concurrency <= 0) {
    throw new Error('并发数必须是正整数');
  }

  if (limit != null && (!Number.isFinite(limit) || limit <= 0)) {
    throw new Error('limit 必须是正整数');
  }

  await mkdir(outputDir, { recursive: true });

  // 临时目录只用于中转原图和格式转换，避免把中间文件留在目标目录里。
  const tempDir = await mkdtemp(path.join(tmpdir(), 'magicpro-images-'));

  try {
    const cookieHeader = await login(email, password);
    const [products, categories] = await Promise.all([
      fetchProducts(cookieHeader),
      fetchCategories(cookieHeader),
    ]);
    const typeLookup = buildTypeLookup(categories);
    const hydratedProducts = hydrateProducts(products, typeLookup);
    const seenSkus = new Set();

    // 先按货号排序，保证重复货号时保留结果稳定。
    const orderedProducts = [...hydratedProducts].sort((left, right) => {
      const leftKey = sanitizeSku(left.identifier, left._id);
      const rightKey = sanitizeSku(right.identifier, right._id);
      return leftKey.localeCompare(rightKey) || String(left._id).localeCompare(String(right._id));
    });
    const selectedProducts = limit == null ? orderedProducts : orderedProducts.slice(0, limit);

    const rows = await runWithConcurrency(
      selectedProducts,
      async (product) => downloadOneProduct(product, outputDir, tempDir, seenSkus),
      concurrency,
    );

    const reportPath = path.join(outputDir, 'download-report.csv');
    await writeReport(reportPath, rows);

    const sampleChecks = await verifyRandomSamples(outputDir, 10);
    const summary = {
      totalProducts: orderedProducts.length,
      selectedProducts: selectedProducts.length,
      uniqueSkuSaved: rows.filter((row) => row.status === 'success').length,
      skippedDuplicates: rows.filter((row) => row.status === 'skipped').length,
      failed: rows.filter((row) => row.status === 'failed').length,
      reportPath,
      outputDir,
      sampleChecks,
    };

    console.log(JSON.stringify(summary, null, 2));
  } finally {
    await rm(tempDir, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
