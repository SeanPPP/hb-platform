import { Button, Checkbox, Col, Modal, Row, Select, Space, Spin } from 'antd'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  defaultPushProductsToHqUpdateFields,
  pushProductsToHqUpdateFieldOptions,
} from '../../types/posProduct'
import type {
  PushProductsToHqStoreOption,
  PushProductsToHqUpdateField,
} from '../../types/posProduct'
import {
  buildPushToHqStoreSelectOptions,
  getNextPushToHqStoreSelection,
  getPushToHqStoreSelectAllState,
  hasPushToHqTargetStoreError,
} from './storeSelection'

interface PosHqPushModalProps {
  open: boolean
  selectedCount: number
  storeOptions: PushProductsToHqStoreOption[]
  storeOptionsLoading: boolean
  storeOptionsError?: string | null
  onRetryStoreOptions: () => void
  confirmLoading: boolean
  onConfirm: (updateFields: PushProductsToHqUpdateField[], targetStoreCodes: string[]) => void
  onCancel: () => void
}

// 商品管理页和仓库商品页共用的“发送到 HQ”弹窗：分店选择位于 16 个更新字段上方，
// 每次打开由页面重新获取最新 HQ 分店选项并默认全选。
export default function PosHqPushModal({
  open,
  selectedCount,
  storeOptions,
  storeOptionsLoading,
  storeOptionsError,
  onRetryStoreOptions,
  confirmLoading,
  onConfirm,
  onCancel,
}: PosHqPushModalProps) {
  const { t } = useTranslation()
  const [selectedFields, setSelectedFields] = useState<PushProductsToHqUpdateField[]>(
    () => [...defaultPushProductsToHqUpdateFields],
  )
  const [targetStoreCodes, setTargetStoreCodes] = useState<string[]>([])
  const [validationError, setValidationError] = useState<string | null>(null)

  const allStoreCodes = storeOptions.map((option) => option.storeCode)
  const selectAllState = getPushToHqStoreSelectAllState(targetStoreCodes, allStoreCodes)

  // 每次打开（或分店选项刷新完成）都重置为“默认全选最新选项”。
  useEffect(() => {
    if (!open) return
    setSelectedFields([...defaultPushProductsToHqUpdateFields])
    setTargetStoreCodes(storeOptions.map((option) => option.storeCode))
    setValidationError(null)
  }, [open, storeOptions])

  const handleOk = () => {
    if (!selectedFields.length) {
      setValidationError(t('containers.updateFields.selectAtLeastOne', '请至少选择一个更新字段'))
      return
    }
    if (hasPushToHqTargetStoreError(selectedFields, targetStoreCodes)) {
      setValidationError(t('posAdmin.products.pushToHqTargetStoresRequired', '请至少选择一个目标分店'))
      return
    }
    setValidationError(null)
    onConfirm(selectedFields, targetStoreCodes)
  }

  const handleCancel = () => {
    // 提交进行中忽略取消/关闭/ESC/mask，避免释放锁后立即重开造成重复提交。
    if (confirmLoading) return
    onCancel()
  }

  return (
    <Modal
      open={open}
      title={t('posAdmin.products.pushToHq', '发送到HQ')}
      width="min(640px, calc(100vw - 32px))"
      okText={t('common.confirm', '确定')}
      cancelText={t('common.cancel', '取消')}
      okButtonProps={{ disabled: Boolean(storeOptionsError) || storeOptionsLoading }}
      cancelButtonProps={{ disabled: confirmLoading }}
      confirmLoading={confirmLoading}
      closable={!confirmLoading}
      keyboard={!confirmLoading}
      maskClosable={!confirmLoading}
      onOk={handleOk}
      onCancel={handleCancel}
      destroyOnHidden
    >
      {storeOptionsLoading ? (
        <div style={{ textAlign: 'center', padding: 24 }}>
          <Spin />
        </div>
      ) : storeOptionsError ? (
        <Space direction="vertical" size={10} style={{ width: '100%' }}>
          <div style={{ color: '#cf1322' }}>
            {t('posAdmin.products.pushToHqStoreOptionsLoadFailed', '获取 HQ 分店选项失败，请重试')}
          </div>
          <Button onClick={onRetryStoreOptions}>{t('common.retry', '重试')}</Button>
        </Space>
      ) : (
        <Space direction="vertical" size={10} style={{ width: '100%' }}>
          <div>
            {t('posAdmin.products.pushToHqUpdateFieldsHint', '已选择 {{count}} 个商品，请勾选要更新到 HQ 的字段。', { count: selectedCount })}
          </div>
          <div>
            <div style={{ marginBottom: 4 }}>
              {t('posAdmin.products.pushToHqTargetStoresLabel', 'HQ 分店')}
            </div>
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              aria-label={t('posAdmin.products.pushToHqTargetStoresLabel', 'HQ 分店')}
              maxTagCount="responsive"
              style={{ width: '100%' }}
              value={targetStoreCodes}
              onChange={(values) => {
                setTargetStoreCodes(values.map(String))
                setValidationError(null)
              }}
              options={buildPushToHqStoreSelectOptions(storeOptions)}
              popupRender={(menu) => (
                <div>
                  {menu}
                  <div style={{ padding: '6px 12px', borderTop: '1px solid #f0f0f0' }}>
                    <Checkbox
                      checked={selectAllState.checked}
                      indeterminate={selectAllState.indeterminate}
                      disabled={!allStoreCodes.length}
                      onChange={(event) => {
                        setTargetStoreCodes(getNextPushToHqStoreSelection(event.target.checked, allStoreCodes))
                        setValidationError(null)
                      }}
                    >
                      {t('posAdmin.products.selectAllStores', '全选所有分店 ({{count}} 个)', { count: storeOptions.length })}
                    </Checkbox>
                  </div>
                </div>
              )}
            />
          </div>
          <Checkbox.Group
            value={selectedFields}
            onChange={(values) => {
              setSelectedFields(values.map(String) as PushProductsToHqUpdateField[])
              setValidationError(null)
            }}
          >
            <Row gutter={[8, 6]}>
              {pushProductsToHqUpdateFieldOptions.map((field) => (
                <Col span={12} key={field.value}>
                  <Checkbox value={field.value}>{t(field.labelKey, field.fallbackLabel)}</Checkbox>
                </Col>
              ))}
            </Row>
          </Checkbox.Group>
          {validationError ? (
            <div style={{ color: '#cf1322' }}>{validationError}</div>
          ) : null}
          <div style={{ color: '#8c8c8c', fontSize: 12 }}>
            {t('posAdmin.products.pushToHqTargetStoresHint', '目标分店仅约束已有 HQ 商品的分店维度写入；选择分店维度字段时，新商品仍会补齐全部 HQ 分店。')}
          </div>
          <div style={{ color: '#8c8c8c', fontSize: 12 }}>
            {t(
              'containers.updateFields.hqCreateHint',
              '字段选择主要限制已有 HQ 记录更新；如果目标表需要新增记录，系统仍会写入创建该记录所需的完整字段。',
            )}
          </div>
        </Space>
      )}
    </Modal>
  )
}
