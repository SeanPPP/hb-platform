#!/usr/bin/env node

import { access, mkdir, mkdtemp, readFile, readdir, rm, unlink, writeFile } from 'node:fs/promises';
import { constants } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

const BOOMUP_BASE_URL = 'https://boomup.com.au';
const BOOMUP_LOGIN_URL = `${BOOMUP_BASE_URL}/my-account/`;
const BOOMUP_PRODUCTS_API_URL = `${BOOMUP_BASE_URL}/wp-json/wp/v2/product`;
const DEFAULT_OUTPUT_DIR = '/Users/sean/Downloads/boomup-product-images';
const DEFAULT_CONCURRENCY = 6;
const DEFAULT_PAGE_SIZE = 100;
const DEFAULT_REQUEST_TIMEOUT_MS = 30_000;

function parseArgs(argv) {
  const options = {
    headless: false,
    resume: true,
  };

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
      case 'output-dir':
      case 'concurrency':
      case 'limit':
        options[key] = inlineValue ?? nextValue;
        if (inlineValue == null) {
          index += 1;
        }
        break;
      case 'headless':
        options.headless = true;
        break;
      case 'resume':
        options.resume = true;
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

function sanitizeSku(rawSku) {
  const sku = String(rawSku ?? '').trim();

  if (!sku) {
    return '';
  }

  return sku.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_');
}

function decodeHtmlEntities(value) {
  return String(value ?? '')
    .replace(/&#(\d+);/g, (_, code) => String.fromCodePoint(Number(code)))
    .replace(/&#x([0-9a-f]+);/gi, (_, code) => String.fromCodePoint(Number.parseInt(code, 16)))
    .replace(/&quot;/g, '"')
    .replace(/&#039;/g, "'")
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&nbsp;/g, ' ');
}

function escapeCsv(value) {
  const normalized = value == null ? '' : String(value);
  return `"${normalized.replace(/"/g, '""')}"`;
}

async function fetchWithTimeout(url, options = {}) {
  const timeoutMs = Number(options.timeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS);
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  try {
    return await fetch(url, {
      ...options,
      signal: controller.signal,
    });
  } catch (error) {
    if (error?.name === 'AbortError') {
      throw new Error(`请求超时: ${url}`);
    }

    throw error;
  } finally {
    clearTimeout(timeout);
  }
}

function buildCookieEntries(headers) {
  const rawCookies = typeof headers.getSetCookie === 'function'
    ? headers.getSetCookie()
    : headers.get('set-cookie')
      ? [headers.get('set-cookie')]
      : [];

  return rawCookies
    .map((cookie) => cookie.split(';')[0]?.trim())
    .filter(Boolean)
    .map((cookie) => {
      const separatorIndex = cookie.indexOf('=');
      if (separatorIndex === -1) {
        return null;
      }

      return {
        name: cookie.slice(0, separatorIndex),
        value: cookie.slice(separatorIndex + 1),
      };
    })
    .filter(Boolean);
}

function mergeCookieHeader(currentCookieHeader, headers) {
  const cookieMap = new Map();

  for (const part of String(currentCookieHeader ?? '').split(';')) {
    const trimmed = part.trim();
    if (!trimmed) {
      continue;
    }

    const separatorIndex = trimmed.indexOf('=');
    if (separatorIndex === -1) {
      continue;
    }

    cookieMap.set(trimmed.slice(0, separatorIndex), trimmed.slice(separatorIndex + 1));
  }

  for (const entry of buildCookieEntries(headers)) {
    cookieMap.set(entry.name, entry.value);
  }

  return Array.from(cookieMap.entries())
    .map(([name, value]) => `${name}=${value}`)
    .join('; ');
}

async function fetchText(url, options = {}) {
  const response = await fetchWithTimeout(url, options);
  const text = await response.text();

  return {
    response,
    text,
  };
}

function matchInputValue(html, name) {
  const pattern = new RegExp(`name="${name}"\\s+value="([^"]+)"`);
  return html.match(pattern)?.[1] ?? null;
}

async function login(email, password) {
  const loginPage = await fetchText(BOOMUP_LOGIN_URL, {
    redirect: 'manual',
  });
  const loginNonce = matchInputValue(loginPage.text, 'woocommerce-login-nonce');

  if (!loginNonce) {
    throw new Error('登录页未找到 woocommerce-login-nonce');
  }

  let cookieHeader = mergeCookieHeader('', loginPage.response.headers);
  const formBody = new URLSearchParams({
    username: email,
    password,
    rememberme: 'forever',
    'woocommerce-login-nonce': loginNonce,
    _wp_http_referer: '/my-account/',
    login: 'Log in',
  });

  const loginResponse = await fetchText(BOOMUP_LOGIN_URL, {
    method: 'POST',
    headers: {
      'content-type': 'application/x-www-form-urlencoded',
      cookie: cookieHeader,
      referer: BOOMUP_LOGIN_URL,
    },
    body: formBody.toString(),
    redirect: 'manual',
  });
  cookieHeader = mergeCookieHeader(cookieHeader, loginResponse.response.headers);

  const location = loginResponse.response.headers.get('location') ?? BOOMUP_LOGIN_URL;
  const verifyResponse = await fetchText(new URL(location, BOOMUP_BASE_URL), {
    headers: {
      cookie: cookieHeader,
      referer: BOOMUP_LOGIN_URL,
    },
    redirect: 'manual',
  });
  cookieHeader = mergeCookieHeader(cookieHeader, verifyResponse.response.headers);

  if (!verifyResponse.text.includes('woocommerce-MyAccount-navigation') || !verifyResponse.text.includes('customer-logout')) {
    throw new Error('登录后未进入账号页，可能账号或密码不正确');
  }

  return cookieHeader;
}

async function fetchProductsPage(page) {
  const url = new URL(BOOMUP_PRODUCTS_API_URL);
  url.searchParams.set('per_page', String(DEFAULT_PAGE_SIZE));
  url.searchParams.set('page', String(page));
  url.searchParams.set('_embed', 'wp:featuredmedia');

  const response = await fetchWithTimeout(url, {
    headers: {
      accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`拉取商品列表失败: page=${page} HTTP ${response.status}`);
  }

  const totalPages = Number(response.headers.get('x-wp-totalpages') ?? '1');
  const products = await response.json();

  return {
    totalPages,
    products,
  };
}

async function fetchAllProducts(limit) {
  const firstPage = await fetchProductsPage(1);
  const allProducts = [...firstPage.products];
  const totalPages = firstPage.totalPages;
  console.error(`商品列表: 已读取第 1/${totalPages} 页，累计 ${allProducts.length} 个商品`);

  if (limit != null && allProducts.length >= limit) {
    return allProducts.slice(0, limit);
  }

  for (let page = 2; page <= totalPages; page += 1) {
    const current = await fetchProductsPage(page);
    allProducts.push(...current.products);
    console.error(`商品列表: 已读取第 ${page}/${totalPages} 页，累计 ${allProducts.length} 个商品`);

    if (limit != null && allProducts.length >= limit) {
      return allProducts.slice(0, limit);
    }
  }

  return allProducts;
}

function chooseApiImageUrl(product) {
  const media = product?._embedded?.['wp:featuredmedia']?.[0];
  const full = media?.media_details?.sizes?.full?.source_url;

  if (full) {
    return full;
  }

  if (media?.source_url) {
    return media.source_url;
  }

  return '';
}

function parseSkuFromHtml(html) {
  const inlineSku = html.match(/<span class="sku">([\s\S]*?)<\/span>/i)?.[1];
  if (inlineSku) {
    return sanitizeSku(decodeHtmlEntities(inlineSku).trim());
  }

  const ldJsonSku = html.match(/"sku":"([^"]+)"/i)?.[1];
  if (ldJsonSku) {
    return sanitizeSku(decodeHtmlEntities(ldJsonSku).trim());
  }

  const addToCartSku = html.match(/data-product_sku="([^"]+)"/i)?.[1];
  if (addToCartSku) {
    return sanitizeSku(decodeHtmlEntities(addToCartSku).trim());
  }

  return '';
}

function parseMainImageFromHtml(html) {
  const largeImage = html.match(/data-large_image="([^"]+)"/i)?.[1];
  if (largeImage) {
    return decodeHtmlEntities(largeImage);
  }

  const anchorImage = html.match(/woocommerce-product-gallery__image"><a href="([^"]+)"/i)?.[1];
  if (anchorImage) {
    return decodeHtmlEntities(anchorImage);
  }

  const ldJsonImage = html.match(/"image":"([^"]+)"/i)?.[1];
  if (ldJsonImage) {
    return decodeHtmlEntities(ldJsonImage.replace(/\\\//g, '/'));
  }

  return '';
}

function normalizeProductTitle(product) {
  return decodeHtmlEntities(product?.title?.rendered ?? '').trim();
}

async function fetchProductMetadata(product, cookieHeader) {
  const response = await fetchText(product.link, {
    headers: {
      cookie: cookieHeader,
      referer: BOOMUP_LOGIN_URL,
    },
    redirect: 'manual',
  });
  const html = response.text;
  const sku = parseSkuFromHtml(html);
  const imageUrl = chooseApiImageUrl(product) || parseMainImageFromHtml(html);
  const title = normalizeProductTitle(product);

  return {
    productUrl: product.link,
    title,
    slug: product.slug ?? '',
    sku,
    imageUrl,
    loginExpired: html.includes('woocommerce-form-login') && html.includes('woocommerce-login-nonce'),
  };
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

function resolveMetadataRows(rows) {
  const skuFirstIndex = new Map();

  return rows.map((row, index) => {
    if (row.error) {
      return row;
    }

    if (row.loginExpired) {
      return {
        ...row,
        error: 'login_expired',
      };
    }

    if (!row.sku) {
      return {
        ...row,
        error: 'missing_sku',
      };
    }

    if (!row.imageUrl) {
      return {
        ...row,
        error: 'missing_main_image',
      };
    }

    if (skuFirstIndex.has(row.sku)) {
      return {
        ...row,
        error: `duplicate_sku:first_seen_at_index_${skuFirstIndex.get(row.sku)}`,
      };
    }

    skuFirstIndex.set(row.sku, index);
    return row;
  });
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

async function downloadOneImage(row, imagesDir, tempDir, resume) {
  const targetPath = path.join(imagesDir, `${row.sku}.jpg`);

  if (resume && await fileExists(targetPath)) {
    return {
      ...row,
      savedPath: targetPath,
      status: 'already_exists',
    };
  }

  const tempSourcePath = path.join(tempDir, `${row.sku}.source`);

  try {
    const response = await fetchWithTimeout(row.imageUrl);

    if (!response.ok || !response.body) {
      throw new Error(`图片下载失败: HTTP ${response.status}`);
    }

    await streamToFile(response.body, tempSourcePath);
    await convertToJpeg(tempSourcePath, targetPath);

    return {
      ...row,
      savedPath: targetPath,
      status: 'downloaded',
    };
  } finally {
    await unlink(tempSourcePath).catch(() => {});
  }
}

async function writeCsv(filePath, header, rows) {
  const lines = [
    header.join(','),
    ...rows.map((row) => header.map((column) => escapeCsv(row[column])).join(',')),
  ];

  await writeFile(filePath, `${lines.join('\n')}\n`, 'utf8');
}

async function verifyRandomSamples(imagesDir, sampleSize = 10) {
  const files = (await readdir(imagesDir))
    .filter((name) => name.endsWith('.jpg'))
    .sort()
    .slice(0, sampleSize);

  const checks = [];

  for (const file of files) {
    const filePath = path.join(imagesDir, file);
    checks.push({
      file,
      isJpeg: await inspectJpeg(filePath),
    });
  }

  return checks;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const email = requireOption(args.email ?? process.env.BOOMUP_EMAIL, 'email / BOOMUP_EMAIL');
  const password = requireOption(args.password ?? process.env.BOOMUP_PASSWORD, 'password / BOOMUP_PASSWORD');
  const outputDir = args['output-dir'] ?? process.env.BOOMUP_OUTPUT_DIR ?? DEFAULT_OUTPUT_DIR;
  const concurrency = Number(args.concurrency ?? process.env.BOOMUP_CONCURRENCY ?? DEFAULT_CONCURRENCY);
  const limit = args.limit == null && process.env.BOOMUP_LIMIT == null
    ? null
    : Number(args.limit ?? process.env.BOOMUP_LIMIT);
  const resume = args.resume ?? true;

  if (!Number.isFinite(concurrency) || concurrency <= 0) {
    throw new Error('并发数必须是正整数');
  }

  if (limit != null && (!Number.isFinite(limit) || limit <= 0)) {
    throw new Error('limit 必须是正整数');
  }

  const imagesDir = path.join(outputDir, 'images');
  await mkdir(imagesDir, { recursive: true });

  // 临时目录只存放原图下载中间文件，避免把半成品留在输出目录中。
  const tempDir = await mkdtemp(path.join(tmpdir(), 'boomup-images-'));

  try {
    // 当前实现直接走 HTTP 会话抓取，保留 headless 参数只是为了兼容约定好的命令接口。
    console.error('登录: 开始建立 WooCommerce 会话');
    const cookieHeader = await login(email, password);
    console.error('登录: 成功');
    const products = await fetchAllProducts(limit);

    if (products.length === 0) {
      throw new Error('未拉取到任何商品');
    }

    // 先抓元数据再处理重复 SKU，避免并发下载时出现抢占同名文件的竞态。
    let metadataCompleted = 0;
    const metadataRows = await runWithConcurrency(
      products,
      async (product) => {
        try {
          return await fetchProductMetadata(product, cookieHeader);
        } catch (error) {
          return {
            productUrl: product.link,
            title: normalizeProductTitle(product),
            slug: product.slug ?? '',
            sku: '',
            imageUrl: chooseApiImageUrl(product),
            error: error instanceof Error ? error.message : String(error),
          };
        } finally {
          metadataCompleted += 1;
          if (metadataCompleted % 50 === 0 || metadataCompleted === products.length) {
            console.error(`详情页: 已读取 ${metadataCompleted}/${products.length}`);
          }
        }
      },
      concurrency,
    );
    const resolvedRows = resolveMetadataRows(metadataRows);

    const errorRows = resolvedRows
      .filter((row) => row.error)
      .map((row) => ({
        product_url: row.productUrl,
        sku: row.sku,
        reason: row.error,
      }));

    const downloadCandidates = resolvedRows.filter((row) => !row.error);
    let downloadCompleted = 0;
    const downloadResults = await runWithConcurrency(
      downloadCandidates,
      async (row) => {
        try {
          return await downloadOneImage(row, imagesDir, tempDir, resume);
        } catch (error) {
          errorRows.push({
            product_url: row.productUrl,
            sku: row.sku,
            reason: error instanceof Error ? error.message : String(error),
          });
          return null;
        } finally {
          downloadCompleted += 1;
          if (downloadCompleted % 50 === 0 || downloadCompleted === downloadCandidates.length) {
            console.error(`图片: 已处理 ${downloadCompleted}/${downloadCandidates.length}`);
          }
        }
      },
      concurrency,
    );

    const successRows = downloadResults
      .filter(Boolean)
      .map((row) => ({
        sku: row.sku,
        product_url: row.productUrl,
        image_url: row.imageUrl,
        saved_path: row.savedPath,
      }));

    const manifestPath = path.join(outputDir, 'manifest.csv');
    const errorsPath = path.join(outputDir, 'errors.csv');
    await writeCsv(manifestPath, ['sku', 'product_url', 'image_url', 'saved_path'], successRows);
    await writeCsv(errorsPath, ['product_url', 'sku', 'reason'], errorRows);

    const sampleChecks = await verifyRandomSamples(imagesDir, 10);
    const summary = {
      totalProducts: products.length,
      manifestCount: successRows.length,
      downloadedCount: downloadResults.filter((row) => row?.status === 'downloaded').length,
      alreadyExistsCount: downloadResults.filter((row) => row?.status === 'already_exists').length,
      errorCount: errorRows.length,
      outputDir,
      imagesDir,
      manifestPath,
      errorsPath,
      sampleChecks,
      interfaceOptions: {
        headless: Boolean(args.headless),
        resume,
      },
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
