import { PictureOutlined } from '@ant-design/icons'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

interface ProductImageProps {
  src?: string
  alt: string
  size?: 48 | 64
}

export default function ProductImage({ src, alt, size = 48 }: ProductImageProps) {
  const { t } = useTranslation()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    setFailed(false)
  }, [src])

  if (!src || failed) {
    return (
      <span
        aria-label={t('productFlowShared.noImage', { product: alt })}
        className="product-flow-image-fallback"
        style={{ width: size, height: size }}
      >
        <PictureOutlined />
      </span>
    )
  }

  return (
    <img
      src={src}
      alt={alt}
      loading="lazy"
      width={size}
      height={size}
      className="product-flow-image"
      onError={() => setFailed(true)}
    />
  )
}
