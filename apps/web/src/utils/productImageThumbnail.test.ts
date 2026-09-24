import assert from 'node:assert/strict'
import { isCosImageUrl, toProductThumbnailUrl } from './productImageThumbnail'

const singaporeCos = 'https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/YW200/HB038-XM-001.jpg'
const shanghaiCos = 'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/HB167-037.jpg'

assert.equal(
  toProductThumbnailUrl(singaporeCos),
  `${singaporeCos}?imageMogr2/thumbnail/72x72/format/webp`,
  '新加坡 COS 桶图片应生成 72px WebP 缩略图',
)
assert.equal(
  toProductThumbnailUrl(shanghaiCos, 96),
  `${shanghaiCos}?imageMogr2/thumbnail/96x96/format/webp`,
  '应支持自定义缩略图边长',
)
assert.equal(
  toProductThumbnailUrl(`  ${shanghaiCos}  `),
  `${shanghaiCos}?imageMogr2/thumbnail/72x72/format/webp`,
  '首尾空白应被去除',
)

// 供应商官网外链无法做 COS 图片处理，必须原样返回。
assert.equal(
  toProductThumbnailUrl('https://www.malmar.com.au/images/products/DOGPOWS.jpg'),
  'https://www.malmar.com.au/images/products/DOGPOWS.jpg',
  '外链图片应原样返回',
)
// 仿冒域名不得被当成 COS。
assert.equal(isCosImageUrl('https://evil.example.com/x.cos.ap-singapore.myqcloud.com/a.jpg'), false, '路径里含 COS 域名不算 COS')
assert.equal(isCosImageUrl('https://a.cos.ap-singapore.myqcloud.com.evil.com/a.jpg'), false, '域名后缀被篡改不算 COS')
// 已有查询串时不拼接，避免生成 COS 无法识别的地址。
assert.equal(
  toProductThumbnailUrl(`${singaporeCos}?v=2`),
  `${singaporeCos}?v=2`,
  '已带查询串的地址应原样返回',
)
// 相对路径与空值。
assert.equal(toProductThumbnailUrl('/hb-sales-2019/YW200/a.jpg'), '/hb-sales-2019/YW200/a.jpg', '相对路径应原样返回')
assert.equal(toProductThumbnailUrl(''), undefined, '空串应返回 undefined')
assert.equal(toProductThumbnailUrl('   '), undefined, '空白串应返回 undefined')
assert.equal(toProductThumbnailUrl(null), undefined, 'null 应返回 undefined')
assert.equal(toProductThumbnailUrl(undefined), undefined, 'undefined 应返回 undefined')

console.log('productImageThumbnail.test: ok')
