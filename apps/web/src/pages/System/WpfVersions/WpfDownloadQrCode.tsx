import { QRCodeSVG } from '@rc-component/qrcode'

interface WpfDownloadQrCodeProps {
  value: string
}

export default function WpfDownloadQrCode({ value }: WpfDownloadQrCodeProps) {
  return (
    <div style={{ width: '100%', maxWidth: 280 }}>
      {/* 四模块静区直接写入 SVG，避免依赖 Ant Design 5.x 未透传的 marginSize。 */}
      <QRCodeSVG
        value={value}
        size={280}
        marginSize={4}
        level="M"
        title="WPF download QR code"
        style={{ display: 'block', width: '100%', height: 'auto' }}
      />
    </div>
  )
}
