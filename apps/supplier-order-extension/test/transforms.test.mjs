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

test('after-colon 仅提取冒号后的内容，无冒号时 fail closed', () => {
  assert.equal(applyTransform('Code: CA144609', 'after-colon'), 'CA144609');
  assert.equal(applyTransform('Item: 60156', 'after-colon'), '60156');
  assert.equal(applyTransform('Label: value:extra', 'after-colon'), 'value:extra');
  assert.equal(applyTransform('CA144609', 'after-colon'), '');
  assert.equal(isTransformAllowed('after-colon'), true);
  assert.equal(safeTransformList(['after-colon']), true);
});

test('GFA 下划线货号转换为 HB 的斜线货号', () => {
  assert.equal(applyTransform('HO_ABN2BW', 'underscore-to-slash'), 'HO/ABN2BW');
  assert.equal(applyTransform('AA_BB_CC', 'underscore-to-slash'), 'AA/BB/CC');
  assert.equal(isTransformAllowed('underscore-to-slash'), true);
});

test('TXK 仅提取固定 SKU 前缀后的货号', () => {
  assert.equal(applyTransform(' - SKU TXK6160', 'after-sku'), 'TXK6160');
  assert.equal(applyTransform('SKU 2111', 'after-sku'), '2111');
  assert.equal(applyTransform('Code TXK6160', 'after-sku'), '');
  assert.equal(isTransformAllowed('after-sku'), true);
  assert.equal(safeTransformList(['after-sku', 'trim', 'uppercase']), true);
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
