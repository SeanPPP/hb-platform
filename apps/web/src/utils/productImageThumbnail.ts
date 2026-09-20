// 商品图片缩略图地址。
//
// 列表里的图片只显示 34~36px，但数据库里存的是原图地址（单张 100~150KB）。
// 腾讯云 COS 桶已开通图片处理，给地址追加 imageMogr2 参数即可由 COS 实时缩放并转 WebP，
// 实测单张降到约 2KB。供应商官网等外链图片无法处理，保持原地址。

// 腾讯云 COS 默认域名：<bucket>-<appid>.cos.<region>.myqcloud.com
const COS_HOST_PATTERN = /^[a-z0-9-]+\.cos\.[a-z0-9-]+\.myqcloud\.com$/i

/** 列表缩略图边长：按 2 倍像素密度覆盖 36px 的显示尺寸。 */
export const PRODUCT_LIST_THUMBNAIL_SIZE = 72

export function isCosImageUrl(url: string): boolean {
  try {
    const parsed = new URL(url)
    return (parsed.protocol === 'https:' || parsed.protocol === 'http:') && COS_HOST_PATTERN.test(parsed.hostname)
  } catch {
    return false
  }
}

/**
 * 返回 COS 图片的缩略图地址；非 COS 地址、已带查询参数的地址原样返回。
 * 已带查询参数时不追加：COS 要求图片处理参数位于查询串开头，拼接容易生成无效地址。
 */
export function toProductThumbnailUrl(
  url: string | null | undefined,
  size: number = PRODUCT_LIST_THUMBNAIL_SIZE,
): string | undefined {
  const trimmed = url?.trim()
  if (!trimmed) {
    return undefined
  }
  if (trimmed.includes('?') || !isCosImageUrl(trimmed)) {
    return trimmed
  }
  return `${trimmed}?imageMogr2/thumbnail/${size}x${size}/format/webp`
}
