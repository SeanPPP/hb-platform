import test from 'node:test';
import assert from 'node:assert/strict';
import {
  applyTransform,
  applyTransforms,
  isTransformAllowed,
  safeTransformList,
} from '../src/lib/transforms.js';

test('trim/uppercase/lowercase', () => {
  assert.equal(applyTransform('  abc  ', { type: 'trim' }), 'abc');
  assert.equal(applyTransform('abc', { type: 'uppercase' }), 'ABC');
  assert.equal(applyTransform('ABC', { type: 'lowercase' }), 'abc');
});

test('未知 transform 必须抛错（安全边界，绝不 eval）', () => {
  assert.throws(() => applyTransform('x', { type: 'eval' }), /unsupported transform/);
  assert.throws(() => applyTransform('x', { type: 'Function' }));
  assert.throws(() => applyTransform('x', null));
  assert.throws(() => applyTransform('x', { type: 'constructor' }));
});

test('applyTransforms 链式', () => {
  assert.equal(
    applyTransforms('  sku-88  ', [{ type: 'trim' }, { type: 'uppercase' }]),
    'SKU-88',
  );
});

test('服务端契约的字符串 transform 可直接执行', () => {
  assert.equal(applyTransforms('  dats-88  ', ['trim', 'uppercase']), 'DATS-88');
  assert.equal(safeTransformList(['trim', 'uppercase']), true);
});

test('safeTransformList 与允许集合', () => {
  assert.equal(isTransformAllowed('trim'), true);
  assert.equal(isTransformAllowed('eval'), false);
  assert.equal(safeTransformList(null), true);
  assert.equal(safeTransformList(undefined), true);
  assert.equal(safeTransformList([]), true);
  assert.equal(isTransformAllowed('regexCapture'), false);
  assert.equal(safeTransformList([{ type: 'trim' }, { type: 'regexCapture', pattern: 'x' }]), false);
  assert.equal(safeTransformList([{ type: 'eval' }]), false);
  assert.equal(safeTransformList('trim'), false);
});
