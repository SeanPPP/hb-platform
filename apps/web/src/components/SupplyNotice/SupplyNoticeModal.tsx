import { DatePicker, Form, Input, Modal, Radio, Segmented, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { registerPageMessages } from '../../i18n/registerPageMessages'
import type { SupplyExpectedPrecision, SupplyNoticeInput, SupplyPlan, WarehouseSupplyNotice } from '../../types/supplyNotice'
import { supplyNoticeMessages } from './supplyNoticeMessages'

registerPageMessages(supplyNoticeMessages)

const PLANS: SupplyPlan[] = ['WillRestock', 'Undecided', 'Seasonal', 'Discontinued']
const PRECISIONS: SupplyExpectedPrecision[] = ['Unknown', 'Day', 'Range', 'Month']

interface SupplyNoticeFormValues {
  supplyPlan?: SupplyPlan
  expectedPrecision: SupplyExpectedPrecision
  expectedDay?: Dayjs
  expectedRange?: [Dayjs, Dayjs]
  expectedMonth?: Dayjs
  storeFacingNote?: string
  internalNote?: string
}

export interface SupplyNoticeModalProps {
  open: boolean
  /** 本次影响的商品数，用于标题下的说明。 */
  productCount: number
  /** delist：随下架一起提交；edit：修改已下架商品的说明。 */
  mode: 'delist' | 'edit'
  /** 修改时的现值；新登记时可为空。 */
  initialNotice?: WarehouseSupplyNotice | null
  confirmLoading?: boolean
  onCancel: () => void
  onSubmit: (notice: SupplyNoticeInput) => Promise<void> | void
}

function toFormValues(notice: WarehouseSupplyNotice | null | undefined): SupplyNoticeFormValues {
  if (!notice) {
    return { expectedPrecision: 'Unknown' }
  }
  const from = notice.expectedFrom ? dayjs(notice.expectedFrom) : undefined
  const to = notice.expectedTo ? dayjs(notice.expectedTo) : undefined
  return {
    supplyPlan: notice.supplyPlan,
    expectedPrecision: notice.expectedPrecision,
    expectedDay: notice.expectedPrecision === 'Day' ? from : undefined,
    expectedRange: notice.expectedPrecision === 'Range' && from && to ? [from, to] : undefined,
    expectedMonth: notice.expectedPrecision === 'Month' ? from : undefined,
    storeFacingNote: notice.storeFacingNote ?? undefined,
    internalNote: notice.internalNote ?? undefined,
  }
}

function toInput(values: SupplyNoticeFormValues): SupplyNoticeInput {
  const format = (value?: Dayjs | null) => (value ? value.format('YYYY-MM-DD') : null)
  // 后端会按精度归一化（某月展开为整月、某日收成同一天），这里只负责把控件值放到对应字段。
  switch (values.expectedPrecision) {
    case 'Day':
      return { supplyPlan: values.supplyPlan!, expectedPrecision: 'Day', expectedFrom: format(values.expectedDay), storeFacingNote: values.storeFacingNote, internalNote: values.internalNote }
    case 'Range':
      return { supplyPlan: values.supplyPlan!, expectedPrecision: 'Range', expectedFrom: format(values.expectedRange?.[0]), expectedTo: format(values.expectedRange?.[1]), storeFacingNote: values.storeFacingNote, internalNote: values.internalNote }
    case 'Month':
      return { supplyPlan: values.supplyPlan!, expectedPrecision: 'Month', expectedFrom: format(values.expectedMonth?.startOf('month')), storeFacingNote: values.storeFacingNote, internalNote: values.internalNote }
    default:
      return { supplyPlan: values.supplyPlan!, expectedPrecision: 'Unknown', storeFacingNote: values.storeFacingNote, internalNote: values.internalNote }
  }
}

/**
 * 仓库端的供货说明弹窗：下架时与单个 / 批量共用，也用于修改已下架商品的说明。
 * 后续计划必选且不预选，避免顺手全选“会补货”让门店误以为迟早会回来。
 */
export default function SupplyNoticeModal({ open, productCount, mode, initialNotice, confirmLoading, onCancel, onSubmit }: SupplyNoticeModalProps) {
  const { t } = useTranslation()
  const [form] = Form.useForm<SupplyNoticeFormValues>()
  const plan = Form.useWatch('supplyPlan', form)
  const precision = Form.useWatch('expectedPrecision', form) ?? 'Unknown'
  const initialValues = useMemo(() => toFormValues(initialNotice), [initialNotice])

  useEffect(() => {
    if (open) {
      form.setFieldsValue(initialValues)
    }
  }, [open, form, initialValues])

  // 不再供应的商品没有“预计恢复时间”，直接隐藏时间控件。
  const showExpected = plan !== 'Discontinued'

  const handleOk = async () => {
    const values = await form.validateFields()
    await onSubmit(toInput(values))
  }

  return (
    <Modal
      open={open}
      title={t(mode === 'edit' ? 'supplyNotice.form.titleEdit' : 'supplyNotice.form.title')}
      okText={t(mode === 'edit' ? 'supplyNotice.form.save' : 'supplyNotice.form.confirmDelist')}
      okButtonProps={mode === 'delist' ? { danger: true } : undefined}
      cancelText={t('common.cancel')}
      confirmLoading={confirmLoading}
      onCancel={onCancel}
      onOk={() => void handleOk()}
      destroyOnHidden
      width={520}
    >
      <Typography.Paragraph type="secondary" style={{ marginBottom: 16 }}>
        {t(mode === 'edit' ? 'supplyNotice.form.affectedEdit' : 'supplyNotice.form.affected', { count: productCount })}
      </Typography.Paragraph>
      <Form form={form} layout="vertical" initialValues={initialValues} preserve={false}>
        <Form.Item
          name="supplyPlan"
          label={t('supplyNotice.form.planLabel')}
          rules={[{ required: true, message: t('supplyNotice.form.planRequired') }]}
        >
          <Radio.Group>
            {PLANS.map((value) => (
              <Radio key={value} value={value}>{t(`supplyNotice.plan.${value}`)}</Radio>
            ))}
          </Radio.Group>
        </Form.Item>
        {showExpected ? (
          <Form.Item label={t('supplyNotice.form.expectedLabel')} style={{ marginBottom: 8 }}>
            <Form.Item name="expectedPrecision" noStyle>
              <Segmented
                options={PRECISIONS.map((value) => ({ value, label: t(`supplyNotice.precision.${value}`) }))}
                style={{ marginBottom: 8 }}
              />
            </Form.Item>
            {precision === 'Day' ? (
              <Form.Item name="expectedDay" rules={[{ required: true, message: t('supplyNotice.form.expectedFromRequired') }]} noStyle>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            ) : null}
            {precision === 'Range' ? (
              <Form.Item name="expectedRange" rules={[{ required: true, message: t('supplyNotice.form.expectedRangeRequired') }]} noStyle>
                <DatePicker.RangePicker style={{ width: '100%' }} />
              </Form.Item>
            ) : null}
            {precision === 'Month' ? (
              <Form.Item name="expectedMonth" rules={[{ required: true, message: t('supplyNotice.form.expectedMonthRequired') }]} noStyle>
                <DatePicker picker="month" style={{ width: '100%' }} />
              </Form.Item>
            ) : null}
          </Form.Item>
        ) : null}
        <Form.Item name="storeFacingNote" label={t('supplyNotice.form.storeNoteLabel')}>
          <Input.TextArea rows={2} maxLength={500} showCount placeholder={t('supplyNotice.form.storeNotePlaceholder')} />
        </Form.Item>
        <Form.Item name="internalNote" label={t('supplyNotice.form.internalNoteLabel')}>
          <Input.TextArea rows={2} maxLength={500} placeholder={t('supplyNotice.form.internalNotePlaceholder')} />
        </Form.Item>
      </Form>
    </Modal>
  )
}
