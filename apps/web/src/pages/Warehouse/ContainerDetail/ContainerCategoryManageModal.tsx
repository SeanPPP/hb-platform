import {
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Empty,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Spin,
  Switch,
  Typography,
  message,
} from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  createWarehouseCategory,
  deleteWarehouseCategory,
  getCategoryTree,
  type SaveWarehouseCategoryPayload,
  type WarehouseCategoryNode,
  updateWarehouseCategory,
} from '../../../services/warehouseCategoryService'
import CategoryTreePicker from '../Products/CategoryTreePicker'
import {
  buildContainerCategoryParentOptions,
  collectContainerCategoryExpandedGuids,
  executeContainerCategoryMutation,
  findContainerCategory,
  resolveContainerCategorySelectionAfterRefresh,
  type ContainerCategoryChange,
} from './containerCategoryManageLogic'
import './ContainerCategoryManageModal.css'

type CategoryFormMode = 'idle' | 'create' | 'edit'

interface ContainerCategoryFormValues extends SaveWarehouseCategoryPayload {
  isActive: boolean
}

export interface ContainerCategoryManageModalProps {
  open: boolean
  categories: WarehouseCategoryNode[]
  language?: string
  activeTargetCategoryGuid?: string
  onCancel: () => void
  onMutationCommitted: (change: ContainerCategoryChange) => void
  onCategoriesChanged: (
    tree: WarehouseCategoryNode[],
    change: ContainerCategoryChange,
  ) => void
}

function getCategoryFormValues(category: WarehouseCategoryNode): ContainerCategoryFormValues {
  return {
    categoryName: category.categoryName,
    chineseName: category.chineseName,
    parentGUID: category.parentGUID,
    isActive: category.isActive,
    remarks: category.remarks,
  }
}

function getEmptyCategoryFormValues(parentGUID?: string): ContainerCategoryFormValues {
  return {
    categoryName: '',
    chineseName: '',
    parentGUID,
    isActive: true,
    remarks: '',
  }
}

export default function ContainerCategoryManageModal({
  open,
  categories,
  language,
  activeTargetCategoryGuid,
  onCancel,
  onMutationCommitted,
  onCategoriesChanged,
}: ContainerCategoryManageModalProps) {
  const { t } = useTranslation()
  const [form] = Form.useForm<ContainerCategoryFormValues>()
  const [managedCategories, setManagedCategories] = useState(categories)
  const [expandedKeys, setExpandedKeys] = useState<string[]>([])
  const [selectedCategoryGuid, setSelectedCategoryGuid] = useState<string>()
  const [formMode, setFormMode] = useState<CategoryFormMode>('idle')
  const [saving, setSaving] = useState(false)
  const [pendingRefreshChange, setPendingRefreshChange] = useState<ContainerCategoryChange>()
  const wasOpenRef = useRef(false)

  const selectedCategory = useMemo(
    () => findContainerCategory(managedCategories, selectedCategoryGuid),
    [managedCategories, selectedCategoryGuid],
  )
  const parentOptions = useMemo(
    () => buildContainerCategoryParentOptions(
      managedCategories,
      formMode === 'edit' ? selectedCategoryGuid : undefined,
      language,
    ),
    [formMode, language, managedCategories, selectedCategoryGuid],
  )

  useEffect(() => {
    setManagedCategories(categories)
  }, [categories])

  useEffect(() => {
    if (!open) {
      wasOpenRef.current = false
      return
    }

    if (wasOpenRef.current) {
      return
    }
    wasOpenRef.current = true

    setManagedCategories(categories)
    setExpandedKeys(collectContainerCategoryExpandedGuids(categories, 2))

    if (pendingRefreshChange) {
      setSelectedCategoryGuid(undefined)
      setFormMode('idle')
      form.resetFields()
      return
    }

    const activeCategory = findContainerCategory(categories, activeTargetCategoryGuid)
    if (activeCategory) {
      setSelectedCategoryGuid(activeCategory.categoryGUID)
      setFormMode('edit')
      form.setFieldsValue(getCategoryFormValues(activeCategory))
      return
    }

    setSelectedCategoryGuid(undefined)
    setFormMode('create')
    form.setFieldsValue(getEmptyCategoryFormValues())
  }, [activeTargetCategoryGuid, categories, form, open, pendingRefreshChange])

  const showEditForm = (category: WarehouseCategoryNode) => {
    setSelectedCategoryGuid(category.categoryGUID)
    setFormMode('edit')
    form.setFieldsValue(getCategoryFormValues(category))
  }

  const handleSelectCategory = (categoryGuid?: string) => {
    if (saving || pendingRefreshChange) {
      return
    }

    const category = findContainerCategory(managedCategories, categoryGuid)
    if (category) {
      showEditForm(category)
      return
    }

    setSelectedCategoryGuid(undefined)
    setFormMode('idle')
    form.resetFields()
  }

  const handleCreateRoot = () => {
    if (saving || pendingRefreshChange) {
      return
    }

    setSelectedCategoryGuid(undefined)
    setFormMode('create')
    form.setFieldsValue(getEmptyCategoryFormValues())
  }

  const handleCreateChild = () => {
    if (!selectedCategory || saving || pendingRefreshChange) {
      return
    }

    setFormMode('create')
    form.setFieldsValue(getEmptyCategoryFormValues(selectedCategory.categoryGUID))
  }

  const handleEditCategory = () => {
    if (!selectedCategory || saving || pendingRefreshChange) {
      return
    }

    showEditForm(selectedCategory)
  }

  const applyRefreshedTree = (
    tree: WarehouseCategoryNode[],
    change: ContainerCategoryChange,
  ) => {
    const selection = resolveContainerCategorySelectionAfterRefresh(
      tree,
      selectedCategoryGuid,
      activeTargetCategoryGuid,
      change,
    )
    const nextSelectedCategory = findContainerCategory(tree, selection.managedCategoryGuid)

    setManagedCategories(tree)
    setExpandedKeys(collectContainerCategoryExpandedGuids(tree, 2))
    setSelectedCategoryGuid(selection.managedCategoryGuid)

    if (nextSelectedCategory) {
      setFormMode('edit')
      form.setFieldsValue(getCategoryFormValues(nextSelectedCategory))
    } else {
      setFormMode('idle')
      form.resetFields()
    }

    onCategoriesChanged(tree, change)
  }

  const applyCommittedMutation = (change: ContainerCategoryChange) => {
    if (change.kind === 'create') {
      // 新增已落库但树刷新失败时锁住表单，避免用户再次保存出重复分类。
      setSelectedCategoryGuid(change.categoryGuid)
      setFormMode('idle')
      form.resetFields()
    } else if (change.kind === 'delete') {
      setSelectedCategoryGuid(undefined)
      setFormMode('idle')
      form.resetFields()
    }

    onMutationCommitted(change)
  }

  const showRefreshWarning = (change: ContainerCategoryChange, error: unknown) => {
    setPendingRefreshChange(change)
    console.error(error)
    message.warning(
      t(
        'warehouse.categories.refreshAfterMutationFailed',
        '操作已成功，但分类列表刷新失败，请点击“重试”刷新。',
      ),
    )
  }

  const handleRetryRefresh = async () => {
    if (!pendingRefreshChange || saving) {
      return
    }

    setSaving(true)
    try {
      const tree = await getCategoryTree()
      const change = pendingRefreshChange
      setPendingRefreshChange(undefined)
      applyRefreshedTree(tree, change)
      message.success(t('warehouse.categories.refreshSuccess', '分类列表已刷新'))
    } catch (error) {
      showRefreshWarning(pendingRefreshChange, error)
    } finally {
      setSaving(false)
    }
  }

  const handleSave = async () => {
    if (saving || pendingRefreshChange || formMode === 'idle') {
      return
    }

    let values: ContainerCategoryFormValues
    try {
      values = await form.validateFields()
    } catch {
      return
    }

    setSaving(true)
    try {
      const outcome = await executeContainerCategoryMutation(
        async () => {
          if (formMode === 'create') {
            const created = await createWarehouseCategory(values)
            return {
              kind: 'create',
              categoryGuid: created.categoryGUID,
              fallbackCategoryGuid: created.categoryGUID,
            }
          }

          if (!selectedCategoryGuid) {
            throw new Error(t('warehouse.categories.selectEditFirst', '请先选择要编辑的分类'))
          }

          const updated = await updateWarehouseCategory(selectedCategoryGuid, values)
          return {
            kind: 'update',
            categoryGuid: updated.categoryGUID,
            fallbackCategoryGuid: updated.categoryGUID,
          }
        },
        getCategoryTree,
        applyCommittedMutation,
      )

      message.success(
        outcome.change.kind === 'create'
          ? t('warehouse.categories.createSuccess', '创建分类成功')
          : t('warehouse.categories.updateSuccess', '更新分类成功'),
      )

      if (outcome.tree) {
        setPendingRefreshChange(undefined)
        applyRefreshedTree(outcome.tree, outcome.change)
      } else {
        showRefreshWarning(outcome.change, outcome.refreshError)
      }
    } catch (error) {
      console.error(error)
      message.error(
        error instanceof Error
          ? error.message
          : t('warehouse.categories.saveFailed', '保存分类失败'),
      )
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async () => {
    if (!selectedCategory || saving || pendingRefreshChange) {
      return
    }

    const change: ContainerCategoryChange = {
      kind: 'delete',
      categoryGuid: selectedCategory.categoryGUID,
      fallbackCategoryGuid: selectedCategory.parentGUID,
    }

    setSaving(true)
    try {
      const outcome = await executeContainerCategoryMutation(
        async () => {
          await deleteWarehouseCategory(selectedCategory.categoryGUID)
          return change
        },
        getCategoryTree,
        applyCommittedMutation,
      )

      message.success(t('warehouse.categories.deleteSuccess', '删除分类成功'))
      if (outcome.tree) {
        setPendingRefreshChange(undefined)
        applyRefreshedTree(outcome.tree, outcome.change)
      } else {
        showRefreshWarning(outcome.change, outcome.refreshError)
      }
    } catch (error) {
      console.error(error)
      // 后端对子分类或已关联商品的业务错误会以 Error.message 原样展示。
      message.error(
        error instanceof Error
          ? error.message
          : t('warehouse.categories.deleteFailed', '删除分类失败'),
      )
    } finally {
      setSaving(false)
    }
  }

  const handleCancel = () => {
    if (!saving) {
      onCancel()
    }
  }

  return (
    <Modal
      title={t('containers.modals.categoryManageTitle', '管理分类')}
      open={open}
      width={920}
      destroyOnHidden
      maskClosable={!saving}
      keyboard={!saving}
      closable={!saving}
      onCancel={handleCancel}
      footer={(
        <Button disabled={saving} onClick={handleCancel}>
          {t('common.close', '关闭')}
        </Button>
      )}
    >
      <Spin spinning={saving}>
        <div className="container-category-manage-grid">
          <section className="container-category-manage-panel">
            <div className="container-category-manage-actions">
              <Space size={[8, 8]} wrap>
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  disabled={saving || Boolean(pendingRefreshChange)}
                  onClick={handleCreateRoot}
                >
                  {t('warehouse.categories.addTopCategory', '新增顶级分类')}
                </Button>
                <Button
                  disabled={!selectedCategory || saving || Boolean(pendingRefreshChange)}
                  onClick={handleCreateChild}
                >
                  {t('warehouse.categories.addChildCategory', '新增子分类')}
                </Button>
                <Button
                  icon={<EditOutlined />}
                  disabled={!selectedCategory || saving || Boolean(pendingRefreshChange) || formMode === 'create'}
                  onClick={handleEditCategory}
                >
                  {t('warehouse.categories.editCategory', '编辑分类')}
                </Button>
                <Popconfirm
                  title={t('warehouse.categories.confirmDelete', '确认删除该分类？')}
                  description={t(
                    'warehouse.categories.deleteBlockedHint',
                    '若该分类存在子分类或关联商品，后端会阻止删除。',
                  )}
                  disabled={!selectedCategory || saving || Boolean(pendingRefreshChange) || formMode === 'create'}
                  onConfirm={() => void handleDelete()}
                >
                  <Button
                    danger
                    icon={<DeleteOutlined />}
                    disabled={!selectedCategory || saving || Boolean(pendingRefreshChange) || formMode === 'create'}
                  >
                    {t('warehouse.categories.deleteCategory', '删除分类')}
                  </Button>
                </Popconfirm>
              </Space>
            </div>

            {pendingRefreshChange ? (
              <Alert
                type="warning"
                showIcon
                message={t(
                  'warehouse.categories.refreshAfterMutationFailed',
                  '操作已成功，但分类列表刷新失败，请点击“重试”刷新。',
                )}
                action={(
                  <Button
                    size="small"
                    icon={<ReloadOutlined />}
                    loading={saving}
                    onClick={() => void handleRetryRefresh()}
                  >
                    {t('common.retry', '重试')}
                  </Button>
                )}
              />
            ) : null}

            <CategoryTreePicker
              categories={managedCategories}
              selectedKey={selectedCategoryGuid}
              expandedKeys={expandedKeys}
              onExpand={setExpandedKeys}
              onSelect={handleSelectCategory}
              language={language}
              t={t}
              maxHeight={440}
            />
          </section>

          <section className="container-category-manage-panel">
            {formMode === 'idle' ? (
              <Empty
                description={t(
                  'warehouse.categories.selectEditFirst',
                  '请选择要编辑的分类，或新增顶级分类',
                )}
              />
            ) : (
              <>
                <Typography.Title level={5}>
                  {formMode === 'create'
                    ? t('warehouse.categories.newCategory', '新建分类')
                    : t('warehouse.categories.editCategory', '编辑分类')}
                </Typography.Title>
                <Form
                  form={form}
                  layout="vertical"
                  disabled={saving || Boolean(pendingRefreshChange)}
                  initialValues={{ isActive: true }}
                >
                  <Form.Item
                    label={t('warehouse.categories.categoryName', '分类名称')}
                    name="categoryName"
                    rules={[
                      {
                        required: true,
                        message: t('warehouse.categories.enterCategoryName', '请输入分类名称'),
                      },
                      {
                        max: 100,
                        message: t(
                          'warehouse.categories.categoryNameMax',
                          '分类名称不能超过 100 个字符',
                        ),
                      },
                    ]}
                  >
                    <Input
                      maxLength={100}
                      placeholder={t('warehouse.categories.enterCategoryName', '请输入分类名称')}
                    />
                  </Form.Item>

                  <Form.Item
                    label={t('warehouse.categories.chineseName', '中文名称')}
                    name="chineseName"
                    rules={[{
                      max: 100,
                      message: t(
                        'warehouse.categories.chineseNameMax',
                        '中文名称不能超过 100 个字符',
                      ),
                    }]}
                  >
                    <Input
                      maxLength={100}
                      placeholder={t('warehouse.categories.enterChineseName', '请输入中文名称')}
                    />
                  </Form.Item>

                  <Form.Item
                    label={t('warehouse.categories.parent', '父类')}
                    name="parentGUID"
                  >
                    <Select
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      options={parentOptions}
                      placeholder={t(
                        'warehouse.categories.topCategoryWhenEmpty',
                        '不选择则为顶级分类',
                      )}
                    />
                  </Form.Item>

                  <Form.Item
                    label={t('warehouse.categories.isActive', '状态')}
                    name="isActive"
                    valuePropName="checked"
                  >
                    <Switch
                      checkedChildren={t('common.active', '启用')}
                      unCheckedChildren={t('common.inactive', '停用')}
                    />
                  </Form.Item>

                  <Form.Item
                    label={t('warehouse.categories.remarks', '备注')}
                    name="remarks"
                    rules={[{
                      max: 500,
                      message: t(
                        'warehouse.categories.remarksMax',
                        '备注不能超过 500 个字符',
                      ),
                    }]}
                  >
                    <Input.TextArea
                      rows={4}
                      maxLength={500}
                      showCount
                      placeholder={t('common.enterRemarks', '请输入备注')}
                    />
                  </Form.Item>

                  <div className="container-category-manage-form-actions">
                    <Button
                      type="primary"
                      loading={saving}
                      onClick={() => void handleSave()}
                    >
                      {t('common.save', '保存')}
                    </Button>
                  </div>
                </Form>
              </>
            )}
          </section>
        </div>
      </Spin>
    </Modal>
  )
}
