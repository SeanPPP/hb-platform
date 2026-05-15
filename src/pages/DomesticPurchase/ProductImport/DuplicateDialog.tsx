import { Button, Modal, Table, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ColumnsType } from 'antd/es/table'
import type { DuplicateGroup } from './types'

interface DuplicateDialogProps {
  open: boolean
  duplicateGroups: DuplicateGroup[]
  onClose: () => void
  onConfirm: () => void
}

export function DuplicateDialog({ open, duplicateGroups, onClose, onConfirm }: DuplicateDialogProps) {
  const { t } = useTranslation()
  const columns: ColumnsType<DuplicateGroup> = [
    { title: t('productImport.hbProductNoCol', '货号'), dataIndex: 'productCode', key: 'productCode', width: 150 },
    { title: t('productImport.duplicateCount', '重复数量'), dataIndex: 'count', key: 'count', width: 100, render: (count) => <Tag color="orange">{count}</Tag> },
    { title: t('productImport.mergedQuantity', '合并后件数'), key: 'mergedQuantity', width: 120, render: (_, record) => record.merged.quantity },
  ]

  return (
    <Modal title={t('productImport.foundDuplicateGroups', '发现 {{count}} 组重复货号', { count: duplicateGroups.length })} open={open} onCancel={onClose} width={600} footer={<><Button onClick={onClose}>{t('common.cancel', '取消')}</Button><Button type="primary" onClick={onConfirm}>{t('productImport.mergeDuplicates', '合并重复')}</Button></>}>
      <p style={{ marginBottom: 12 }}>{t('productImport.duplicateWarning', '以下货号在导入数据中存在重复，建议合并后再执行检测匹配：')}</p>
      <Table columns={columns} dataSource={duplicateGroups} rowKey="productCode" size="small" pagination={false} />
    </Modal>
  )
}
