import test from 'node:test';
import assert from 'node:assert/strict';

import { resolveGrantedProfileOrigins } from '../src/lib/origin-registration.js';

test('动态内容脚本会恢复浏览器已授权但本地缓存缺失的供应商域名', async () => {
  const profiles = [
    { enabled: true, origins: ['https://www.dats.com.au/*'] },
    { enabled: true, origins: ['https://www.brazcoint.com.au/*', 'https://www.mnb.com.au/*'] },
    { enabled: false, origins: ['https://disabled.example/*'] },
  ];
  const grantedByBrowser = new Set([
    'https://www.dats.com.au/*',
    'https://www.brazcoint.com.au/*',
    'https://www.mnb.com.au/*',
    'https://disabled.example/*',
  ]);

  const result = await resolveGrantedProfileOrigins(
    profiles,
    (origin) => grantedByBrowser.has(origin),
  );

  assert.deepEqual(result, [
    'https://www.dats.com.au/*',
    'https://www.brazcoint.com.au/*',
    'https://www.mnb.com.au/*',
  ]);
});

test('单个权限检查失败时跳过该域名并继续处理其他供应商', async () => {
  const profiles = [
    { enabled: true, origins: ['https://broken.example/*', 'https://ok.example/*'] },
  ];

  const result = await resolveGrantedProfileOrigins(profiles, async (origin) => {
    if (origin.includes('broken')) throw new Error('permission check failed');
    return true;
  });

  assert.deepEqual(result, ['https://ok.example/*']);
});
