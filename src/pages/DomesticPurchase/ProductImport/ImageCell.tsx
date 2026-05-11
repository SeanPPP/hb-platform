import { Modal } from 'antd'

interface ImagePreviewDialogProps {
  open: boolean
  imageUrl: string
  productCode: string
  productName: string
  onClose: () => void
}

export function ImagePreviewDialog({ open, imageUrl, productCode, productName, onClose }: ImagePreviewDialogProps) {
  return (
    <Modal title={`${productCode} - ${productName}`} open={open} onCancel={onClose} footer={null} width={400}>
      {imageUrl ? <img src={imageUrl} alt={productCode} style={{ width: '100%', objectFit: 'contain' }} /> : <p>无图片</p>}
    </Modal>
  )
}
