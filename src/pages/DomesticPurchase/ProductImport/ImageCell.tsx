import { Modal } from 'antd'
import { useTranslation } from 'react-i18next'

interface ImagePreviewDialogProps {
  open: boolean
  imageUrl: string
  productCode: string
  productName: string
  onClose: () => void
}

export function ImagePreviewDialog({ open, imageUrl, productCode, productName, onClose }: ImagePreviewDialogProps) {
  const { t } = useTranslation()
  return (
    <Modal title={`${productCode} - ${productName}`} open={open} onCancel={onClose} footer={null} width={400}>
      {imageUrl ? <img src={imageUrl} alt={productCode} style={{ width: '100%', objectFit: 'contain' }} /> : <p>{t('productImport.noImage', '无图片')}</p>}
    </Modal>
  )
}
