import {
  CheckOutlined,
  CloseOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Drawer,
  Empty,
  Form,
  Image,
  Input,
  Space,
  Spin,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  approveAdminSensitiveChangeRequest,
  getAdminEmployeeProfile,
  getAdminSensitiveChangeRequest,
  getAdminSensitiveChangeRequests,
  rejectAdminSensitiveChangeRequest,
} from '../../../services/employeeProfileService'
import type {
  EmployeeProfileDetailDto,
  EmployeeProfileSensitiveChangeDetailDto,
  EmployeeProfileSensitiveChangeStatus,
  EmployeeProfileSensitiveChangeSummaryDto,
} from '../../../types/employeeProfile'
import {
  getChangedSensitiveFields,
  handleSensitiveReviewFailure,
  isRejectReasonValid,
  maskSensitiveSummary,
  type SensitiveProfileField,
} from './logic'

interface SensitiveChangeReviewPanelProps {
  refreshPendingCount: () => Promise<void>
}

interface ReviewListRow extends EmployeeProfileSensitiveChangeSummaryDto {
  changedFields: SensitiveProfileField[] | null
}

interface ReviewFormValues {
  reason?: string
}

const STATUS_COLORS: Record<EmployeeProfileSensitiveChangeStatus, string> = {
  Pending: 'gold',
  Approved: 'green',
  Rejected: 'red',
  Superseded: 'default',
}

function formatSensitiveValue(value: unknown) {
  return typeof value === 'string' && value.trim() ? value : '--'
}

export default function SensitiveChangeReviewPanel({
  refreshPendingCount,
}: SensitiveChangeReviewPanelProps) {
  const { t, i18n } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [listError, setListError] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [rows, setRows] = useState<ReviewListRow[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [reviewOpen, setReviewOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [reviewLoading, setReviewLoading] = useState(false)
  const [detailError, setDetailError] = useState(false)
  const [reviewDetail, setReviewDetail] = useState<EmployeeProfileSensitiveChangeDetailDto | null>(null)
  const [currentProfile, setCurrentProfile] = useState<EmployeeProfileDetailDto | null>(null)
  const [reviewForm] = Form.useForm<ReviewFormValues>()

  const formatDateTime = (value?: string) => {
    if (!value) return '--'
    return new Intl.DateTimeFormat(i18n.language, {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    }).format(new Date(value))
  }

  const fieldLabel = (field: SensitiveProfileField) =>
    t(`system.employeeProfiles.review.fields.${field}`)

  const calculateChangedFields = async (summary: EmployeeProfileSensitiveChangeSummaryDto) => {
    try {
      // 列表只保留字段名。完整敏感值只在本次计算的局部变量中短暂存在，不写入列表状态或日志。
      const [detail, profile] = await Promise.all([
        getAdminSensitiveChangeRequest(summary.requestId),
        getAdminEmployeeProfile(summary.userGuid),
      ])
      return getChangedSensitiveFields(
        {
          ...profile,
          bankAccountNumber: maskSensitiveSummary(profile.bankAccountNumber),
          superannuationAccountNumber: maskSensitiveSummary(profile.superannuationAccountNumber),
          identityId: maskSensitiveSummary(profile.identityId),
        },
        {
          ...detail,
          bankAccountNumber: maskSensitiveSummary(detail.bankAccountNumber),
          superannuationAccountNumber: maskSensitiveSummary(detail.superannuationAccountNumber),
          identityId: maskSensitiveSummary(detail.identityId),
        },
      )
    } catch {
      return null
    }
  }

  const loadList = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    setListError(false)
    try {
      const result = await getAdminSensitiveChangeRequests({
        page: nextPage,
        pageSize: nextPageSize,
        status: 'Pending',
        keyword: keyword || undefined,
      })
      const changedFields = await Promise.all(result.items.map(calculateChangedFields))
      setRows(result.items.map((item, index) => ({ ...item, changedFields: changedFields[index] ?? null })))
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch {
      setListError(true)
      message.error(t('system.employeeProfiles.review.loadListFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadList(1, pageSize)
  }, [])

  const loadReviewDetail = async (requestId: number) => {
    setDetailLoading(true)
    setDetailError(false)
    try {
      const detail = await getAdminSensitiveChangeRequest(requestId)
      const profile = await getAdminEmployeeProfile(detail.userGuid)
      setReviewDetail(detail)
      setCurrentProfile(profile)
    } catch {
      setDetailError(true)
      message.error(t('system.employeeProfiles.review.loadDetailFailed'))
    } finally {
      setDetailLoading(false)
    }
  }

  const openReview = (record: ReviewListRow) => {
    setReviewOpen(true)
    setReviewDetail(null)
    setCurrentProfile(null)
    reviewForm.resetFields()
    void loadReviewDetail(record.requestId)
  }

  const closeReview = () => {
    setReviewOpen(false)
    setReviewDetail(null)
    setCurrentProfile(null)
    setDetailError(false)
    reviewForm.resetFields()
  }

  const submitReview = async (action: 'approve' | 'reject') => {
    if (!reviewDetail || reviewDetail.status !== 'Pending') {
      return
    }

    const reason = reviewForm.getFieldValue('reason')?.trim()
    if (action === 'reject' && !isRejectReasonValid(reason)) {
      reviewForm.setFields([{ name: 'reason', errors: [t('system.employeeProfiles.review.reasonRequired')] }])
      return
    }
    reviewForm.setFields([{ name: 'reason', errors: [] }])

    const requestId = reviewDetail.requestId
    setReviewLoading(true)
    try {
      if (action === 'approve') {
        await approveAdminSensitiveChangeRequest(requestId, { reason: reason || undefined })
      } else {
        await rejectAdminSensitiveChangeRequest(requestId, { reason: reason! })
      }
      message.success(t(`system.employeeProfiles.review.${action}Success`))
      closeReview()
      await Promise.all([loadList(page, pageSize), refreshPendingCount()])
    } catch (error) {
      const conflictHandled = await handleSensitiveReviewFailure(
        error,
        () => loadReviewDetail(requestId),
        async () => {
          await Promise.all([loadList(page, pageSize), refreshPendingCount()])
        },
      )
      message[conflictHandled ? 'warning' : 'error'](
        t(conflictHandled
          ? 'system.employeeProfiles.review.versionConflict'
          : `system.employeeProfiles.review.${action}Failed`),
      )
    } finally {
      setReviewLoading(false)
    }
  }

  const columns: ColumnsType<ReviewListRow> = [
    {
      title: t('system.employeeProfiles.review.employee'),
      key: 'employee',
      render: (_, record) => (
        <Typography.Text strong>{record.username || '--'}</Typography.Text>
      ),
    },
    {
      title: t('system.employeeProfiles.review.changedFields'),
      dataIndex: 'changedFields',
      render: (fields: SensitiveProfileField[] | null) => fields === null
        ? <Typography.Text type="secondary">{t('system.employeeProfiles.review.viewDetailForFields')}</Typography.Text>
        : fields.length > 0
          ? <Space size={[4, 4]} wrap>{fields.map((field) => <Tag key={field}>{fieldLabel(field)}</Tag>)}</Space>
          : '--',
    },
    {
      title: t('system.employeeProfiles.review.submittedAt'),
      dataIndex: 'submittedAt',
      width: 180,
      render: (value: string) => formatDateTime(value),
    },
    {
      title: t('system.employeeProfiles.review.status'),
      dataIndex: 'status',
      width: 110,
      render: (status: EmployeeProfileSensitiveChangeStatus) => (
        <Tag color={STATUS_COLORS[status]}>{t(`system.employeeProfiles.review.statuses.${status}`)}</Tag>
      ),
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 100,
      render: (_, record) => (
        <Button type="link" onClick={() => openReview(record)}>
          {t('system.employeeProfiles.review.reviewAction')}
        </Button>
      ),
    },
  ]

  const comparisonRows = useMemo(() => {
    if (!reviewDetail || !currentProfile) {
      return []
    }
    const definitions: Array<{
      key: SensitiveProfileField
      current: unknown
      proposed: unknown
    }> = [
      { key: 'bankBsb', current: currentProfile.bankBsb, proposed: reviewDetail.bankBsb },
      { key: 'bankAccountNumber', current: currentProfile.bankAccountNumber, proposed: reviewDetail.bankAccountNumber },
      { key: 'superannuationCompanyName', current: currentProfile.superannuationCompanyName, proposed: reviewDetail.superannuationCompanyName },
      { key: 'superannuationCompanyCode', current: currentProfile.superannuationCompanyCode, proposed: reviewDetail.superannuationCompanyCode },
      { key: 'superannuationAccountNumber', current: currentProfile.superannuationAccountNumber, proposed: reviewDetail.superannuationAccountNumber },
      { key: 'identityType', current: currentProfile.identityType, proposed: reviewDetail.identityType },
      { key: 'identityId', current: currentProfile.identityId, proposed: reviewDetail.identityId },
      { key: 'identityPhotoUrl', current: currentProfile.identityPhotoUrl, proposed: reviewDetail.identityPhotoUrl },
    ]
    const changed = new Set(getChangedSensitiveFields(currentProfile, reviewDetail))
    return definitions.map((item) => ({ ...item, changed: changed.has(item.key) }))
  }, [currentProfile, reviewDetail])

  return (
    <>
      <Space wrap style={{ marginBottom: 16 }}>
        <Input
          allowClear
          prefix={<SearchOutlined />}
          placeholder={t('system.employeeProfiles.review.searchPlaceholder')}
          style={{ width: 300 }}
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          onPressEnter={() => void loadList(1, pageSize)}
        />
        <Button type="primary" onClick={() => void loadList(1, pageSize)}>{t('common.query')}</Button>
        <Button icon={<ReloadOutlined />} onClick={() => void loadList(page, pageSize)}>{t('common.refresh')}</Button>
      </Space>

      {listError ? (
        <Alert
          type="error"
          showIcon
          message={t('system.employeeProfiles.review.loadListFailed')}
          action={<Button size="small" onClick={() => void loadList(page, pageSize)}>{t('common.retry')}</Button>}
          style={{ marginBottom: 16 }}
        />
      ) : null}

      <Table
        rowKey="requestId"
        loading={loading}
        columns={columns}
        dataSource={rows}
        locale={{ emptyText: <Empty description={t('system.employeeProfiles.review.empty')} /> }}
        scroll={{ x: 880 }}
        pagination={{
          current: page,
          pageSize,
          total,
          onChange: (nextPage, nextPageSize) => void loadList(nextPage, nextPageSize),
        }}
      />

      <Drawer
        width={920}
        destroyOnHidden
        open={reviewOpen}
        onClose={closeReview}
        title={t('system.employeeProfiles.review.drawerTitle', {
          name: reviewDetail?.username || reviewDetail?.userGuid || '',
        })}
        extra={reviewDetail?.status === 'Pending' ? (
          <Space>
            <Button
              danger
              icon={<CloseOutlined />}
              loading={reviewLoading}
              onClick={() => void submitReview('reject')}
            >
              {t('system.employeeProfiles.review.reject')}
            </Button>
            <Button
              type="primary"
              icon={<CheckOutlined />}
              loading={reviewLoading}
              onClick={() => void submitReview('approve')}
            >
              {t('system.employeeProfiles.review.approve')}
            </Button>
          </Space>
        ) : null}
      >
        {detailLoading && !reviewDetail ? (
          <Spin tip={t('system.employeeProfiles.review.loadingDetail')} />
        ) : detailError || !reviewDetail || !currentProfile ? (
          <Alert type="error" showIcon message={t('system.employeeProfiles.review.detailUnavailable')} />
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Alert
              type="info"
              showIcon
              message={t('system.employeeProfiles.review.authorizedDetailNotice')}
            />
            <Table
              rowKey="key"
              size="small"
              bordered
              pagination={false}
              dataSource={comparisonRows}
              columns={[
                {
                  title: t('system.employeeProfiles.review.field'),
                  dataIndex: 'key',
                  width: 210,
                  render: (field: SensitiveProfileField, record) => (
                    <Space>
                      <Typography.Text>{fieldLabel(field)}</Typography.Text>
                      {record.changed ? <Tag color="gold">{t('system.employeeProfiles.review.changed')}</Tag> : null}
                    </Space>
                  ),
                },
                {
                  title: t('system.employeeProfiles.review.currentValue'),
                  dataIndex: 'current',
                  render: (value: unknown, record) => record.key === 'identityPhotoUrl'
                    ? (value ? t('system.employeeProfiles.review.hasPhoto') : t('system.employeeProfiles.review.noPhoto'))
                    : <Typography.Text>{formatSensitiveValue(value)}</Typography.Text>,
                },
                {
                  title: t('system.employeeProfiles.review.proposedValue'),
                  dataIndex: 'proposed',
                  render: (value: unknown, record) => record.key === 'identityPhotoUrl'
                    ? (value ? t('system.employeeProfiles.review.hasPhoto') : t('system.employeeProfiles.review.noPhoto'))
                    : <Typography.Text strong={record.changed}>{formatSensitiveValue(value)}</Typography.Text>,
                },
              ]}
            />

            {(currentProfile.identityPhotoUrl || reviewDetail.identityPhotoUrl) ? (
              <Space align="start" size={24} wrap>
                <Space direction="vertical">
                  <Typography.Text strong>{t('system.employeeProfiles.review.currentPhoto')}</Typography.Text>
                  {currentProfile.identityPhotoUrl
                    ? <Image src={currentProfile.identityPhotoUrl} width={220} style={{ borderRadius: 6 }} />
                    : <Typography.Text type="secondary">{t('system.employeeProfiles.review.noPhoto')}</Typography.Text>}
                </Space>
                <Space direction="vertical">
                  <Typography.Text strong>{t('system.employeeProfiles.review.proposedPhoto')}</Typography.Text>
                  {reviewDetail.identityPhotoUrl
                    ? <Image src={reviewDetail.identityPhotoUrl} width={220} style={{ borderRadius: 6 }} />
                    : <Typography.Text type="secondary">{t('system.employeeProfiles.review.noPhoto')}</Typography.Text>}
                </Space>
              </Space>
            ) : null}

            <Form form={reviewForm} layout="vertical">
              <Form.Item
                name="reason"
                label={t('system.employeeProfiles.review.reason')}
                extra={t('system.employeeProfiles.review.reasonHint')}
              >
                <Input.TextArea rows={3} maxLength={1000} showCount />
              </Form.Item>
            </Form>
          </Space>
        )}
      </Drawer>
    </>
  )
}
