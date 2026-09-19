import { Image } from 'antd'
import type { CSSProperties } from 'react'
import { memo } from 'react'
import { toProductThumbnailUrl } from '../utils/productImageThumbnail'

interface ProductListImageProps {
  /** 数据库里存的原图地址。 */
  src: string
  size: number
  fit?: CSSProperties['objectFit']
  radius?: number
  className?: string
  fallback?: string
  /** 预览遮罩文案；传空串隐藏遮罩文字。 */
  previewMask?: string
}

/**
 * 商品列表的图片单元格：
 * - 表格内显示 COS 缩略图，单张从 100KB 以上降到约 2KB；
 * - loading="lazy" 让虚拟滚动之外、尚未进入视口的图片不抢占带宽；
 * - 点开预览时才加载原图，保证放大查看的清晰度。
 */
function ProductListImage({ src, size, fit = 'cover', radius, className, fallback, previewMask }: ProductListImageProps) {
  const thumbnail = toProductThumbnailUrl(src) ?? src
  const preview = previewMask === undefined ? { src } : { src, mask: previewMask }

  return (
    <Image
      className={className}
      src={thumbnail}
      alt=""
      width={size}
      height={size}
      loading="lazy"
      decoding="async"
      style={{ objectFit: fit, ...(radius === undefined ? {} : { borderRadius: radius }) }}
      preview={preview}
      fallback={fallback}
    />
  )
}

export default memo(ProductListImage)
