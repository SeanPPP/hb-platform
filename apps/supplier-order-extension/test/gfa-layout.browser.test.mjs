import test from 'node:test';
import assert from 'node:assert/strict';
import {
  existsSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync, spawnSync } from 'node:child_process';

const PACKAGE_ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const REPOSITORY_ROOT = join(PACKAGE_ROOT, '..', '..');
const LIST_SOURCE_PATH = 'apps/supplier-order-extension/src/content/list.js';

function readListSource() {
  const sourceRef = process.env.HB_GFA_LAYOUT_SOURCE_REF;
  if (!sourceRef) return readFileSync(join(PACKAGE_ROOT, 'src/content/list.js'), 'utf8');
  return execFileSync('git', ['show', `${sourceRef}:${LIST_SOURCE_PATH}`], {
    cwd: REPOSITORY_ROOT,
    encoding: 'utf8',
  });
}

function findChromiumBrowser() {
  return [
    process.env.CHROME_BIN,
    process.env.EDGE_BIN,
    '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/usr/bin/google-chrome',
    '/usr/bin/google-chrome-stable',
    '/usr/bin/chromium',
    '/usr/bin/chromium-browser',
  ].find((candidate) => candidate && existsSync(candidate));
}

const GFA_PAGE_CSS = `
* { box-sizing: border-box; }
body { margin: 0; background: #eef1f3; font-family: Arial, sans-serif; }
.fixture { padding: 40px; }
.list-row {
  background: #fff;
  display: flex;
  border-bottom: 1px solid #eee;
  padding: 5px;
  position: relative;
  overflow: hidden;
  width: calc(100vw - 80px);
}
.list-row h4 {
  margin: 0 0 5px;
  line-height: 18px;
  display: block;
  font-size: 18px;
  height: 24px;
}
.list-row .main-img {
  display: inline-block;
  width: 150px;
  height: 150px;
  flex: 0 0 150px;
}
.list-row .content {
  height: 150px;
  width: 100%;
  flex: 0;
  flex-basis: auto;
  padding: 10px;
  overflow: hidden;
}
.list-row .content .list-content {
  height: 100%;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  margin-right: 130px;
}
.list-row .content .list-detail {
  overflow: hidden;
  height: 100%;
}
.list-row .content .list-detail ul { list-style: none; padding: 0; margin: 0; font-size: 12px; display: table; }
.list-row .content .list-detail li { display: table-row; }
.detail-label { display: table-cell; padding-right: 15px; }
.detail-value { display: table-cell; }
.list-row .content .price {
  position: absolute;
  top: 67px;
  right: 0;
  height: 40px;
  padding: 8px 10px;
  background: #777;
  color: #fff;
}
.list-row .content .options {
  position: absolute;
  right: 10px;
  bottom: 10px;
  width: 225px;
  height: 26px;
  text-align: right;
}
@media (max-width: 500px) {
  .list-row .main-img { display: none; }
}
@media (max-width: 767px) {
  .list-row h4 { font-size: 14px; line-height: 20px; }
  .list-row .main-img { width: 100px; height: 100px; flex: 0 0 100px; }
  .list-row .content { height: 100px; }
  .list-row .content .list-content { margin-right: 80px; }
  .list-row .content .price { top: 0; height: 34px; }
}
.small-list .list-row h4 { font-size: 14px; line-height: 20px; }
.small-list .list-row .main-img { width: 100px; height: 100px; flex: 0 0 100px; }
.small-list .list-row .content { height: 100px; }
.small-list .list-row .content .list-content { margin-right: 80px; }
.small-list .list-row .content .price { top: 0; height: 34px; }
`;

function cardMarkup(id, wrapperClass, { gfa = true } = {}) {
  return `
    <section class="main list ${wrapperClass}">
      <div id="${id}" class="list-row"${gfa ? ' data-product="CO_SCKBEC"' : ''}>
        <div class="main-img">IMAGE</div>
        <div class="content">
          <a href="/product/view?id=CO_SCKBEC">
            <div class="list-content">
              <h4>BYS SKIN BRIGHTENING EYE CREAM 30ML BYS</h4>
              <div class="list-detail">
                <ul>
                  <li><span class="detail-label">Code</span><span class="detail-value">CO_SCKBEC</span></li>
                  <li><span class="detail-label">Pack Size</span><span class="detail-value">6</span></li>
                </ul>
              </div>
            </div>
          </a>
          <div data-hb-sro-host></div>
          <div class="price">$4.13</div>
          <div class="options"><button type="button">Add</button></div>
        </div>
      </div>
    </section>`;
}

function fixtureHtml(layoutCss, hostStyle) {
  return `<!doctype html>
<html><head><meta charset="utf-8"><style>${GFA_PAGE_CSS}</style><style>${layoutCss}</style></head>
<body><main class="fixture">
  ${cardMarkup('small', 'small-list')}
  ${cardMarkup('regular', '')}
  ${cardMarkup('non-gfa', 'small-list', { gfa: false })}
</main><pre id="result"></pre>
<script>
  const hostStyle = ${JSON.stringify(hostStyle)};
  const cardIds = ['small', 'regular', 'non-gfa'];
  for (const id of cardIds) {
    const host = document.querySelector('#' + id + ' [data-hb-sro-host]');
    host.style.cssText = hostStyle;
    const root = host.attachShadow({ mode: 'open' });
    root.innerHTML = '<style>.hb-btn{all:unset;box-sizing:border-box;display:inline-block;max-width:100%;padding:4px 8px;border-radius:4px;border:1px solid #d5d5d5;background:#fafafa;color:#333;cursor:pointer;font:12px/1.5 system-ui,sans-serif;white-space:normal;overflow-wrap:anywhere;pointer-events:auto}.hb-rank-line{display:block;width:max-content;max-width:100%;box-sizing:border-box;margin-top:2px;padding:1px 6px;border:1px solid #b8d8ff;border-radius:999px;background:#eaf3ff;color:#1565c0;font-size:10px;font-weight:700;line-height:1.5}</style><button class="hb-btn">无采购<span class="hb-rank-line">近 60 天销量：TOP 30%</span></button>';
  }
  const rect = (element) => {
    const value = element.getBoundingClientRect();
    return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height };
  };
  const overlaps = (first, second) => !(
    first.right <= second.left || first.left >= second.right || first.bottom <= second.top || first.top >= second.bottom
  );
  const measure = (id) => {
    const card = document.getElementById(id);
    const content = card.querySelector('.content');
    const detail = card.querySelector('.list-detail');
    const host = card.querySelector('[data-hb-sro-host]');
    const price = card.querySelector('.price');
    const options = card.querySelector('.options');
    const button = host.shadowRoot.querySelector('button');
    const cardRect = rect(card);
    const contentRect = rect(content);
    const detailRect = rect(detail);
    const hostRect = rect(host);
    const priceRect = rect(price);
    const optionsRect = rect(options);
    const blankHit = document.elementFromPoint(hostRect.right - 2, hostRect.top + 2);
    const buttonRect = rect(button);
    const buttonHit = host.shadowRoot.elementFromPoint(buttonRect.left + 2, buttonRect.top + 2);
    return {
      cardHeight: cardRect.height,
      contentHeight: contentRect.height,
      hostMarginRight: getComputedStyle(host).marginRight,
      hostPointerEvents: getComputedStyle(host).pointerEvents,
      buttonPointerEvents: getComputedStyle(button).pointerEvents,
      hostInsideCard: hostRect.top >= cardRect.top && hostRect.bottom <= cardRect.bottom,
      hostInsideContent: hostRect.top >= contentRect.top && hostRect.bottom <= contentRect.bottom,
      overlapsDetail: overlaps(hostRect, detailRect),
      overlapsPrice: overlaps(hostRect, priceRect),
      overlapsOptions: overlaps(hostRect, optionsRect),
      blankHitIsHost: blankHit === host,
      buttonHitIsButton: buttonHit === button,
    };
  };
  const result = {
    width: window.innerWidth,
    small: measure('small'),
    regular: measure('regular'),
    nonGfa: measure('non-gfa'),
  };
  document.querySelector('#small [data-hb-sro-host]').remove();
  result.afterTeardown = {
    contentHeight: document.querySelector('#small .content').getBoundingClientRect().height,
    cardHeight: document.querySelector('#small').getBoundingClientRect().height,
  };
  document.getElementById('result').textContent = btoa(JSON.stringify(result));
</script></body></html>`;
}

function runFixture(browser, htmlPath, width, profilePath) {
  const result = spawnSync(browser, [
    '--headless=new',
    '--disable-gpu',
    '--no-sandbox',
    `--user-data-dir=${profilePath}`,
    `--window-size=${width},700`,
    '--virtual-time-budget=1000',
    '--dump-dom',
    pathToFileURL(htmlPath).href,
  ], { encoding: 'utf8', timeout: 30000, maxBuffer: 10 * 1024 * 1024 });
  assert.equal(result.status, 0, result.stderr || '无头浏览器运行失败');
  const encoded = result.stdout.match(/<pre id="result">([A-Za-z0-9+/=]+)<\/pre>/)?.[1];
  assert.ok(encoded, `无头浏览器没有返回布局结果：${result.stdout.slice(-500)}`);
  return JSON.parse(Buffer.from(encoded, 'base64').toString('utf8'));
}

function assertNoOverlap(metrics) {
  assert.equal(metrics.hostInsideCard, true);
  assert.equal(metrics.hostInsideContent, true);
  assert.equal(metrics.overlapsDetail, false);
  assert.equal(metrics.overlapsPrice, false);
  assert.equal(metrics.overlapsOptions, false);
  assert.equal(metrics.hostPointerEvents, 'none');
  assert.equal(metrics.buttonPointerEvents, 'auto');
  assert.equal(metrics.blankHitIsHost, false);
  assert.equal(metrics.buttonHitIsButton, true);
}

test('GFA 摘要在 100/150px 与窄屏商品行中完整显示且不遮挡原控件', { timeout: 60000 }, () => {
  const source = readListSource();
  const layoutCss = source.match(/gfaLayoutStyle\.textContent = `([\s\S]*?)`;/)?.[1];
  const hostStyle = source.match(/\? '([^']*margin:4px 235px[^']*)'/)?.[1];
  assert.ok(layoutCss, '共享内容脚本缺少可执行的 GFA 布局样式');
  assert.ok(hostStyle, '共享内容脚本缺少 GFA 摘要宿主样式');

  const browser = findChromiumBrowser();
  assert.ok(browser, '运行 GFA 布局测试需要 Edge、Chrome 或 Chromium');
  const tempRoot = mkdtempSync(join(tmpdir(), 'hb-gfa-layout-'));
  try {
    const htmlPath = join(tempRoot, 'fixture.html');
    writeFileSync(htmlPath, fixtureHtml(layoutCss, hostStyle));
    const wide = runFixture(browser, htmlPath, 1080, join(tempRoot, 'wide-profile'));
    const narrow = runFixture(browser, htmlPath, 390, join(tempRoot, 'narrow-profile'));

    assertNoOverlap(wide.small);
    assertNoOverlap(wide.regular);
    assert.ok(wide.small.cardHeight > 111, '100px 小列表行必须为摘要扩展高度');
    assert.ok(wide.regular.cardHeight >= 160, '150px 常规行不得被摘要压缩');
    assert.equal(wide.small.hostMarginRight, '235px');

    assertNoOverlap(narrow.small);
    assertNoOverlap(narrow.regular);
    assert.equal(narrow.small.hostMarginRight, '0px');
    assert.equal(narrow.regular.hostMarginRight, '0px');
    assert.ok(narrow.small.cardHeight > wide.small.cardHeight, '窄屏必须为底部操作区保留独立空间');

    assert.equal(wide.nonGfa.contentHeight, 100, '无 GFA data-product 的行不得被全局样式扩高');
    assert.equal(wide.afterTeardown.contentHeight, 100, '移除宿主后 GFA 样式必须自动失效');
    assert.equal(wide.afterTeardown.cardHeight, 111, 'teardown 后必须恢复原始小列表行高');
  } finally {
    rmSync(tempRoot, { recursive: true, force: true });
  }
});
