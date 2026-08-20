import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

import {
  markSummaryRequestFailed,
  needsHostRemount,
  resetSummaryRetry,
  shouldRequestVisibleSummary,
} from '../src/lib/list-recovery.js';

test('页面移除历史按钮 host 后要求重新挂载', () => {
  assert.equal(needsHostRemount({ host: { isConnected: false } }), true);
  assert.equal(needsHostRemount({ host: { isConnected: true } }), false);
  assert.equal(needsHostRemount({ host: null }), true);
});

test('传输失败会释放 requested 锁，并在退避时间到达后允许重试', () => {
  const entry = {
    requested: true,
    isVisible: true,
    state: { kind: 'loading' },
  };

  const state = markSummaryRequestFailed(entry, 1_000);

  assert.deepEqual(state, { kind: 'error', reason: 'error', retryable: true });
  assert.equal(entry.requested, false);
  assert.equal(entry.retryCount, 1);
  assert.equal(entry.nextRetryAt, 3_000);
  assert.equal(shouldRequestVisibleSummary(entry, 2_999), false);
  assert.equal(shouldRequestVisibleSummary(entry, 3_000), true);
});

test('业务错误、不可见、请求进行中或已成功的商品不会重复请求', () => {
  assert.equal(shouldRequestVisibleSummary({ isVisible: false, requested: false, state: { kind: 'error', retryable: true } }), false);
  assert.equal(shouldRequestVisibleSummary({ isVisible: true, requested: true, state: { kind: 'error', retryable: true } }), false);
  assert.equal(shouldRequestVisibleSummary({ isVisible: true, requested: false, state: { kind: 'error' } }), false);
  assert.equal(shouldRequestVisibleSummary({ isVisible: true, requested: false, state: { kind: 'matched' } }), false);
  assert.equal(shouldRequestVisibleSummary({ isVisible: true, requested: false, state: { kind: 'loading' } }), true);
});

test('传输失败最多重试三次并使用指数退避', () => {
  const entry = { requested: true, isVisible: true, state: { kind: 'loading' } };
  const expectedRetryAt = [3_000, 6_000, 11_000];

  for (let attempt = 0; attempt < 3; attempt += 1) {
    const state = markSummaryRequestFailed(entry, 1_000 + attempt * 1_000);
    assert.equal(state.retryable, true);
    assert.equal(entry.nextRetryAt, expectedRetryAt[attempt]);
    entry.requested = true;
  }

  const exhausted = markSummaryRequestFailed(entry, 4_000);
  assert.equal(exhausted.retryable, false);
  assert.equal(shouldRequestVisibleSummary(entry, Number.MAX_SAFE_INTEGER), false);
});

test('新商品或成功响应会清除既有重试状态', () => {
  const entry = { retryCount: 2, nextRetryAt: 9_000 };
  resetSummaryRetry(entry);
  assert.equal(entry.retryCount, 0);
  assert.equal(entry.nextRetryAt, 0);
});

test('滚动进入可见区域也必须经过退避和重试上限门控', () => {
  const source = readFileSync(new URL('../src/content/list.js', import.meta.url), 'utf8');
  assert.match(
    source,
    /if \(e\.isIntersecting && shouldRequestVisibleSummary\(entry\)\) requestSummary\(entry\);/,
  );
});

test('SPA 导航会使复用卡片的摘要失效并重新加载', () => {
  const source = readFileSync(new URL('../src/content/list.js', import.meta.url), 'utf8');
  const navigationBlock = source.match(/const handleNavigation = \(\) => \{[\s\S]*?\n  \};/)?.[0] || '';
  assert.match(navigationBlock, /entry\.state = \{ kind: 'loading' \};/);
  assert.match(navigationBlock, /scan\(\);/);
});
