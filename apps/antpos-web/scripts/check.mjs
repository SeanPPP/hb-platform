import { access, readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const publicRoot = path.join(projectRoot, 'public');

const requiredFiles = [
  'index.html',
  'favicon.svg',
  'robots.txt',
  'sitemap.xml',
  'assets/site.css',
  'assets/site.js',
  'assets/antpos-pos-hero-768.webp',
  'assets/antpos-pos-hero-1536.webp',
  'assets/antpos-peripherals-724.webp',
  'assets/antpos-peripherals-1448.webp',
  'assets/antpos-retail-counter-724.webp',
  'assets/antpos-retail-counter-1448.webp',
  'assets/antpos-og-1200x630.webp'
];

const failures = [];

function expect(condition, message) {
  if (!condition) failures.push(message);
}

for (const relativePath of requiredFiles) {
  try {
    await access(path.join(publicRoot, relativePath));
  } catch {
    failures.push(`缺少生产文件: public/${relativePath}`);
  }
}

let html = '';
let css = '';
let js = '';
let robots = '';
let sitemap = '';

try {
  [html, css, js, robots, sitemap] = await Promise.all([
    readFile(path.join(publicRoot, 'index.html'), 'utf8'),
    readFile(path.join(publicRoot, 'assets/site.css'), 'utf8'),
    readFile(path.join(publicRoot, 'assets/site.js'), 'utf8'),
    readFile(path.join(publicRoot, 'robots.txt'), 'utf8'),
    readFile(path.join(publicRoot, 'sitemap.xml'), 'utf8')
  ]);
} catch {
  // 缺失文件已在上方逐项报告；继续汇总，便于一次看清契约差距。
}

if (html) {
  expect(html.includes('<link rel="canonical" href="https://antpos.dev/">'), '缺少 apex canonical');
  expect(html.includes('property="og:url" content="https://antpos.dev/"'), '缺少 Open Graph URL');
  expect(html.includes('property="og:image" content="https://antpos.dev/assets/antpos-og-1200x630.webp"'), '缺少 Open Graph 图片');
  expect(html.includes('type="application/ld+json"'), '缺少 Organization JSON-LD');
  expect(html.includes('"@type": "Organization"'), 'JSON-LD 不是 Organization');
  expect(html.includes('href="/assets/site.css"'), 'CSS 必须使用同源独立文件');
  expect(html.includes('src="/assets/site.js"'), 'JS 必须使用同源独立文件');
  expect(!/<style(?:\s|>)/i.test(html), 'HTML 不得包含内联样式');

  const executableInlineScripts = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)]
    .filter(([, attributes]) => !/\bsrc=/i.test(attributes) && !/type=["']application\/ld\+json["']/i.test(attributes));
  expect(executableInlineScripts.length === 0, 'HTML 不得包含可执行内联脚本');

  expect(html.includes('POS hardware.<br>Software support.'), 'Hero 核心文案不完整');
  expect(html.includes('Explore services'), '缺少 Explore services 主 CTA');
  expect(html.includes('Company details'), '缺少 Company details 辅 CTA');
  expect(html.includes('ANTPOS PTY LTD'), '缺少公司法定名称');
  expect(html.includes('56 662 654 186'), '缺少 ABN');
  expect(html.includes('662 654 186'), '缺少 ACN');
  expect(html.includes('23 September 2022'), '缺少登记日期');
  expect(html.includes('Queensland'), '缺少登记州信息');
  expect(html.includes('Software setup'), '缺少软件安装支持');
  expect(html.includes('Configuration'), '缺少软件配置支持');
  expect(html.includes('Troubleshooting'), '缺少软件故障支持');

  expect(!/<form\b/i.test(html), '公开页面不得包含联系表单');
  expect(!/<(?:input|textarea|select)\b/i.test(html), '公开页面不得包含信息收集控件');
  expect(!/mailto:|tel:/i.test(html), '公开页面不得包含直接联系入口');
  expect(!/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i.test(html), '公开页面不得包含邮箱地址');
  expect(!/google-analytics|googletagmanager|segment|mixpanel|hotjar|clarity/i.test(html), '公开页面不得包含追踪脚本');
  expect(!/(?:src|href)=["']https?:\/\//i.test(html.replaceAll('https://antpos.dev/', '')), '页面资源必须全部同源');

  expect(/id="menu-toggle"[^>]+aria-controls="primary-menu"[^>]+aria-expanded="false"/s.test(html), '手机菜单按钮缺少 ARIA 初始状态');
  expect(html.includes('id="primary-menu"'), '手机菜单缺少受控导航容器');
  expect(html.includes('class="skip-link"'), '缺少键盘跳转链接');
  expect(/fetchpriority="high"/.test(html), 'Hero 图片缺少高优先级加载');
  expect(/srcset="\/assets\/antpos-pos-hero-768\.webp 768w, \/assets\/antpos-pos-hero-1536\.webp 1536w"/.test(html), 'Hero 图片缺少响应式 srcset');

  const images = [...html.matchAll(/<img\b[^>]*>/gi)].map(([tag]) => tag);
  expect(images.length >= 3, '至少需要 Hero 与两张服务图片');
  expect(images.every((tag) => /\bwidth="\d+"/.test(tag) && /\bheight="\d+"/.test(tag)), '所有图片必须声明尺寸');
  expect(images.slice(1).every((tag) => /\bloading="lazy"/.test(tag)), '非 Hero 图片必须延迟加载');
}

if (css) {
  expect(css.includes('@media (prefers-color-scheme: dark)'), '缺少自动暗色模式');
  expect(css.includes(':focus-visible'), '缺少清晰的键盘焦点样式');
  expect(css.includes('@media (max-width: 760px)'), '缺少手机布局');
  expect(css.includes('prefers-reduced-motion: reduce'), '缺少减少动效适配');
  expect(!/@import/i.test(css), 'CSS 不得导入外部资源');
  expect(!/url\(["']?https?:\/\//i.test(css), 'CSS 不得引用外部资源');
}

if (js) {
  expect(js.includes("setAttribute('aria-expanded'"), '菜单脚本必须同步 aria-expanded');
  expect(js.includes("event.key === 'Escape'"), '菜单脚本必须支持 Escape 关闭');
  expect(js.includes('menuToggle.focus()'), 'Escape 关闭后必须归还焦点');
  expect(!/fetch\(|XMLHttpRequest|sendBeacon/i.test(js), '静态站脚本不得发送网络请求');
}

if (robots) {
  expect(robots.includes('Sitemap: https://antpos.dev/sitemap.xml'), 'robots.txt 缺少 sitemap');
}

if (sitemap) {
  expect(sitemap.includes('<loc>https://antpos.dev/</loc>'), 'sitemap.xml 缺少首页 URL');
}

if (failures.length > 0) {
  console.error(`ANTPOS 静态站检查失败（${failures.length} 项）：`);
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log(`ANTPOS 静态站检查通过：${requiredFiles.length} 个生产文件，隐私、SEO、无障碍与静态边界均符合要求。`);
