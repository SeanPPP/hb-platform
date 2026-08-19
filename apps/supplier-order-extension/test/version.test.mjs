import test from 'node:test';
import assert from 'node:assert/strict';
import {
  parseSemver,
  isValidSemver,
  compareSemver,
  evaluateReleaseStatus,
  assertSameVersion,
} from '../src/lib/version.js';

test('parseSemver 解析合法版本', () => {
  assert.deepEqual(parseSemver('1.2.3'), {
    major: 1,
    minor: 2,
    patch: 3,
    prerelease: null,
    raw: '1.2.3',
  });
  assert.equal(parseSemver('1.2.3-beta.1').prerelease, 'beta.1');
  assert.equal(parseSemver('abc'), null);
  assert.equal(parseSemver('1.2'), null);
  assert.equal(parseSemver(''), null);
});

test('isValidSemver', () => {
  assert.equal(isValidSemver('1.0.0'), true);
  assert.equal(isValidSemver('1.0'), false);
  assert.equal(isValidSemver(''), false);
  assert.equal(isValidSemver(1.0), false);
  assert.equal(isValidSemver(null), false);
});

test('compareSemver', () => {
  assert.equal(compareSemver('1.0.0', '1.0.1'), -1);
  assert.equal(compareSemver('1.0.1', '1.0.0'), 1);
  assert.equal(compareSemver('1.0.0', '1.0.0'), 0);
  assert.equal(compareSemver('1.0.0', '1.0.0-beta'), 1);
  assert.equal(compareSemver('1.0.0-beta', '1.0.0'), -1);
  assert.throws(() => compareSemver('x', '1.0.0'));
});

test('evaluateReleaseStatus 版本状态', () => {
  assert.equal(
    evaluateReleaseStatus({ currentVersion: '1.0.0', latestVersion: '1.1.0', minVersion: '1.0.0' }),
    'update-available',
  );
  assert.equal(
    evaluateReleaseStatus({ currentVersion: '1.0.0', latestVersion: '1.1.0', minVersion: '1.1.0' }),
    'blocked',
  );
  assert.equal(
    evaluateReleaseStatus({ currentVersion: '1.1.0', latestVersion: '1.1.0', minVersion: '1.0.0' }),
    'latest',
  );
});

test('assertSameVersion 同版本双包', () => {
  assert.equal(assertSameVersion('1.0.0', '1.0.0'), true);
  assert.equal(assertSameVersion('1.0.0', '1.0.1'), false);
});
