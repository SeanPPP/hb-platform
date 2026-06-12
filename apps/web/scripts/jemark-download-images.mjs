#!/usr/bin/env node

import { access, mkdir, mkdtemp, readdir, rm, unlink, writeFile } from 'node:fs/promises';
import { constants } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';

const JEMARK_BASE_URL = 'https://www.jemark.com.au';
const JEMARK_LOGIN_URL = `${JEMARK_BASE_URL}/login`;
const JEMARK_ACCOUNT_URL = `${JEMARK_BASE_URL}/account`;
const JEMARK_CATEGORY_URL = `${JEMARK_BASE_URL}/category`;
const JEMARK_DOOFINDER_HASHID = 'd2c4ee8952227613441a9cebf8c1626e';
const JEMARK_DOOFINDER_URL = `https://us1-search.doofinder.com/6/${JEMARK_DOOFINDER_HASHID}/_search`;
const DEFAULT_OUTPUT_DIR = '/Users/sean/Downloads/jemark-product-images';
const DEFAULT_CONCURRENCY = 2;
const DOOFINDER_PAGE_SIZE = 100;
const DOOFINDER_MAX_PAGE = 10;
const DOOFINDER_DIRECT_FETCH_LIMIT = DOOFINDER_PAGE_SIZE * DOOFINDER_MAX_PAGE;
const DEFAULT_HEADERS = {
  'user-agent': 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36',
  accept: 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8',
  'accept-language': 'en-AU,en;q=0.9',
};

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

function setCookiesFromResponse(response) {
  if (typeof response.headers.getSetCookie !== 'function') {
    return [];
  }

  return response.headers.getSetCookie().map((cookie) => cookie.split(';')[0]?.trim()).filter(Boolean);
}

function mergeCookies(...cookieParts) {
  const cookieMap = new Map();

  for (const part of cookieParts) {
    const tokens = Array.isArray(part) ? part : String(part ?? '').split(/;\s*/);

    for (const token of tokens) {
      if (!token) {
        continue;
      }

      const separatorIndex = token.indexOf('=');
      if (separatorIndex < 0) {
        continue;
      }

      const key = token.slice(0, separatorIndex).trim();
      const value = token.slice(separatorIndex + 1).trim();

      if (key) {
        cookieMap.set(key, value);
      }
    }
  }

  return Array.from(cookieMap.entries()).map(([key, value]) => `${key}=${value}`).join('; ');
}

function decodeHtmlEntities(value) {
  return String(value ?? '')
    .replace(/&amp;/g, '&')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&nbsp;/g, ' ')
    .trim();
}

function stripTags(value) {
  return decodeHtmlEntities(String(value ?? '').replace(/<[^>]+>/g, ' ')).replace(/\s+/g, ' ').trim();
}

function titleFromHtml(html) {
  const match = html.match(/<title>([^<]*)<\/title>/i);
  return decodeHtmlEntities(match?.[1] ?? '');
}

function csrfFromHtml(html) {
  const match = html.match(/name="_csrf_token"\s+value="([^"]+)"/i);
  return match?.[1] ?? '';
}

function absoluteUrl(url) {
  if (!url) {
    return '';
  }

  if (url.startsWith('http://') || url.startsWith('https://')) {
    return url;
  }

  return `${JEMARK_BASE_URL}/${url.replace(/^\/+/, '')}`;
}

function sanitizeSku(rawSku, fallbackId) {
  const sku = decodeHtmlEntities(rawSku).trim();

  if (!sku) {
    return `missing-sku-${fallbackId}`;
  }

  return sku.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_');
}

function escapeCsv(value) {
  const normalized = value == null ? '' : String(value);
  return `"${normalized.replace(/"/g, '""')}"`;
}

function delay(ms) {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

async function fetchText(url, cookieHeader, init = {}) {
  const maxAttempts = init.maxAttempts ?? 4;
  const retryableStatuses = new Set([403, 408, 425, 429, 500, 502, 503, 504]);
  let lastResponse = null;
  let lastText = '';

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const response = await fetch(url, {
      ...init,
      headers: {
        ...DEFAULT_HEADERS,
        ...(cookieHeader ? { cookie: cookieHeader } : {}),
        ...(init.headers ?? {}),
      },
      redirect: init.redirect ?? 'manual',
    });
    const text = await response.text();

    lastResponse = response;
    lastText = text;

    if (!retryableStatuses.has(response.status) || attempt === maxAttempts) {
      return {
        response,
        text,
      };
    }

    // 403/429 在 Jemark 上更像临时风控，退避后继续比立刻并发重试更稳。
    await delay(1000 * attempt);
  }

  return {
    response: lastResponse,
    text: lastText,
  };
}

async function login(email, password) {
  const loginPage = await fetchText(JEMARK_LOGIN_URL);
  if (!loginPage.response.ok) {
    throw new Error(`打开登录页失败: HTTP ${loginPage.response.status}`);
  }

  const csrfToken = csrfFromHtml(loginPage.text);
  if (!csrfToken) {
    throw new Error('登录页未找到 CSRF token');
  }

  let cookieHeader = mergeCookies(setCookiesFromResponse(loginPage.response));
  const body = new URLSearchParams({
    _csrf_token: csrfToken,
    email,
    password,
  });

  const loginResponse = await fetch(JEMARK_LOGIN_URL, {
    method: 'POST',
    headers: {
      ...DEFAULT_HEADERS,
      cookie: cookieHeader,
      referer: JEMARK_LOGIN_URL,
      'content-type': 'application/x-www-form-urlencoded',
    },
    body,
    redirect: 'manual',
  });

  cookieHeader = mergeCookies(cookieHeader, setCookiesFromResponse(loginResponse));
  if (!cookieHeader) {
    throw new Error('登录后未拿到有效 cookie');
  }

  const accountPage = await fetchText(JEMARK_ACCOUNT_URL, cookieHeader);
  if (!accountPage.response.ok) {
    throw new Error(`登录后验证账号页失败: HTTP ${accountPage.response.status}`);
  }

  const accountTitle = titleFromHtml(accountPage.text);
  if (!/my account/i.test(accountTitle)) {
    throw new Error(`登录后未进入账号页，当前标题: ${accountTitle || '未知'}`);
  }

  return cookieHeader;
}

async function fetchBinary(url, init = {}) {
  const maxAttempts = init.maxAttempts ?? 4;
  const retryableStatuses = new Set([403, 408, 425, 429, 500, 502, 503, 504]);
  let lastError = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const response = await fetch(url, init);
    if (response.ok && response.body) {
      return response;
    }

    lastError = new Error(`图片下载失败: HTTP ${response.status}`);
    if (!retryableStatuses.has(response.status) || attempt === maxAttempts) {
      throw lastError;
    }

    await delay(1000 * attempt);
  }

  throw lastError ?? new Error('图片下载失败');
}

async function fetchJson(url, init = {}) {
  const maxAttempts = init.maxAttempts ?? 4;
  const retryableStatuses = new Set([403, 408, 425, 429, 500, 502, 503, 504]);
  let lastResponse = null;
  let lastText = '';

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const response = await fetch(url, {
      ...init,
      headers: {
        ...DEFAULT_HEADERS,
        accept: 'application/json,text/plain,*/*',
        ...(init.headers ?? {}),
      },
    });
    const text = await response.text();

    lastResponse = response;
    lastText = text;

    if (response.ok) {
      return JSON.parse(text);
    }

    if (!retryableStatuses.has(response.status) || attempt === maxAttempts) {
      throw new Error(`JSON 请求失败: HTTP ${response.status} ${text.slice(0, 200)}`);
    }

    await delay(1000 * attempt);
  }

  throw new Error(`JSON 请求失败: HTTP ${lastResponse?.status ?? 'unknown'} ${lastText.slice(0, 200)}`);
}

function appendMultiValueParams(params, prefix, values) {
  for (const value of values ?? []) {
    params.append(`${prefix}[]`, value);
  }
}

function buildDoofinderSearchUrl({
  query = ' ',
  page = 1,
  rpp = DOOFINDER_PAGE_SIZE,
  filters = {},
  excludes = {},
  facets = [],
}) {
  const params = new URLSearchParams({
    query,
    page: String(page),
    rpp: String(rpp),
  });

  for (const [field, values] of Object.entries(filters)) {
    appendMultiValueParams(params, `filter[${field}]`, values);
  }

  for (const [field, values] of Object.entries(excludes)) {
    appendMultiValueParams(params, `exclude[${field}]`, values);
  }

  facets.forEach((facet, index) => {
    params.append(`facets[${index}][field]`, facet.field);
    params.append(`facets[${index}][size]`, String(facet.size ?? 50));
  });

  return `${JEMARK_DOOFINDER_URL}?${params.toString()}`;
}

async function fetchDoofinderSearch(options) {
  return fetchJson(buildDoofinderSearchUrl(options), {
    headers: {
      origin: JEMARK_BASE_URL,
      referer: `${JEMARK_BASE_URL}/`,
    },
  });
}

function extractLinks(html, regex) {
  return [...html.matchAll(/href="([^"]+)"/gi)]
    .map((match) => absoluteUrl(match[1]))
    .filter((url) => regex.test(url));
}

function extractCategorySummary(html) {
  const match = html.match(/<span class="start">(\d+)<\/span>\s*to\s*<span class="end">(\d+)<\/span>\s*of\s*<span class="total">(\d+)<\/span>/i);
  if (!match) {
    return null;
  }

  return {
    start: Number(match[1]),
    end: Number(match[2]),
    total: Number(match[3]),
  };
}

async function discoverProductUrls(cookieHeader) {
  const rootPage = await fetchText(JEMARK_CATEGORY_URL, cookieHeader);
  if (!rootPage.response.ok) {
    throw new Error(`打开分类根页失败: HTTP ${rootPage.response.status}`);
  }

  const queue = [...new Set(extractLinks(rootPage.text, /^https:\/\/www\.jemark\.com\.au\/category\/\d+/))];
  const seenCategories = new Set(queue);
  const seenProducts = new Set();
  const categorySummaries = [];

  for (let index = 0; index < queue.length; index += 1) {
    const categoryUrl = queue[index];
    const page = await fetchText(categoryUrl, cookieHeader);

    if (!page.response.ok) {
      categorySummaries.push({
        url: categoryUrl,
        status: 'failed',
        note: `HTTP ${page.response.status}`,
      });
      continue;
    }

    const childCategoryLinks = extractLinks(page.text, /^https:\/\/www\.jemark\.com\.au\/category\/\d+/);
    for (const childLink of childCategoryLinks) {
      if (!seenCategories.has(childLink)) {
        seenCategories.add(childLink);
        queue.push(childLink);
      }
    }

    const productLinks = extractLinks(page.text, /^https:\/\/www\.jemark\.com\.au\/product\/\d+-/);
    for (const productLink of productLinks) {
      seenProducts.add(productLink);
    }

    const summary = extractCategorySummary(page.text);
    categorySummaries.push({
      url: categoryUrl,
      status: 'success',
      results: summary?.total ?? productLinks.length,
      hasPagination: summary != null && summary.total > (summary.end - summary.start + 1),
    });
  }

  return {
    categorySummaries,
    productUrls: [...seenProducts].sort(),
  };
}

function normalizeDoofinderItem(item) {
  return {
    source: 'doofinder',
    productId: String(item.link ?? item.id ?? '').match(/\/product\/(\d+)-/)?.[1] ?? String(item.id ?? 'unknown'),
    productName: decodeHtmlEntities(item.title ?? item.id ?? ''),
    productUrl: absoluteUrl(item.link ?? ''),
    sku: sanitizeSku(item.id, item.link ?? 'unknown'),
    imageUrl: absoluteUrl(item.image_link ?? ''),
    status: item.image_link ? 'ready' : 'failed',
    note: item.image_link ? '' : '索引里未提供主图 URL',
  };
}

async function fetchDoofinderFacetTerms({ filters = {}, excludes = {}, field, size = 50 }) {
  const response = await fetchDoofinderSearch({
    page: 1,
    rpp: 1,
    filters,
    excludes,
    facets: [{ field, size }],
  });

  return {
    total: Number(response.total ?? 0),
    items: response.facets?.[0]?.terms?.items ?? [],
  };
}

async function fetchDoofinderPartitionProducts(partition, limit) {
  const total = Math.min(partition.expectedTotal, limit == null ? partition.expectedTotal : limit);
  const pagesNeeded = Math.ceil(total / DOOFINDER_PAGE_SIZE);
  const results = [];

  // Doofinder 要求 rpp * page <= 1000，因此每个分片都要控制在 10 页以内。
  for (let page = 1; page <= pagesNeeded; page += 1) {
    const response = await fetchDoofinderSearch({
      page,
      rpp: DOOFINDER_PAGE_SIZE,
      filters: partition.filters,
      excludes: partition.excludes,
    });
    results.push(...(response.results ?? []));
    await delay(150);
  }

  return results.slice(0, total).map(normalizeDoofinderItem);
}

async function buildDoofinderPartitions(totalLimit = null) {
  const brandFacet = await fetchDoofinderFacetTerms({ field: 'brand' });
  const partitions = [];
  let remainingLimit = totalLimit;
  const knownBrands = brandFacet.items.map((item) => item.name).filter(Boolean);

  for (const brandTerm of brandFacet.items) {
    if (remainingLimit != null && remainingLimit <= 0) {
      break;
    }

    const brandFilters = { brand: [brandTerm.name] };
    const brandCount = Number(brandTerm.count ?? 0);

    if (brandCount <= DOOFINDER_DIRECT_FETCH_LIMIT) {
      const expectedTotal = remainingLimit == null ? brandCount : Math.min(brandCount, remainingLimit);
      partitions.push({
        filters: brandFilters,
        excludes: {},
        expectedTotal,
        note: `brand:${brandTerm.name}`,
      });
      if (remainingLimit != null) {
        remainingLimit -= expectedTotal;
      }
      continue;
    }

    const seenSubCategories = new Set();
    let remainingBrandCount = brandCount;

    while (remainingBrandCount > 0) {
      const excludes = {
        'sub categories': [...seenSubCategories],
      };
      const subCategoryFacet = await fetchDoofinderFacetTerms({
        filters: brandFilters,
        excludes,
        field: 'sub categories',
      });
      const subCategoryTerms = subCategoryFacet.items ?? [];

      if (subCategoryTerms.length === 0) {
        const expectedTotal = remainingLimit == null ? remainingBrandCount : Math.min(remainingBrandCount, remainingLimit);
        partitions.push({
          filters: brandFilters,
          excludes,
          expectedTotal,
          note: `brand:${brandTerm.name}:remainder`,
        });
        if (remainingLimit != null) {
          remainingLimit -= expectedTotal;
        }
        break;
      }

      let coveredCount = 0;
      for (const subCategoryTerm of subCategoryTerms) {
        if (remainingLimit != null && remainingLimit <= 0) {
          break;
        }

        const subCategoryCount = Number(subCategoryTerm.count ?? 0);
        coveredCount += subCategoryCount;
        seenSubCategories.add(subCategoryTerm.name);

        const expectedTotal = remainingLimit == null ? subCategoryCount : Math.min(subCategoryCount, remainingLimit);
        partitions.push({
          filters: {
            ...brandFilters,
            'sub categories': [subCategoryTerm.name],
          },
          excludes: {},
          expectedTotal,
          note: `brand:${brandTerm.name}:sub-category:${subCategoryTerm.name}`,
        });
        if (remainingLimit != null) {
          remainingLimit -= expectedTotal;
        }
      }

      remainingBrandCount = Math.max(0, remainingBrandCount - coveredCount);
      if (subCategoryTerms.length < 50 || (remainingLimit != null && remainingLimit <= 0)) {
        if (remainingBrandCount > 0 && (remainingLimit == null || remainingLimit > 0)) {
          const expectedTotal = remainingLimit == null ? remainingBrandCount : Math.min(remainingBrandCount, remainingLimit);
          partitions.push({
            filters: brandFilters,
            excludes: {
              'sub categories': [...seenSubCategories],
            },
            expectedTotal,
            note: `brand:${brandTerm.name}:tail`,
          });
          if (remainingLimit != null) {
            remainingLimit -= expectedTotal;
          }
        }
        break;
      }
    }
  }

  const knownBrandTotal = brandFacet.items.reduce((sum, item) => sum + Number(item.count ?? 0), 0);
  const uncategorizedBrandTotal = Math.max(0, Number(brandFacet.total ?? 0) - knownBrandTotal);

  if (uncategorizedBrandTotal > 0 && (remainingLimit == null || remainingLimit > 0)) {
    const blankBrandFacet = await fetchDoofinderFacetTerms({
      excludes: {
        brand: knownBrands,
      },
      field: 'categories',
    });

    for (const categoryTerm of blankBrandFacet.items) {
      if (remainingLimit != null && remainingLimit <= 0) {
        break;
      }

      const expectedTotal = remainingLimit == null
        ? Number(categoryTerm.count ?? 0)
        : Math.min(Number(categoryTerm.count ?? 0), remainingLimit);
      partitions.push({
        filters: {
          categories: [categoryTerm.name],
        },
        excludes: {
          brand: knownBrands,
        },
        expectedTotal,
        note: `blank-brand:category:${categoryTerm.name}`,
      });
      if (remainingLimit != null) {
        remainingLimit -= expectedTotal;
      }
    }
  }

  return partitions;
}

async function fetchDoofinderProducts(limit, concurrency) {
  const partitions = await buildDoofinderPartitions(limit);
  const products = [];

  for (const partition of partitions) {
    if (partition.expectedTotal <= 0) {
      continue;
    }

    const partitionProducts = await fetchDoofinderPartitionProducts(partition, partition.expectedTotal);
    products.push(...partitionProducts);

    if (limit != null && products.length >= limit) {
      break;
    }

    // 分片之间也稍作停顿，避免连续命中过多索引请求。
    await delay(Math.max(200, 800 / Math.max(1, concurrency)));
  }

  return products.slice(0, limit == null ? products.length : limit);
}

function parseProductPage(html, productUrl) {
  const titleMatch = html.match(/<div id="product-title">[\s\S]*?<h1>([\s\S]*?)<\/h1>/i);
  const title = stripTags(titleMatch?.[1] ?? '');

  const skuMatch = html.match(/<div class="model-label">Stock Code:<\/div>\s*<div class="value">([\s\S]*?)<\/div>/i);
  const rawSku = stripTags(skuMatch?.[1] ?? '');

  const imageMatch =
    html.match(/data-zoom-image="([^"]+)"/i) ??
    html.match(/xlarge="([^"]+)"/i) ??
    html.match(/<div class="button-outer enlarge"><a class="btn" href="([^"]+)"/i);

  const imageUrl = absoluteUrl(imageMatch?.[1] ?? '');
  const productIdMatch = productUrl.match(/\/product\/(\d+)-/);
  const fallbackId = productIdMatch?.[1] ?? 'unknown';

  return {
    productId: fallbackId,
    productName: title || fallbackId,
    sku: sanitizeSku(rawSku, fallbackId),
    imageUrl,
  };
}

async function streamToFile(readable, filePath) {
  const chunks = [];
  for await (const chunk of readable) {
    chunks.push(chunk);
  }
  await writeFile(filePath, Buffer.concat(chunks));
}

function extensionFromContentType(contentType) {
  const normalized = String(contentType ?? '').toLowerCase();
  if (normalized.includes('png')) {
    return '.png';
  }
  if (normalized.includes('webp')) {
    return '.webp';
  }
  if (normalized.includes('gif')) {
    return '.gif';
  }
  if (normalized.includes('jpeg') || normalized.includes('jpg')) {
    return '.jpg';
  }
  return '.img';
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

async function collectProductMetadata(productUrls, cookieHeader, concurrency) {
  return runWithConcurrency(productUrls, async (productUrl) => {
    const page = await fetchText(productUrl, cookieHeader);

    if (!page.response.ok) {
      return {
        productUrl,
        status: 'failed',
        note: `商品页 HTTP ${page.response.status}`,
      };
    }

    const parsed = parseProductPage(page.text, productUrl);
    if (!parsed.imageUrl) {
      return {
        productUrl,
        status: 'failed',
        note: '未解析到主图 URL',
        ...parsed,
      };
    }

    return {
      productUrl,
      status: 'ready',
      note: '',
      ...parsed,
    };
  }, concurrency);
}

async function downloadOneProduct(product, outputDir, tempDir, seenSkus) {
  const targetPath = path.join(outputDir, `${product.sku}.jpg`);
  const report = {
    sku: product.sku,
    product_name: product.productName,
    product_url: product.productUrl,
    image_url: product.imageUrl ?? '',
    saved_file: targetPath,
    status: 'failed',
    note: product.note ?? '',
  };

  if (seenSkus.has(product.sku)) {
    report.status = 'skipped';
    report.note = '重复 SKU，按规则保留第一张主图';
    return report;
  }

  if (product.status !== 'ready') {
    report.note = product.note || '商品信息未就绪';
    return report;
  }

  try {
    if (await fileExists(targetPath)) {
      seenSkus.add(product.sku);
      report.status = 'success';
      report.note = 'reused-existing';
      return report;
    }

    const response = await fetchBinary(product.imageUrl, {
      headers: {
        ...DEFAULT_HEADERS,
        accept: 'image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8',
      },
    });

    const sourceExtension = extensionFromContentType(response.headers.get('content-type'));
    const tempSourcePath = path.join(tempDir, `${product.sku}-${product.productId}${sourceExtension}`);

    await streamToFile(response.body, tempSourcePath);
    await convertToJpeg(tempSourcePath, targetPath);
    await unlink(tempSourcePath).catch(() => {});

    seenSkus.add(product.sku);
    report.status = 'success';
    report.note = 'downloaded';
    return report;
  } catch (error) {
    report.note = error instanceof Error ? error.message : String(error);
    return report;
  }
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
    checks.push({
      file,
      isJpeg: await inspectJpeg(path.join(outputDir, file)),
    });
  }

  return checks;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const email = requireOption(args.email ?? process.env.JEMARK_EMAIL, 'email / JEMARK_EMAIL');
  const password = requireOption(args.password ?? process.env.JEMARK_PASSWORD, 'password / JEMARK_PASSWORD');
  const outputDir = args.output ?? process.env.JEMARK_OUTPUT_DIR ?? DEFAULT_OUTPUT_DIR;
  const concurrency = Number(args.concurrency ?? process.env.JEMARK_CONCURRENCY ?? DEFAULT_CONCURRENCY);
  const limit = args.limit == null && process.env.JEMARK_LIMIT == null
    ? null
    : Number(args.limit ?? process.env.JEMARK_LIMIT);

  if (!Number.isFinite(concurrency) || concurrency <= 0) {
    throw new Error('并发数必须是正整数');
  }

  if (limit != null && (!Number.isFinite(limit) || limit <= 0)) {
    throw new Error('limit 必须是正整数');
  }

  await mkdir(outputDir, { recursive: true });
  const tempDir = await mkdtemp(path.join(tmpdir(), 'jemark-images-'));

  try {
    let source = 'doofinder';
    let discoveredProducts = [];
    let discoveredCategoryCount = null;
    let pagedCategoryCount = null;

    try {
      discoveredProducts = await fetchDoofinderProducts(limit, concurrency);
    } catch {
      source = 'site-crawl';
      const cookieHeader = await login(email, password);
      const discovered = await discoverProductUrls(cookieHeader);
      discoveredCategoryCount = discovered.categorySummaries.length;
      pagedCategoryCount = discovered.categorySummaries.filter((item) => item.hasPagination).length;
      const metadata = await collectProductMetadata(discovered.productUrls, cookieHeader, concurrency);
      discoveredProducts = metadata;
    }

    const selectedProducts = limit == null
      ? discoveredProducts
      : discoveredProducts.slice(0, limit);
    const seenSkus = new Set();
    const rows = await runWithConcurrency(
      selectedProducts,
      async (product) => downloadOneProduct(product, outputDir, tempDir, seenSkus),
      concurrency,
    );

    const reportPath = path.join(outputDir, 'download-report.csv');
    await writeReport(reportPath, rows);

    const sampleChecks = await verifyRandomSamples(outputDir, 10);
    const summary = {
      source,
      discoveredCategories: discoveredCategoryCount,
      discoveredProducts: discoveredProducts.length,
      selectedProducts: selectedProducts.length,
      uniqueSkuSaved: rows.filter((row) => row.status === 'success').length,
      skippedDuplicates: rows.filter((row) => row.status === 'skipped').length,
      failed: rows.filter((row) => row.status === 'failed').length,
      pagedCategories: pagedCategoryCount,
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
