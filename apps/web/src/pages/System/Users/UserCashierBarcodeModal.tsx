import { ReloadOutlined } from '@ant-design/icons'
import { QRCodeSVG } from '@rc-component/qrcode'
import { Alert, Button, Descriptions, Empty, Modal, Space, Spin, Tag, Typography } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getUserCashierBarcode, refreshUserCashierBarcode } from '../../../services/userService'
import type { EmployeeCashierBarcodeDto, UserDto } from '../../../types/user'
import { formatUserLocalDateTime } from './time'

export default function UserCashierBarcodeModal({ user, onClose }: { user: UserDto; onClose: () => void }) {
  const { t } = useTranslation()
  const [barcode, setBarcode] = useState<EmployeeCashierBarcodeDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(false)
  const [uncertain, setUncertain] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const alive = useRef(false)
  const sequence = useRef(0)
  const mutationPending = useRef(false)

  const load = useCallback(async () => {
    if (mutationPending.current || !user.isActive) return
    const requestId = ++sequence.current
    setLoading(true)
    setBarcode(null)
    setError(false)
    setConfirming(false)
    try {
      const result = await getUserCashierBarcode(user.userGUID)
      if (alive.current && requestId === sequence.current) setBarcode(result)
    } catch {
      if (alive.current && requestId === sequence.current) setError(true)
    } finally {
      if (alive.current && requestId === sequence.current) setLoading(false)
    }
  }, [user.userGUID, user.isActive])

  useEffect(() => {
    alive.current = true
    void load()
    const onFocus = () => { if (document.visibilityState !== 'hidden') void load() }
    window.addEventListener('focus', onFocus)
    document.addEventListener('visibilitychange', onFocus)
    return () => {
      // 关闭或切换用户后，旧请求不得把另一人的身份码写回弹窗。
      alive.current = false
      sequence.current += 1
      window.removeEventListener('focus', onFocus)
      document.removeEventListener('visibilitychange', onFocus)
    }
  }, [load])

  const refresh = async () => {
    if (!barcode || loading || mutationPending.current || !user.isActive) return
    const expectedBarcode = barcode.exists ? barcode.barcode : null
    if (barcode.exists && !expectedBarcode) return
    mutationPending.current = true
    const requestId = ++sequence.current
    setConfirming(false)
    setSaving(true)
    setBarcode(null)
    setUncertain(false)
    setError(false)
    try {
      const result = await refreshUserCashierBarcode(user.userGUID, expectedBarcode)
      if (alive.current && requestId === sequence.current) setBarcode(result)
    } catch {
      if (alive.current && requestId === sequence.current) {
        // POST 结果不明时只读回查，不能再次生成而使刚发出的码失效。
        setUncertain(true)
        mutationPending.current = false
        await load()
      }
    } finally {
      mutationPending.current = false
      if (alive.current) setSaving(false)
    }
  }

  const busy = loading || saving
  const hasCode = Boolean(barcode?.exists && barcode.barcode)
  const canMutate = user.isActive && !busy && !error && barcode !== null && (!barcode.exists || hasCode)
  const actionLabel = hasCode ? t('system.users.cashierQr.reset') : t('system.users.cashierQr.generate')

  return (
    <>
      <Modal
        open
        title={t('system.users.cashierQr.title')}
        width={460}
        onCancel={onClose}
        footer={<Space wrap>
          <Button onClick={onClose}>{t('common.close', '关闭')}</Button>
          {user.isActive && <Button icon={<ReloadOutlined />} disabled={saving} onClick={() => void load()}>{t('system.users.cashierQr.reload')}</Button>}
          {user.isActive && <Button type="primary" danger={hasCode} disabled={!canMutate} loading={saving} onClick={() => hasCode ? setConfirming(true) : void refresh()}>{actionLabel}</Button>}
        </Space>}
      >
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Descriptions size="small" column={1}>
            <Descriptions.Item label={t('system.users.fullName', '姓名')}>{user.fullName || '—'}</Descriptions.Item>
            <Descriptions.Item label={t('system.users.username', '用户名')}>{user.username}</Descriptions.Item>
            <Descriptions.Item label={t('common.status', '状态')}><Tag color={user.isActive ? 'success' : 'default'}>{t(user.isActive ? 'common.active' : 'common.inactive')}</Tag></Descriptions.Item>
          </Descriptions>
          {!user.isActive ? <Alert type="warning" showIcon message={t('system.users.cashierQr.inactive')} /> : <>
            {uncertain && <Alert type="warning" showIcon message={t('system.users.cashierQr.uncertain')} />}
            {busy ? <div style={{ textAlign: 'center', padding: '64px 0' }}><Spin /><div style={{ marginTop: 12 }}>{t('system.users.cashierQr.loading')}</div></div>
              : error ? <Alert type="error" showIcon message={t('system.users.cashierQr.loadFailed')} />
                : hasCode ? <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12 }}>
                  <div data-testid="cashier-qr" style={{ background: '#fff', width: '100%', maxWidth: 256 }}>
                    {/* 静区直接写入 SVG，确保窄屏缩放后仍保留四模块白边。 */}
                    <QRCodeSVG value={barcode!.barcode!} size={256} marginSize={4} level="M" fgColor="#000" bgColor="#fff" title={t('system.users.cashierQr.title')} style={{ display: 'block', width: '100%', height: 'auto' }} />
                  </div>
                  <Typography.Text type="secondary">{t('system.users.cashierQr.scanHint')}</Typography.Text>
                  <Typography.Text type="secondary">{t('system.users.cashierQr.updatedAt')}: {formatUserLocalDateTime(barcode?.updatedAt || barcode?.createdAt)}</Typography.Text>
                </div> : <>
                  <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('system.users.cashierQr.empty')} />
                  <Alert type="info" showIcon message={t('system.users.cashierQr.generateHint')} />
                </>}
          </>}
        </Space>
      </Modal>
      <Modal
        open={confirming}
        title={t('system.users.cashierQr.confirmTitle')}
        onCancel={() => setConfirming(false)}
        onOk={() => void refresh()}
        okText={t('system.users.cashierQr.reset')}
        cancelText={t('common.cancel', '取消')}
        okButtonProps={{ danger: true, disabled: !canMutate }}
      >
        <Typography.Paragraph>{t('system.users.cashierQr.confirmDescription', { name: user.fullName ? `${user.fullName} (${user.username})` : user.username })}</Typography.Paragraph>
      </Modal>
    </>
  )
}
