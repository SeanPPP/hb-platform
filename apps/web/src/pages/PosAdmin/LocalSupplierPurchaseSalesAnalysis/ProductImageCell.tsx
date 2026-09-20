import { useEffect, useMemo, useState } from 'react'
import { TRANSPARENT_IMAGE_FALLBACK, buildPurchaseSalesAnalysisImageSourceChain } from './helpers'

/** 商品图片单元格：依次尝试接口图片、按货号拼出的默认图片，最后回退透明占位；后台与订货前台分析页共用。 */
export default function ProductImageCell(props: {
  productImage?: string | null
  itemNumber?: string | null
  productCode?: string | null
  alt: string
}) {
  const sourceChain = useMemo(
    () =>
      buildPurchaseSalesAnalysisImageSourceChain(
        props.productImage,
        props.itemNumber,
        props.productCode,
      ),
    [props.itemNumber, props.productCode, props.productImage],
  )
  const [sourceIndex, setSourceIndex] = useState(0)

  useEffect(() => {
    setSourceIndex(0)
  }, [sourceChain])

  const currentSource =
    sourceChain[Math.min(sourceIndex, Math.max(sourceChain.length - 1, 0))] || TRANSPARENT_IMAGE_FALLBACK

  return (
    <img
      src={currentSource}
      alt={props.alt}
      loading="lazy"
      width={48}
      height={48}
      style={{
        width: 48,
        height: 48,
        objectFit: 'contain',
        borderRadius: 4,
        border: '1px solid #f0f0f0',
        background: '#fff',
      }}
      onError={() => {
        setSourceIndex((current) => (current < sourceChain.length - 1 ? current + 1 : current))
      }}
    />
  )
}
