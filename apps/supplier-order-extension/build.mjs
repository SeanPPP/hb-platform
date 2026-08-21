// 生成 Chrome、Edge 与 Safari 三个目标；Safari 后台用 esbuild 转为 classic service worker。
import {
  cpSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build as bundle } from 'esbuild';

const ROOT = dirname(fileURLToPath(import.meta.url));
const SRC = join(ROOT, 'src');
const DIST = join(ROOT, 'dist');

const pkg = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8'));
const VERSION = pkg.version;
function normalizeOrigin(value, name) {
  const url = new URL(value);
  const isLocalHttp = url.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(url.hostname);
  if ((url.protocol !== 'https:' && !isLocalHttp) || url.pathname !== '/' || url.search || url.hash) {
    throw new Error(`${name} 必须是 HTTPS origin（本地调试可用 localhost HTTP），不能包含路径、查询或片段`);
  }
  return url.origin;
}

// Web 与 API 可以同源，也可以分别部署；/shop 桥接必须绑定 Web 源。
const HB_API_ORIGIN = normalizeOrigin(
  process.env.HB_API_ORIGIN || 'https://hotbargain.vip',
  'HB_API_ORIGIN',
);
const HB_WEB_ORIGIN = normalizeOrigin(
  process.env.HB_WEB_ORIGIN || process.env.HB_API_ORIGIN || 'https://hotbargain.vip',
  'HB_WEB_ORIGIN',
);
const TARGETS = ['chrome', 'edge', 'safari'];
const SAFARI_BUNDLED_FILES = new Set([
  join('background', 'service-worker.js'),
  join('content', 'list.js'),
  join('content', 'shop-bridge.js'),
]);

function createSafariBundlePlugin(config) {
  return {
    name: 'hb-safari-modules',
    setup(build) {
      build.onResolve({ filter: /^\.\.\/config\.js$/ }, () => ({
        path: 'config.js',
        namespace: 'hb-safari-config',
      }));
      build.onLoad({ filter: /.*/, namespace: 'hb-safari-config' }, () => ({
        contents: config,
        loader: 'js',
      }));
      build.onLoad({ filter: /[\\/]content[\\/](list|shop-bridge)\.js$/ }, ({ path }) => {
        const source = readFileSync(path, 'utf8');
        let rewrittenImportCount = 0;
        const contents = source.replace(
          /import\(chrome\.runtime\.getURL\((['"])([^'"]+)\1\)\)/g,
          (_match, _quote, resourcePath) => {
            if (resourcePath !== 'config.js' && !/^lib\/[A-Za-z0-9._/-]+\.js$/.test(resourcePath)) {
              throw new Error(`Safari 内容脚本引用了不允许的模块路径: ${resourcePath}`);
            }
            rewrittenImportCount += 1;
            return `import(${JSON.stringify(`../${resourcePath}`)})`;
          },
        );
        if (rewrittenImportCount === 0) {
          throw new Error(`Safari 内容脚本没有可内联的运行时模块: ${path}`);
        }
        return { contents, loader: 'js' };
      });
    },
  };
}

function listCopiedSourceFiles(dir, prefix = '') {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const relativePath = prefix ? join(prefix, entry.name) : entry.name;
    if (entry.isDirectory()) return listCopiedSourceFiles(join(dir, entry.name), relativePath);
    if (
      entry.name === 'config.template.js'
      || entry.name === 'manifest.template.json'
      || entry.name === 'manifest.safari.template.json'
    ) return [];
    return [relativePath];
  });
}

const COPIED_SOURCE_FILES = listCopiedSourceFiles(SRC);
const EXTENSION_ICONS = [
  ['icon16.png', 16],
  ['icon32.png', 32],
  ['icon48.png', 48],
  ['icon128.png', 128],
];

// PNG 的 IHDR 固定包含宽高，构建前先阻止缺失或尺寸错误的商店图标进入包。
for (const [name, expectedSize] of EXTENSION_ICONS) {
  const icon = readFileSync(join(SRC, 'icons', name));
  const isPng = icon.subarray(0, 8).equals(
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  );
  const width = icon.length >= 24 ? icon.readUInt32BE(16) : 0;
  const height = icon.length >= 24 ? icon.readUInt32BE(20) : 0;
  if (!isPng || width !== expectedSize || height !== expectedSize) {
    throw new Error(`src/icons/${name} 必须是 ${expectedSize}x${expectedSize} PNG`);
  }
}

for (const target of TARGETS) {
  const out = join(DIST, target);
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });

  // 复制源码（排除配置与 manifest 模板，稍后按目标生成）
  cpSync(SRC, out, {
    recursive: true,
    filter: (src) =>
      !src.includes('config.template.js')
      && !src.includes('manifest.template.json')
      && !src.includes('manifest.safari.template.json'),
  });

  // 生成 config.js（替换构建期占位符）
  const configTpl = readFileSync(join(SRC, 'config.template.js'), 'utf8');
  const config = configTpl
    .replaceAll('"__VERSION__"', JSON.stringify(VERSION))
    .replaceAll('"__HB_API_ORIGIN__"', JSON.stringify(HB_API_ORIGIN))
    .replaceAll('"__HB_WEB_ORIGIN__"', JSON.stringify(HB_WEB_ORIGIN))
    .replaceAll('"__BUILD_TARGET__"', JSON.stringify(target));
  writeFileSync(join(out, 'config.js'), config);

  // 生成目标 manifest.json。
  const manifestTemplateName = target === 'safari'
    ? 'manifest.safari.template.json'
    : 'manifest.template.json';
  const manifestTpl = readFileSync(join(SRC, manifestTemplateName), 'utf8');
  const manifest = manifestTpl
    .replaceAll('__VERSION__', VERSION)
    .replaceAll('__API_ORIGIN__', HB_API_ORIGIN)
    .replaceAll('__WEB_ORIGIN__', HB_WEB_ORIGIN);
  writeFileSync(join(out, 'manifest.json'), manifest);
  JSON.parse(manifest); // 语法校验

  if (target === 'safari') {
    // Safari 的后台和内容脚本都打成 classic 单文件，运行时不再依赖内容脚本动态 import。
    for (const relativePath of SAFARI_BUNDLED_FILES) {
      await bundle({
        entryPoints: [join(SRC, relativePath)],
        outfile: join(out, relativePath),
        bundle: true,
        format: 'iife',
        platform: 'browser',
        target: ['safari16.4'],
        minify: false,
        legalComments: 'none',
        plugins: [createSafariBundlePlugin(config)],
      });
    }
  }

  for (const relativePath of COPIED_SOURCE_FILES) {
    if (target === 'safari' && SAFARI_BUNDLED_FILES.has(relativePath)) continue;
    const source = readFileSync(join(SRC, relativePath));
    const built = readFileSync(join(out, relativePath));
    if (!source.equals(built)) {
      throw new Error(`dist/${target}/${relativePath} 与源码不一致`);
    }
  }

  console.log(
    `built dist/${target} (v${VERSION}, web=${HB_WEB_ORIGIN}, api=${HB_API_ORIGIN})`,
  );
}

// 三个浏览器包同版本校验
const chromeManifest = JSON.parse(readFileSync(join(DIST, 'chrome', 'manifest.json'), 'utf8'));
const edgeManifest = JSON.parse(readFileSync(join(DIST, 'edge', 'manifest.json'), 'utf8'));
const safariManifest = JSON.parse(readFileSync(join(DIST, 'safari', 'manifest.json'), 'utf8'));
if (chromeManifest.version !== edgeManifest.version || chromeManifest.version !== safariManifest.version) {
  throw new Error('chrome/edge/safari 版本不一致');
}
console.log(`三包版本一致: ${chromeManifest.version}`);
