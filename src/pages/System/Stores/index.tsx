import { EditOutlined, EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Descriptions,
  Drawer,
  Form,
  Input,
  Modal,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import { getStoreByGuid, getStores, updateStore } from '../../../services/storeService'
import type { StoreDetailDto, StoreDto, UpdateStoreDto } from '../../../types/store'
import StoreUserManagement from './StoreUserManagement'

export default function SystemStoresPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<StoreDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailStore, setDetailStore] = useState<StoreDetailDto | null>(null)
  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editingStore, setEditingStore] = useState<StoreDetailDto | null>(null)
  const [storeUserOpen, setStoreUserOpen] = useState(false)
  const [storeUserTarget, setStoreUserTarget] = useState<StoreDto | null>(null)
  const [form] = Form.useForm<UpdateStoreDto>()

  const loadData = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getStores({
        page: nextPage,
        pageSize: nextPageSize,
        search: keyword || undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.loadListFailed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const reloadStoreDetail = async (storeGuid: string) => {
    const detail = await getStoreByGuid(storeGuid)
    setDetailStore(detail)
    return detail
  }

  const handleViewDetail = async (record: StoreDto) => {
    setDetailOpen(true)
    setDetailLoading(true)
    setDetailStore(null)
    try {
      const detail = await getStoreByGuid(record.storeGUID)
      setDetailStore(detail)
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.loadDetailFailed'))
      setDetailOpen(false)
    } finally {
      setDetailLoading(false)
    }
  }

  const handleEdit = async (record: StoreDto) => {
    setEditOpen(true)
    setEditLoading(true)
    setEditingStore(null)
    form.resetFields()
    try {
      const detail = await getStoreByGuid(record.storeGUID)
      setEditingStore(detail)
      form.setFieldsValue({
        storeName: detail.storeName,
        storeCode: detail.storeCode,
        description: detail.description,
        address: detail.address,
        contactPhone: detail.contactPhone,
        contactEmail: detail.contactEmail,
        abn: detail.abn,
        brandName: detail.brandName,
        isActive: detail.isActive,
      })
    } catch (error) {
      console.error(error)
      message.error(t('system.stores.loadEditFailed'))
      setEditOpen(false)
    } finally {
      setEditLoading(false)
    }
  }

  const handleEditSubmit = async () => {
    if (!editingStore) {
      return
    }

    try {
      const values = await form.validateFields()
      setEditLoading(true)
      const updated = await updateStore(editingStore.storeGUID, values)
      message.success(t('system.stores.updateSuccess'))
      setEditOpen(false)
      setEditingStore(updated)
      form.resetFields()
      if (detailStore?.storeGUID === updated.storeGUID) {
        setDetailStore((current) => (current ? { ...current, ...updated } : updated))
      }
      void loadData(page, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(t('system.stores.updateFailed'))
    } finally {
      setEditLoading(false)
    }
  }

  const handleOpenStoreUsers = (store: StoreDto) => {
    setStoreUserTarget(store)
    setStoreUserOpen(true)
  }

  const columns: ColumnsType<StoreDto> = [
    { title: t('system.stores.storeName'), dataIndex: 'storeName', width: 240 },
    { title: t('system.stores.storeCode'), dataIndex: 'storeCode', width: 140 },
    { title: t('system.stores.brandName'), dataIndex: 'brandName', width: 180, render: (value) => value || '--' },
    { title: t('system.stores.contactPhone'), dataIndex: 'contactPhone', width: 160, render: (value) => value || '--' },
    {
      title: t('system.stores.linkedUserCount'),
      dataIndex: 'totalUsers',
      width: 120,
      render: (value: number | undefined, record) => (
        <Button type="link" style={{ paddingInline: 0 }} onClick={() => handleOpenStoreUsers(record)}>
          {value ?? 0}
        </Button>
      ),
    },
    {
      title: t('column.status'),
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.active') : t('common.inactive')}</Tag>
      ),
    },
    {
      title: t('column.action'),
      key: 'action',
      width: 160,
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            {t('common.view')}
          </Button>
          <HasPermission code={P.Stores.Edit}>
            <Button type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
              {t('common.edit')}
            </Button>
          </HasPermission>
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title={t('system.stores.pageTitle')}
      subtitle={t('system.stores.pageSubtitle')}
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('system.stores.searchPlaceholder')}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 260 }}
            allowClear
          />
          <Button type="primary" onClick={() => void loadData(1, pageSize)}>
            {t('common.query')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize)}>
            {t('common.refresh')}
          </Button>
        </Space>

        <Table
          rowKey="storeGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 980 }}
          pagination={{
            current: page,
            pageSize,
            total,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize)
            },
          }}
        />
      </Card>

      <Drawer
        title={detailStore ? t('system.stores.detailTitle', { name: detailStore.storeCode }) : t('system.stores.detailTitleShort')}
        width={860}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailStore(null)
        }}
        destroyOnHidden
        extra={
          detailStore ? (
            <HasPermission code={P.Stores.Edit}>
              <Button type="primary" onClick={() => handleOpenStoreUsers(detailStore)}>
                {t('system.stores.manageUsers')}
              </Button>
            </HasPermission>
          ) : null
        }
      >
        {detailLoading ? (
          <Typography.Text type="secondary">{t('system.stores.loadingDetail')}</Typography.Text>
        ) : !detailStore ? (
          <Typography.Text type="danger">{t('system.stores.notFound')}</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2}>
              <Descriptions.Item label={t('system.stores.storeName')}>{detailStore.storeName}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.storeCode')}>{detailStore.storeCode}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.brandName')}>{detailStore.brandName || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.contactPhone')}>{detailStore.contactPhone || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('system.stores.contactEmail')}>{detailStore.contactEmail || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('column.status')}>
                <Tag color={detailStore.isActive ? 'success' : 'default'}>
                  {detailStore.isActive ? t('common.active') : t('common.inactive')}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.stores.userCount')}>
                <Button type="link" style={{ paddingInline: 0 }} onClick={() => handleOpenStoreUsers(detailStore)}>
                  {detailStore.activeUsers ?? 0} / {detailStore.totalUsers ?? 0}
                </Button>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.stores.address')} span={2}>
                {detailStore.address || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.description')} span={2}>
                {detailStore.description || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('column.createTime')}>{detailStore.createdAt}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.updatedAt')}>{detailStore.updatedAt}</Descriptions.Item>
            </Descriptions>
          </Space>
        )}
      </Drawer>

      <Modal
        title={editingStore ? t('system.stores.editTitle', { name: editingStore.storeCode }) : t('system.stores.editTitleShort')}
        open={editOpen}
        onCancel={() => {
          setEditOpen(false)
          setEditingStore(null)
          form.resetFields()
        }}
        onOk={() => void handleEditSubmit()}
        confirmLoading={editLoading}
        width={720}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item label={t('system.stores.storeName')} name="storeName" rules={[{ required: true, message: t('system.stores.storeNameRequired') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.storeCode')} name="storeCode" rules={[{ required: true, message: t('system.stores.storeCodeRequired') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.brandName')} name="brandName">
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.contactPhone')} name="contactPhone">
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.contactEmail')} name="contactEmail" rules={[{ type: 'email', message: t('system.users.emailInvalid') }]}>
            <Input />
          </Form.Item>
          <Form.Item label={t('system.stores.address')} name="address">
            <Input />
          </Form.Item>
          <Form.Item label={t('column.description')} name="description">
            <Input.TextArea rows={4} />
          </Form.Item>
          <Form.Item label={t('column.status')} name="isActive" valuePropName="checked">
            <Switch checkedChildren={t('common.active')} unCheckedChildren={t('common.inactive')} />
          </Form.Item>
        </Form>
      </Modal>

      <StoreUserManagement
        open={storeUserOpen}
        store={storeUserTarget}
        onClose={() => {
          setStoreUserOpen(false)
          setStoreUserTarget(null)
        }}
        onChanged={() => {
          if (storeUserTarget) {
            if (detailStore?.storeGUID === storeUserTarget.storeGUID) {
              void reloadStoreDetail(storeUserTarget.storeGUID)
            }
            void loadData(page, pageSize)
          }
        }}
      />
    </PageContainer>
  )
}
