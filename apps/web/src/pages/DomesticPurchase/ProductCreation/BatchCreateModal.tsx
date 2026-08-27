import {
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  SaveOutlined,
  SettingOutlined,
} from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import {
  Button,
  Col,
  Form,
  Input,
  InputNumber,
  message,
  Modal,
  Popconfirm,
  Row,
  Select,
  Space,
  Steps,
  Switch,
  Tag,
  theme,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import {
  createBatch,
  createSetProductTemplate,
  deactivateSetProductTemplate,
  getActivePrefixes,
  getSetProductTemplate,
  getSetProductTemplates,
  updateSetProductTemplate,
} from '../../../services/domesticProductCreationService'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { BatchInfo, CreateBatchRequest, SetProductTemplateDetail, SetProductTemplatePayload, SetProductTemplateSummary } from '../../../types/domesticProductCreation'
import {
  applyBatchAddProducts,
  buildPreviewItems,
  buildCreateBatchItems,
  createDraftProduct,
  createDraftSetSubItem,
  findInvalidSetProduct,
  normalizeCreateCount,
} from './batchCreateRules'
import type { BatchAddMode, DraftPreviewItem, DraftProductItem, DraftSetSubItem } from './batchCreateRules'
import {
  applyParentColumnPaste,
  applySubItemColumnPaste,
  getNextBatchCreateEditableCell,
} from './batchCreateGridRules'
import type {
  BatchCreateEditableField,
  BatchCreateNavigationDirection,
  BatchCreatePasteField,
} from './batchCreateGridRules'
import PrefixCodeManageModal from './PrefixCodeManageModal'
import { applySetTemplateDraft, buildSetProductTemplatePayload, createSetDraftFromTemplate, validateSetTemplateProduct } from './setTemplateRules'
import { MeasuredTable } from '../../../components/MeasuredTable'

type ProductItem = DraftProductItem
type SetSubItem = DraftSetSubItem
type PreviewItem = DraftPreviewItem

interface SetTemplateFormValues {
  templateName: string
  setProductName: string
  isEnabled: boolean
  subItems: Array<{ productName?: string; privateLabelPrice?: number | null }>
}

const PRODUCT_TABLE_SCROLL_X = 1080
const PREVIEW_TABLE_SCROLL_X = 760
const PARENT_GRID_SCOPE = 'parent'
const PARENT_COMMON_EDITABLE_FIELDS = ['productName', 'privateLabelPrice'] as const
const PARENT_SET_EDITABLE_FIELDS = ['productName', 'privateLabelPrice', 'createCount', 'setQuantity', 'setPrice'] as const
const SUB_ITEM_EDITABLE_FIELDS = ['productName', 'privateLabelPrice'] as const
const NAVIGATION_DIRECTION_BY_KEY: Partial<Record<string, BatchCreateNavigationDirection>> = {
  ArrowUp: 'up',
  ArrowDown: 'down',
  ArrowLeft: 'left',
  ArrowRight: 'right',
}

type BatchCreatePasteTarget =
  | { scope: 'parent'; field: BatchCreatePasteField }
  | { scope: 'subItem'; setKey: string; field: BatchCreatePasteField }

type BatchCreateFocusableCell = {
  focus: () => void
  select?: () => void
  input?: HTMLInputElement | null
  nativeElement?: HTMLElement | null
}

function isSamePasteTarget(left: BatchCreatePasteTarget | null, right: BatchCreatePasteTarget) {
  if (!left || left.scope !== right.scope || left.field !== right.field) return false
  return left.scope === 'parent' || (right.scope === 'subItem' && left.setKey === right.setKey)
}

function getSubItemGridScope(setKey: string) {
  return `subItem:${setKey}`
}

function buildEditableCellKey(scope: string, rowKey: string, field: BatchCreateEditableField) {
  return `${scope}:${rowKey}:${field}`
}

function getSetTemplateErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message.trim() ? error.message : fallback
}

interface BatchCreateModalProps {
  visible: boolean
  onClose: () => void
  onSuccess: (createdBatch?: BatchInfo) => void
}

export default function BatchCreateModal({ visible, onClose, onSuccess }: BatchCreateModalProps) {
  const { t } = useTranslation()
  const { token } = theme.useToken()
  const [form] = Form.useForm()
  const [setTemplateForm] = Form.useForm<SetTemplateFormValues>()
  const [saveTemplateForm] = Form.useForm<{ templateName: string }>()
  const [currentStep, setCurrentStep] = useState(0)
  const [loading, setLoading] = useState(false)
  const [suppliers, setSuppliers] = useState<Array<{ supplierCode: string; supplierName: string }>>([])
  const [prefixCodes, setPrefixCodes] = useState<Array<{ prefixCode: string; prefixName: string; prefixDescription?: string }>>([])
  const [products, setProducts] = useState<ProductItem[]>(() => [
    createDraftProduct(ProductCreationType.NORMAL, 0),
  ])
  // 记录系统自动占位行，避免套用模板时误删用户手动添加的空白行。
  const automaticPlaceholderKeyRef = useRef(products[0]?.key)
  const [submitting, setSubmitting] = useState(false)
  const [manageModalVisible, setManageModalVisible] = useState(false)
  const [selectedSupplier, setSelectedSupplier] = useState<{ code: string; name: string } | null>(null)
  const [batchAddVisible, setBatchAddVisible] = useState(false)
  const [batchAddCount, setBatchAddCount] = useState(5)
  const [batchAddPrice, setBatchAddPrice] = useState<number | null>(null)
  const [batchAddMode, setBatchAddMode] = useState<BatchAddMode>('append')
  const [batchAddType, setBatchAddType] = useState<ProductCreationType>(ProductCreationType.NORMAL)
  const [batchEditNameVisible, setBatchEditNameVisible] = useState(false)
  const [batchEditNameMode, setBatchEditNameMode] = useState<'replace' | 'prefix' | 'suffix'>('replace')
  const [batchEditNameValue, setBatchEditNameValue] = useState('')
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([])
  const [setTemplates, setSetTemplates] = useState<SetProductTemplateSummary[]>([])
  const [managedSetTemplates, setManagedSetTemplates] = useState<SetProductTemplateSummary[]>([])
  const [selectedSetTemplateId, setSelectedSetTemplateId] = useState<string | undefined>()
  const [setTemplateLoading, setSetTemplateLoading] = useState(false)
  const [setTemplateManageVisible, setSetTemplateManageVisible] = useState(false)
  const [setTemplateEditing, setSetTemplateEditing] = useState<SetProductTemplateDetail | null>(null)
  const [setTemplateSaving, setSetTemplateSaving] = useState(false)
  const [saveTemplateVisible, setSaveTemplateVisible] = useState(false)
  const [templateSourceProductKey, setTemplateSourceProductKey] = useState<string | null>(null)
  const [selectedPasteTarget, setSelectedPasteTarget] = useState<BatchCreatePasteTarget | null>(null)
  const editableCellRefs = useRef(new Map<string, BatchCreateFocusableCell>())
  const watchedPrefixCode = Form.useWatch('prefixCode', form) || ''

  const createEmptyProduct = useCallback(
    (type: ProductCreationType, index: number, price?: number | null): ProductItem => createDraftProduct(type, index, price),
    [],
  )

  const loadSuppliers = async () => {
    try {
      setLoading(true)
      const response = await getActiveChinaSuppliers()
      setLoading(false)
      setSuppliers(response || [])
    } catch {
      setLoading(false)
      message.error(t('productCreation.loadSupplierFailed', '加载供应商失败'))
    }
  }

  const handleReset = useCallback(() => {
    setCurrentStep(0)
    form.resetFields()
    setSuppliers([])
    setPrefixCodes([])
    setSelectedSupplier(null)
    const automaticPlaceholder = createEmptyProduct(ProductCreationType.NORMAL, 0)
    automaticPlaceholderKeyRef.current = automaticPlaceholder.key
    setProducts([automaticPlaceholder])
    setManageModalVisible(false)
    setBatchAddVisible(false)
    setBatchAddCount(5)
    setBatchAddPrice(null)
    setBatchAddMode('append')
    setBatchAddType(ProductCreationType.NORMAL)
    setBatchEditNameVisible(false)
    setBatchEditNameMode('replace')
    setBatchEditNameValue('')
    setSelectedRowKeys([])
    setExpandedRowKeys([])
    setSetTemplates([])
    setManagedSetTemplates([])
    setSelectedSetTemplateId(undefined)
    setSetTemplateManageVisible(false)
    setSetTemplateEditing(null)
    setSetTemplateSaving(false)
    setSaveTemplateVisible(false)
    setTemplateSourceProductKey(null)
    setSelectedPasteTarget(null)
    editableCellRefs.current.clear()
    setTemplateForm.resetFields()
    saveTemplateForm.resetFields()
  }, [createEmptyProduct, form, saveTemplateForm, setTemplateForm])

  useEffect(() => {
    if (visible) {
      handleReset()
      loadSuppliers()
    }
  }, [visible, handleReset])

  const loadPrefixes = async (supplierCode: string) => {
    try {
      const response = await getActivePrefixes(supplierCode)
      if (response.success) {
        setPrefixCodes(response.data || [])
      }
    } catch {
      // ignore
    }
  }

  const loadSetTemplates = useCallback(async (supplierCode: string, includeInactive = false) => {
    if (!supplierCode) {
      if (includeInactive) setManagedSetTemplates([])
      else setSetTemplates([])
      return
    }

    try {
      setSetTemplateLoading(true)
      const response = await getSetProductTemplates(supplierCode, includeInactive)
      if (!response.success) {
        message.error(response.message || t('productCreation.loadSetTemplatesFailed', '加载套装模板失败'))
        return
      }
      if (form.getFieldValue('supplierCode') !== supplierCode) return
      if (includeInactive) setManagedSetTemplates(response.data || [])
      else setSetTemplates((response.data || []).filter((template) => template.isEnabled))
    } catch (error) {
      message.error(getSetTemplateErrorMessage(error, t('productCreation.loadSetTemplatesFailed', '加载套装模板失败')))
    } finally {
      setSetTemplateLoading(false)
    }
  }, [form, t])

  const getSetTemplateValidationMessage = useCallback((validationError: ReturnType<typeof validateSetTemplateProduct>) => {
    const messages = {
      missing_set_product_name: t('productCreation.setTemplateSetNameRequired', '请填写套装商品名'),
      missing_sub_items: t('productCreation.setTemplateSubItemsRequired', '模板至少需要一个子项'),
      missing_sub_item_name: t('productCreation.setTemplateSubItemNameRequired', '请完整填写模板子项名称'),
      missing_sub_item_price: t('productCreation.setTemplateSubItemPriceRequired', '请完整填写模板子项零售价'),
      invalid_sub_item_price: t('productCreation.setTemplateSubItemPriceInvalid', '模板子项零售价不能小于 0'),
    }
    return validationError ? messages[validationError] : undefined
  }, [t])

  const handleApplySetTemplate = useCallback(async (templateId: string) => {
    const supplierCode = form.getFieldValue('supplierCode')
    if (!supplierCode) return
    try {
      setSetTemplateLoading(true)
      const response = await getSetProductTemplate(templateId, supplierCode)
      if (!response.success || !response.data) {
        message.error(response.message || t('productCreation.loadSetTemplateFailed', '加载套装模板详情失败'))
        return
      }
      if (!response.data.isEnabled || response.data.supplierCode !== form.getFieldValue('supplierCode')) {
        message.error(t('productCreation.setTemplateUnavailable', '当前供应商不可使用该套装模板'))
        return
      }
      const draft = createSetDraftFromTemplate(response.data, products.length)
      setProducts((current) => applySetTemplateDraft(current, draft, automaticPlaceholderKeyRef.current))
      setExpandedRowKeys((keys) => Array.from(new Set([...keys, draft.key])))
      setSelectedSetTemplateId(undefined)
    } catch (error) {
      message.error(getSetTemplateErrorMessage(error, t('productCreation.loadSetTemplateFailed', '加载套装模板详情失败')))
    } finally {
      setSetTemplateLoading(false)
    }
  }, [form, products.length, t])

  const handleOpenSetTemplateManager = useCallback(() => {
    if (!selectedSupplier?.code) {
      message.warning(t('domesticProducts.selectSupplier', '请选择供应商'))
      return
    }
    setSetTemplateManageVisible(true)
    void loadSetTemplates(selectedSupplier.code, true)
  }, [loadSetTemplates, selectedSupplier?.code, t])

  const handleEditSetTemplate = useCallback(async (templateId: string) => {
    if (!selectedSupplier?.code) return
    try {
      setSetTemplateLoading(true)
      const response = await getSetProductTemplate(templateId, selectedSupplier.code)
      if (!response.success || !response.data) {
        message.error(response.message || t('productCreation.loadSetTemplateFailed', '加载套装模板详情失败'))
        return
      }
      if (response.data.supplierCode !== form.getFieldValue('supplierCode')) return
      setSetTemplateEditing(response.data)
      setTemplateForm.setFieldsValue({
        templateName: response.data.templateName,
        setProductName: response.data.setProductName,
        isEnabled: response.data.isEnabled,
        subItems: response.data.subItems.map((item) => ({
          productName: item.productName,
          privateLabelPrice: item.privateLabelPrice,
        })),
      })
    } catch (error) {
      message.error(getSetTemplateErrorMessage(error, t('productCreation.loadSetTemplateFailed', '加载套装模板详情失败')))
    } finally {
      setSetTemplateLoading(false)
    }
  }, [form, selectedSupplier?.code, setTemplateForm, t])

  const handleSaveSetTemplateEdit = useCallback(async () => {
    if (!selectedSupplier?.code || !setTemplateEditing) return
    try {
      const values = await setTemplateForm.validateFields()
      const editingProduct: ProductItem = {
        key: setTemplateEditing.templateId,
        productType: ProductCreationType.SET,
        productName: values.setProductName,
        subItems: (values.subItems || []).map((item, index) => ({
          key: `template-edit-${index}`,
          productName: item.productName,
          privateLabelPrice: item.privateLabelPrice,
        })),
      }
      const validationError = validateSetTemplateProduct(editingProduct)
      if (validationError) {
        message.error(getSetTemplateValidationMessage(validationError))
        return
      }
      const payload: SetProductTemplatePayload = buildSetProductTemplatePayload(
        selectedSupplier.code,
        values.templateName,
        editingProduct,
        values.isEnabled,
      )
      setSetTemplateSaving(true)
      const response = await updateSetProductTemplate(setTemplateEditing.templateId, selectedSupplier.code, payload)
      if (!response.success) {
        message.error(response.message || t('productCreation.saveSetTemplateFailed', '保存套装模板失败'))
        return
      }
      message.success(t('productCreation.saveSetTemplateSuccess', '套装模板已保存'))
      setSetTemplateEditing(null)
      setTemplateForm.resetFields()
      await Promise.all([
        loadSetTemplates(selectedSupplier.code, true),
        loadSetTemplates(selectedSupplier.code),
      ])
    } catch (error) {
      message.error(getSetTemplateErrorMessage(error, t('productCreation.saveSetTemplateFailed', '保存套装模板失败')))
    } finally {
      setSetTemplateSaving(false)
    }
  }, [getSetTemplateValidationMessage, loadSetTemplates, selectedSupplier?.code, setTemplateEditing, setTemplateForm, t])

  const handleDeactivateSetTemplate = useCallback(async (templateId: string) => {
    if (!selectedSupplier?.code) return
    try {
      setSetTemplateLoading(true)
      const response = await deactivateSetProductTemplate(templateId, selectedSupplier.code)
      if (!response.success) {
        message.error(response.message || t('productCreation.deactivateSetTemplateFailed', '停用套装模板失败'))
        return
      }
      message.success(t('productCreation.deactivateSetTemplateSuccess', '套装模板已停用'))
      await Promise.all([
        loadSetTemplates(selectedSupplier.code, true),
        loadSetTemplates(selectedSupplier.code),
      ])
    } catch (error) {
      message.error(getSetTemplateErrorMessage(error, t('productCreation.deactivateSetTemplateFailed', '停用套装模板失败')))
    } finally {
      setSetTemplateLoading(false)
    }
  }, [loadSetTemplates, selectedSupplier?.code, t])

  const handleOpenSaveSetTemplate = useCallback((productKey: string) => {
    const product = products.find((item) => item.key === productKey)
    if (!selectedSupplier?.code) {
      message.warning(t('domesticProducts.selectSupplier', '请选择供应商'))
      return
    }
    if (!product || product.productType !== ProductCreationType.SET) return
    const validationError = validateSetTemplateProduct(product)
    if (validationError) {
      message.error(getSetTemplateValidationMessage(validationError))
      return
    }
    setTemplateSourceProductKey(productKey)
    saveTemplateForm.setFieldsValue({ templateName: product.productName?.trim() || '' })
    setSaveTemplateVisible(true)
  }, [getSetTemplateValidationMessage, products, saveTemplateForm, selectedSupplier?.code, t])

  const handleSaveSetTemplateFromDraft = useCallback(async () => {
    if (!selectedSupplier?.code || !templateSourceProductKey) return
    const product = products.find((item) => item.key === templateSourceProductKey)
    if (!product || product.productType !== ProductCreationType.SET) return
    const validationError = validateSetTemplateProduct(product)
    if (validationError) {
      message.error(getSetTemplateValidationMessage(validationError))
      return
    }
    try {
      const values = await saveTemplateForm.validateFields()
      const payload = buildSetProductTemplatePayload(selectedSupplier.code, values.templateName, product)
      setSetTemplateSaving(true)
      const response = await createSetProductTemplate(payload)
      if (!response.success) {
        message.error(response.message || t('productCreation.saveSetTemplateFailed', '保存套装模板失败'))
        return
      }
      message.success(t('productCreation.saveSetTemplateSuccess', '套装模板已保存'))
      setSaveTemplateVisible(false)
      setTemplateSourceProductKey(null)
      saveTemplateForm.resetFields()
      await loadSetTemplates(selectedSupplier.code)
    } catch (error) {
      message.error(getSetTemplateErrorMessage(error, t('productCreation.saveSetTemplateFailed', '保存套装模板失败')))
    } finally {
      setSetTemplateSaving(false)
    }
  }, [getSetTemplateValidationMessage, loadSetTemplates, products, saveTemplateForm, selectedSupplier?.code, t, templateSourceProductKey])

  const handleAddProduct = useCallback(
    (type: ProductCreationType) => {
      const newProduct = createEmptyProduct(type, products.length)
      setProducts([...products, newProduct])
      if (type === ProductCreationType.SET) {
        setExpandedRowKeys((keys) => [...keys, newProduct.key])
      }
    },
    [createEmptyProduct, products],
  )

  const handleAddSubItem = useCallback((setKey: string) => {
    const newSubItem = createDraftSetSubItem()
    setProducts((current) => current.map((item) => {
      if (item.key !== setKey) return item
      const subItems = [...(item.subItems || []), newSubItem]
      return { ...item, subItems, setQuantity: subItems.length }
    }))
  }, [])

  const handleOpenBatchAdd = useCallback(() => {
    setBatchAddType(ProductCreationType.NORMAL)
    setBatchAddMode('append')
    setBatchAddCount(5)
    setBatchAddPrice(null)
    setBatchAddVisible(true)
  }, [])

  const handleBatchAdd = useCallback(
    (type: ProductCreationType, count: number, price?: number | null, mode: BatchAddMode = 'append') => {
      const nextState = applyBatchAddProducts({
        products,
        selectedRowKeys: selectedRowKeys.map(String),
        expandedRowKeys: expandedRowKeys.map(String),
        type,
        count,
        price,
        mode,
        createProduct: createEmptyProduct,
      })
      setProducts(nextState.products)
      setSelectedRowKeys(nextState.selectedRowKeys)
      setExpandedRowKeys(nextState.expandedRowKeys)
      setSelectedPasteTarget(null)
      setBatchAddVisible(false)
    },
    [createEmptyProduct, expandedRowKeys, products, selectedRowKeys],
  )

  const handleDeleteProduct = useCallback(
    (key: string) => {
      if (products.length <= 1) {
        message.warning(t('productCreation.keepAtLeastOneRow', '至少保留一行'))
        return
      }
      setProducts(products.filter((item) => item.key !== key))
      setSelectedRowKeys((keys) => keys.filter((selectedKey) => selectedKey !== key))
      setExpandedRowKeys((keys) => keys.filter((expandedKey) => expandedKey !== key))
      setSelectedPasteTarget((target) => (
        target?.scope === 'subItem' && target.setKey === key ? null : target
      ))
    },
    [products],
  )

  const handleUpdateProduct = useCallback(
    (key: string, field: keyof ProductItem, value: unknown) => {
      setProducts(products.map((item) => (item.key === key ? { ...item, [field]: value } : item)))
    },
    [products],
  )

  const handleDeleteSubItem = useCallback((setKey: string, subKey: string) => {
    setProducts((current) => current.map((item) => {
      if (item.key !== setKey) return item
      const subItems = (item.subItems || []).filter((subItem) => subItem.key !== subKey)
      return { ...item, subItems, setQuantity: subItems.length }
    }))
  }, [])

  const handleUpdateSubItem = useCallback((setKey: string, subKey: string, field: keyof SetSubItem, value: unknown) => {
    setProducts((current) => current.map((item) => (
      item.key === setKey
        ? { ...item, subItems: (item.subItems || []).map((subItem) => (subItem.key === subKey ? { ...subItem, [field]: value } : subItem)) }
        : item
    )))
  }, [])

  const setEditableCellRef = useCallback((
    rowKey: string,
    field: BatchCreateEditableField,
    cell: BatchCreateFocusableCell | null,
    setKey?: string,
  ) => {
    const scope = setKey ? getSubItemGridScope(setKey) : PARENT_GRID_SCOPE
    const cellKey = buildEditableCellKey(scope, rowKey, field)
    if (cell) editableCellRefs.current.set(cellKey, cell)
    else editableCellRefs.current.delete(cellKey)
  }, [])

  const focusEditableCell = useCallback((
    scope: string,
    rowKey: string,
    field: BatchCreateEditableField,
  ) => {
    window.requestAnimationFrame(() => {
      const cell = editableCellRefs.current.get(buildEditableCellKey(scope, rowKey, field))
      if (!cell) return

      const inputElement = cell.input
        ?? (cell.nativeElement instanceof HTMLInputElement
          ? cell.nativeElement
          : cell.nativeElement?.querySelector<HTMLInputElement>('input'))
      const scrollTarget = inputElement ?? cell.nativeElement
      scrollTarget?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
      cell.focus()
      window.requestAnimationFrame(() => {
        cell.select?.()
        inputElement?.select()
      })
    })
  }, [])

  const handleEditableCellKeyDown = useCallback((
    event: React.KeyboardEvent<HTMLElement>,
    rowKey: string,
    field: BatchCreateEditableField,
    setKey?: string,
  ) => {
    const direction = NAVIGATION_DIRECTION_BY_KEY[event.key]
    if (!direction || event.nativeEvent.isComposing) return

    // 方向键在录入表格中专用于切换单元格，边界处也保持当前输入框焦点。
    event.preventDefault()
    event.stopPropagation()

    const navigationRows = setKey
      ? (products.find((product) => product.key === setKey)?.subItems || []).map((subItem) => ({
        rowKey: subItem.key,
        fields: SUB_ITEM_EDITABLE_FIELDS,
      }))
      : products.map((product) => ({
        rowKey: product.key,
        fields: product.productType === ProductCreationType.SET
          ? PARENT_SET_EDITABLE_FIELDS
          : PARENT_COMMON_EDITABLE_FIELDS,
      }))
    const nextCell = getNextBatchCreateEditableCell({
      rows: navigationRows,
      current: { rowKey, field },
      direction,
    })
    if (!nextCell) return

    focusEditableCell(
      setKey ? getSubItemGridScope(setKey) : PARENT_GRID_SCOPE,
      nextCell.rowKey,
      nextCell.field,
    )
  }, [focusEditableCell, products])

  const reportPasteResult = useCallback((result: ReturnType<typeof applyParentColumnPaste>) => {
    if (result.error === 'multiple_columns') {
      message.warning(t('productCreation.pasteMultipleColumns', '一次只能粘贴一列 Excel 数据'))
      return false
    }
    if (result.error === 'missing_target') {
      setSelectedPasteTarget(null)
      message.warning(t('productCreation.pasteTargetMissing', '粘贴目标已失效，请重新选择列'))
      return false
    }

    setProducts(result.products)
    if (result.appliedCount + result.clearedCount + result.addedCount > 0) {
      message.success(t('productCreation.pasteResult', {
        applied: result.appliedCount,
        cleared: result.clearedCount,
        added: result.addedCount,
      }))
    }
    if (result.invalidCount > 0) {
      message.warning(t('productCreation.pasteInvalidPrices', {
        count: result.invalidCount,
      }))
    }
    return true
  }, [t])

  const handleColumnPaste = useCallback((
    event: React.ClipboardEvent<HTMLElement>,
    target: BatchCreatePasteTarget,
    startRowKey?: string,
  ) => {
    const clipboardText = event.clipboardData.getData('text')
    if (!clipboardText) return

    event.preventDefault()
    event.stopPropagation()
    setSelectedPasteTarget(target)
    const result = target.scope === 'parent'
      ? applyParentColumnPaste({
        products,
        startProductKey: startRowKey,
        field: target.field,
        clipboardText,
        createProduct: createEmptyProduct,
      })
      : applySubItemColumnPaste({
        products,
        setKey: target.setKey,
        startSubItemKey: startRowKey,
        field: target.field,
        clipboardText,
      })
    reportPasteResult(result)
  }, [createEmptyProduct, products, reportPasteResult])

  const renderPasteableColumnTitle = useCallback((
    label: string,
    target: BatchCreatePasteTarget,
  ) => {
    const selected = isSamePasteTarget(selectedPasteTarget, target)
    const accessibleLabel = selected
      ? t('productCreation.deselectPasteColumn', '取消选择“{{column}}”列', { column: label })
      : t('productCreation.selectPasteColumn', '选择“{{column}}”列进行粘贴', { column: label })
    return (
      <button
        type="button"
        aria-label={accessibleLabel}
        aria-pressed={selected}
        title={accessibleLabel}
        onClick={(event) => {
          event.stopPropagation()
          setSelectedPasteTarget((current) => (isSamePasteTarget(current, target) ? null : target))
        }}
        onPaste={(event) => handleColumnPaste(event, target)}
        style={{
          appearance: 'none',
          border: `1px solid ${selected ? token.colorPrimaryBorder : 'transparent'}`,
          borderRadius: token.borderRadiusSM,
          background: selected ? token.colorPrimaryBg : 'transparent',
          color: 'inherit',
          cursor: 'copy',
          font: 'inherit',
          lineHeight: 'inherit',
          margin: '-2px -6px',
          padding: '2px 6px',
        }}
      >
        {label}
      </button>
    )
  }, [handleColumnPaste, selectedPasteTarget, t, token.borderRadiusSM, token.colorPrimaryBg, token.colorPrimaryBorder])

  const handleBatchEditName = useCallback(() => {
    if (!batchEditNameValue.trim()) {
      message.warning(t('productCreation.enterName', '请输入名称'))
      return
    }
    const targetKeys = selectedRowKeys.length > 0 ? selectedRowKeys.map(String) : products.map((p) => p.key)
    setProducts(
      products.map((item) => {
        if (!targetKeys.includes(item.key)) return item
        let newName = item.productName
        switch (batchEditNameMode) {
          case 'replace': newName = batchEditNameValue; break
          case 'prefix': newName = batchEditNameValue + newName; break
          case 'suffix': newName = newName + batchEditNameValue; break
        }
        return { ...item, productName: newName }
      }),
    )
    setBatchEditNameVisible(false)
    setBatchEditNameValue('')
    setBatchEditNameMode('replace')
  }, [products, selectedRowKeys, batchEditNameMode, batchEditNameValue])

  const previewData = useMemo<PreviewItem[]>(() => buildPreviewItems(products, watchedPrefixCode), [products, watchedPrefixCode])

  const handleNext = useCallback(async () => {
    if (currentStep === 0) {
      try {
        await form.validateFields(['supplierCode'])
        setCurrentStep(1)
      } catch { return }
    } else if (currentStep === 1) {
      if (products.length === 0) {
        message.error(t('productCreation.addAtLeastOneProduct', '请至少添加一行商品'))
        return
      }
      const invalidSetProduct = findInvalidSetProduct(products)
      if (invalidSetProduct) {
        setExpandedRowKeys((keys) => Array.from(new Set([...keys, invalidSetProduct.key])))
        message.error(t('productCreation.addSetSubItemRequired', '第 {{index}} 行套装至少需要 1 个有效子项', { index: invalidSetProduct.index }))
        return
      }
      setCurrentStep(2)
    }
  }, [currentStep, form, products])

  const handlePrev = useCallback(() => setCurrentStep(currentStep - 1), [currentStep])

  const handleSubmit = async () => {
    const supplierCode = form.getFieldValue('supplierCode')
    if (!supplierCode) { message.error(t('domesticProducts.selectSupplier', '请选择供应商')); return }
    setSubmitting(true)
    try {
      const requestData: CreateBatchRequest = {
        supplierCode,
        prefixCode: form.getFieldValue('prefixCode'),
        prefixName: form.getFieldValue('prefixCode'),
        items: buildCreateBatchItems(products),
      }
      const response = await createBatch(requestData)
      setSubmitting(false)
      if (response.success) {
        message.success(t('productCreation.createSuccess', '创建成功'))
        if (!response.data?.batchNumber) {
          message.warning(t('productCreation.createSuccessNoBatchNumber', '创建成功，但未返回批次号，请从列表查看'))
          onSuccess()
          return
        }
        const createdBatch: BatchInfo = {
          batchNumber: response.data.batchNumber,
          supplierCode,
          supplierName: selectedSupplier?.name || supplierCode,
          prefixCode: form.getFieldValue('prefixCode') || undefined,
          normalCount: response.data.normalProductCount,
          setCount: response.data.setProductCount,
          totalCount: response.data.totalCreated,
          createdAt: new Date().toISOString(),
        }
        onSuccess(createdBatch)
      } else {
        message.error(response.message || t('productCreation.createFailed', '创建失败'))
      }
    } catch {
      setSubmitting(false)
      message.error(t('productCreation.createFailed', '创建失败'))
    }
  }

  const handleClose = useCallback(() => { handleReset(); onClose() }, [handleReset, onClose])

  const handleSupplierChange = useCallback(
    (supplierCode: string) => {
      form.setFieldValue('prefixCode', undefined)
      setPrefixCodes([])
      setSetTemplates([])
      setManagedSetTemplates([])
      setSelectedSetTemplateId(undefined)
      setSetTemplateManageVisible(false)
      setSetTemplateEditing(null)
      const supplier = suppliers.find((s) => s.supplierCode === supplierCode)
      setSelectedSupplier(supplier ? { code: supplier.supplierCode, name: supplier.supplierName } : null)
      if (supplierCode) {
        loadPrefixes(supplierCode)
        void loadSetTemplates(supplierCode)
      }
    },
    [form, loadSetTemplates, suppliers],
  )

  const productColumns: ColumnsType<ProductItem> = [
    {
      title: '#',
      key: '_index',
      width: 50,
      align: 'center',
      render: (_, __, index) => index + 1,
    },
    {
      title: t('productCreation.type', '类型'),
      dataIndex: 'productType',
      key: 'productType',
      width: 120,
      render: (type: ProductCreationType) => {
        const typeMap: Record<ProductCreationType, { text: string; color: string }> = {
          [ProductCreationType.NORMAL]: { text: t('productCreation.normal', '普通'), color: 'blue' },
          [ProductCreationType.SET]: { text: t('productCreation.set', '套装'), color: 'green' },
          [ProductCreationType.SET_SUB_ITEM]: { text: t('productCreation.setSubItem', '套装子项'), color: 'orange' },
        }
        const config = typeMap[type] || typeMap[ProductCreationType.NORMAL]
        return <Tag color={config.color} style={{ marginInlineEnd: 0 }}>{config.text}</Tag>
      },
    },
    {
      title: renderPasteableColumnTitle(
        t('domesticProducts.productName', '商品名称'),
        { scope: 'parent', field: 'productName' },
      ),
      dataIndex: 'productName',
      key: 'productName',
      width: 280,
      render: (text, record) => (
        <Input
          ref={(cell) => setEditableCellRef(record.key, 'productName', cell)}
          value={text}
          onChange={(e) => handleUpdateProduct(record.key, 'productName', e.target.value)}
          onKeyDown={(event) => handleEditableCellKeyDown(event, record.key, 'productName')}
          onPaste={(event) => handleColumnPaste(
            event,
            { scope: 'parent', field: 'productName' },
            record.key,
          )}
          placeholder={t('domesticProducts.productName', '商品名称')}
          style={{
            backgroundColor: isSamePasteTarget(selectedPasteTarget, { scope: 'parent', field: 'productName' })
              ? token.colorPrimaryBg
              : undefined,
          }}
        />
      ),
    },
    {
      title: renderPasteableColumnTitle(
        t('productCreation.privateLabelPrice', '零售价'),
        { scope: 'parent', field: 'privateLabelPrice' },
      ),
      dataIndex: 'privateLabelPrice',
      key: 'privateLabelPrice',
      width: 130,
      render: (text, record) => (
        <InputNumber
          ref={(cell) => setEditableCellRef(record.key, 'privateLabelPrice', cell)}
          value={text}
          onChange={(value) => handleUpdateProduct(record.key, 'privateLabelPrice', value)}
          onKeyDown={(event) => handleEditableCellKeyDown(event, record.key, 'privateLabelPrice')}
          onPaste={(event) => handleColumnPaste(
            event,
            { scope: 'parent', field: 'privateLabelPrice' },
            record.key,
          )}
          placeholder={t('productCreation.privateLabelPrice', '零售价')}
          style={{
            width: '100%',
            backgroundColor: isSamePasteTarget(selectedPasteTarget, { scope: 'parent', field: 'privateLabelPrice' })
              ? token.colorPrimaryBg
              : undefined,
          }}
          min={0}
          precision={2}
        />
      ),
    },
    {
      title: t('productCreation.createCount', '创建套数'),
      dataIndex: 'createCount',
      key: 'createCount',
      width: 120,
      render: (text, record) =>
        record.productType === ProductCreationType.SET ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(record.key, 'createCount', cell)}
            value={text ?? 1}
            onChange={(value) => handleUpdateProduct(record.key, 'createCount', normalizeCreateCount(value))}
            onKeyDown={(event) => handleEditableCellKeyDown(event, record.key, 'createCount')}
            style={{ width: '100%' }}
            min={1}
            precision={0}
          />
        ) : '-',
    },
    {
      title: t('productCreation.setQuantity', '套装数量'),
      dataIndex: 'setQuantity',
      key: 'setQuantity',
      width: 110,
      render: (text, record) =>
        record.productType === ProductCreationType.SET ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(record.key, 'setQuantity', cell)}
            value={text}
            onChange={(value) => handleUpdateProduct(record.key, 'setQuantity', value)}
            onKeyDown={(event) => handleEditableCellKeyDown(event, record.key, 'setQuantity')}
            style={{ width: '100%' }}
            min={1}
          />
        ) : '-',
    },
    {
      title: t('productCreation.setPrice', '套装价格'),
      dataIndex: 'setPrice',
      key: 'setPrice',
      width: 120,
      render: (text, record) =>
        record.productType === ProductCreationType.SET ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(record.key, 'setPrice', cell)}
            value={text}
            onChange={(value) => handleUpdateProduct(record.key, 'setPrice', value)}
            onKeyDown={(event) => handleEditableCellKeyDown(event, record.key, 'setPrice')}
            style={{ width: '100%' }}
            min={0}
            precision={2}
          />
        ) : '-',
    },
    {
      title: t('common.action', '操作'),
      key: 'actions',
      width: 170,
      fixed: 'right',
      render: (_, record) => (
        <Space size={4}>
          {record.productType === ProductCreationType.SET && <Button type="text" size="small" icon={<PlusOutlined />} onClick={() => handleAddSubItem(record.key)}>{t('productCreation.setSubItem', '子项')}</Button>}
          {record.productType === ProductCreationType.SET && <Button type="text" size="small" icon={<SaveOutlined />} onClick={() => handleOpenSaveSetTemplate(record.key)}>{t('productCreation.saveSetTemplate', '存模板')}</Button>}
          <Button type="text" danger icon={<DeleteOutlined />} onClick={() => handleDeleteProduct(record.key)} disabled={products.length <= 1} />
        </Space>
      ),
    },
  ]

  const createSubItemColumns = (setKey: string): ColumnsType<SetSubItem> => [
    {
      title: '#',
      key: '_index',
      width: 50,
      align: 'center',
      render: (_, __, index) => index + 1,
    },
    {
      title: renderPasteableColumnTitle(
        t('domesticProducts.productName', '商品名称'),
        { scope: 'subItem', setKey, field: 'productName' },
      ),
      dataIndex: 'productName',
      key: 'productName',
      render: (text, record) => (
        <Input
          ref={(cell) => setEditableCellRef(record.key, 'productName', cell, setKey)}
          value={text}
          onChange={(e) => handleUpdateSubItem(setKey, record.key, 'productName', e.target.value)}
          onKeyDown={(event) => handleEditableCellKeyDown(event, record.key, 'productName', setKey)}
          onPaste={(event) => handleColumnPaste(
            event,
            { scope: 'subItem', setKey, field: 'productName' },
            record.key,
          )}
          placeholder={t('domesticProducts.productName', '商品名称')}
          style={{
            backgroundColor: isSamePasteTarget(selectedPasteTarget, { scope: 'subItem', setKey, field: 'productName' })
              ? token.colorPrimaryBg
              : undefined,
          }}
        />
      ),
    },
    {
      title: renderPasteableColumnTitle(
        t('productCreation.privateLabelPrice', '零售价'),
        { scope: 'subItem', setKey, field: 'privateLabelPrice' },
      ),
      dataIndex: 'privateLabelPrice',
      key: 'privateLabelPrice',
      width: 140,
      render: (text, record) => (
        <InputNumber
          ref={(cell) => setEditableCellRef(record.key, 'privateLabelPrice', cell, setKey)}
          value={text}
          onChange={(value) => handleUpdateSubItem(setKey, record.key, 'privateLabelPrice', value)}
          onKeyDown={(event) => handleEditableCellKeyDown(event, record.key, 'privateLabelPrice', setKey)}
          onPaste={(event) => handleColumnPaste(
            event,
            { scope: 'subItem', setKey, field: 'privateLabelPrice' },
            record.key,
          )}
          placeholder={t('productCreation.privateLabelPrice', '零售价')}
          style={{
            width: '100%',
            backgroundColor: isSamePasteTarget(selectedPasteTarget, { scope: 'subItem', setKey, field: 'privateLabelPrice' })
              ? token.colorPrimaryBg
              : undefined,
          }}
          min={0}
          precision={2}
        />
      ),
    },
    {
      title: t('common.action', '操作'),
      key: 'actions',
      width: 80,
      render: (_, record) => <Button type="text" danger icon={<DeleteOutlined />} onClick={() => handleDeleteSubItem(setKey, record.key)} />,
    },
  ]

  const previewColumns: ColumnsType<PreviewItem> = [
    {
      title: '#',
      key: '_index',
      width: 50,
      align: 'center',
      render: (_, __, index) => index + 1,
    },
    { title: t('productImport.hbProductNoCol', '货号'), dataIndex: 'itemNumber', key: 'itemNumber', width: 150, render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span> },
    { title: t('domesticProducts.productName', '商品名称'), dataIndex: 'productName', key: 'productName', render: (text, record) => <span style={{ paddingLeft: record.parentPreviewKey ? 20 : 0 }}>{record.parentPreviewKey ? '└ ' : ''}{text || '-'}</span> },
    {
      title: t('productCreation.type', '类型'),
      dataIndex: 'productType',
      key: 'productType',
      width: 100,
      render: (type: ProductCreationType) => {
        const typeMap: Record<ProductCreationType, string> = { [ProductCreationType.NORMAL]: t('productCreation.normal', '普通'), [ProductCreationType.SET]: t('productCreation.set', '套装'), [ProductCreationType.SET_SUB_ITEM]: t('productCreation.setSubItem', '套装子项') }
        return typeMap[type] || type
      },
    },
    { title: t('productCreation.privateLabelPrice', '零售价'), dataIndex: 'privateLabelPrice', key: 'privateLabelPrice', width: 120, render: (text) => (text != null ? `$${text}` : '-') },
    {
      title: t('productCreation.createCount', '创建套数'),
      dataIndex: 'createCount',
      key: 'createCount',
      width: 110,
      render: (text, record) => (record.productType === ProductCreationType.SET ? normalizeCreateCount(text) : '-'),
    },
    {
      title: t('productCreation.setQuantity', '套装数量'),
      dataIndex: 'setQuantity',
      key: 'setQuantity',
      width: 110,
      render: (text, record) => (record.productType === ProductCreationType.SET ? text ?? '-' : '-'),
    },
    {
      title: t('productCreation.setPrice', '套装价格'),
      dataIndex: 'setPrice',
      key: 'setPrice',
      width: 120,
      render: (text, record) => (record.productType === ProductCreationType.SET && text != null ? `$${text}` : '-'),
    },
  ]

  const steps = [{ title: t('productCreation.basicInfo', '基本信息') }, { title: t('productCreation.productDetail', '商品明细') }, { title: t('productCreation.previewConfirm', '预览确认') }]

  return (
    <Modal
      title={t('productCreation.createBatch', '创建批次')}
      open={visible}
      onCancel={handleClose}
      width={1120}
      style={{ top: 40, maxWidth: 'calc(100vw - 32px)' }}
      footer={null}
      destroyOnHidden
    >
      <Form form={form} layout="vertical">
        <Steps current={currentStep} items={steps} style={{ marginBottom: 24 }} />

        {currentStep === 0 && (
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="supplierCode" label={t('domesticProducts.supplier', '供应商')} rules={[{ required: true, message: t('domesticProducts.selectSupplier', '请选择供应商') }]}>
                <Select showSearch placeholder={t('domesticProducts.selectSupplier', '请选择供应商')} optionFilterProp="label" loading={loading} onChange={handleSupplierChange} options={suppliers.map((s) => ({ label: `${s.supplierCode} - ${s.supplierName}`, value: s.supplierCode }))} />
              </Form.Item>
            </Col>
            <Col span={12} style={{ position: 'relative' }}>
              <Form.Item name="prefixCode" label={t('productCreation.prefixCode', '前缀码')}>
                <Select placeholder={t('productCreation.selectPrefixCode', '请选择前缀码')} allowClear showSearch optionFilterProp="label" style={{ width: 'calc(100% - 80px)' }} options={prefixCodes.map((p) => ({ label: p.prefixDescription ? `${p.prefixName} - ${p.prefixDescription}` : p.prefixName, value: p.prefixName }))} />
              </Form.Item>
              <Button type="link" size="small" icon={<SettingOutlined />} disabled={!selectedSupplier} onClick={() => setManageModalVisible(true)} style={{ position: 'absolute', right: 0, top: 6 }} />
            </Col>
          </Row>
        )}

        {currentStep === 1 && (
          <div>
            <Space wrap style={{ marginBottom: 16 }}>
              <Button icon={<PlusOutlined />} onClick={() => handleAddProduct(ProductCreationType.NORMAL)}>{t('productCreation.normal', '普通')}</Button>
              <Button icon={<PlusOutlined />} onClick={() => handleAddProduct(ProductCreationType.SET)}>{t('productCreation.set', '套装')}</Button>
              <Select
                value={selectedSetTemplateId}
                allowClear
                loading={setTemplateLoading}
                placeholder={t('productCreation.selectSetTemplate', '选择套装模板')}
                style={{ width: 220 }}
                onChange={(value) => {
                  setSelectedSetTemplateId(value)
                  if (value) void handleApplySetTemplate(value)
                }}
                options={setTemplates
                  .filter((template) => template.isEnabled)
                  .map((template) => ({
                    value: template.templateId,
                    label: `${template.templateName} · ${template.setProductName} (${template.setQuantity})`,
                  }))}
              />
              <Button type="dashed" icon={<SettingOutlined />} onClick={handleOpenSetTemplateManager}>{t('productCreation.manageSetTemplates', '管理模板')}</Button>
              <Button type="dashed" onClick={handleOpenBatchAdd}>{t('productCreation.batchAdd', '批量添加')}</Button>
              <Button type="dashed" icon={<EditOutlined />} onClick={() => setBatchEditNameVisible(true)}>{t('productCreation.batchName', '批量命名')}</Button>
            </Space>
            <Modal title={t('productCreation.batchAdd', '批量添加')} open={batchAddVisible} onOk={() => { handleBatchAdd(batchAddType, batchAddCount, batchAddPrice, batchAddMode); setBatchAddVisible(false) }} onCancel={() => setBatchAddVisible(false)} okText={t('common.confirm', '确定')} cancelText={t('common.cancel', '取消')}>
              <Space direction="vertical" style={{ width: '100%' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <span>{t('productCreation.type', '类型')}:</span>
                  <Select value={batchAddType} onChange={(value) => setBatchAddType(value)} style={{ width: 110 }} options={[{ label: t('productCreation.normal', '普通'), value: ProductCreationType.NORMAL }, { label: t('productCreation.set', '套装'), value: ProductCreationType.SET }]} />
                  <span>{t('productCreation.quantity', '数量')}:</span>
                  <InputNumber min={1} max={100} value={batchAddCount} onChange={(v) => setBatchAddCount(v || 5)} />
                  <span>{t('productCreation.mode', '模式')}:</span>
                  <Select value={batchAddMode} onChange={(v) => setBatchAddMode(v)} style={{ width: 140 }} options={[{ label: t('productCreation.adjustToCount', '调整到指定数量'), value: 'overwrite' }, { label: t('productCreation.appendCount', '追加指定数量'), value: 'append' }]} />
                </div>
                <div>
                  {t('productCreation.uniformPrice', '统一零售价')}:
                  <InputNumber value={batchAddPrice} min={0} precision={2} placeholder={t('productCreation.optional', '可选')} style={{ marginLeft: 8, width: 160 }} onChange={(v) => setBatchAddPrice(v)} />
                </div>
              </Space>
            </Modal>
            <Modal title={t('productCreation.batchName', '批量命名')} open={batchEditNameVisible} onOk={handleBatchEditName} onCancel={() => { setBatchEditNameVisible(false); setBatchEditNameValue(''); setBatchEditNameMode('replace') }} okText={t('common.confirm', '确定')} cancelText={t('common.cancel', '取消')}>
              <Space direction="vertical" style={{ width: '100%' }} size="middle">
                <div>
                  {t('productCreation.mode', '模式')}:
                  <Select value={batchEditNameMode} onChange={(v) => setBatchEditNameMode(v)} style={{ marginLeft: 8, width: 160 }} options={[{ label: t('productCreation.replace', '替换'), value: 'replace' }, { label: t('productCreation.addPrefix', '加前缀'), value: 'prefix' }, { label: t('productCreation.addSuffix', '加后缀'), value: 'suffix' }]} />
                </div>
                <div>
                  {t('productCreation.value', '值')}:
                  <Input value={batchEditNameValue} onChange={(e) => setBatchEditNameValue(e.target.value)} style={{ marginLeft: 8, width: 280 }} placeholder={t('productCreation.enterName', '请输入名称')} />
                </div>
                <div style={{ color: '#999', fontSize: 12 }}>
                  {selectedRowKeys.length > 0 ? t('productCreation.applyToSelectedRows', { count: selectedRowKeys.length }) : t('productCreation.applyToAll', '将应用到所有行')}
                </div>
              </Space>
            </Modal>
            <Modal
              title={t('productCreation.saveSetTemplate', '保存套装模板')}
              open={saveTemplateVisible}
              confirmLoading={setTemplateSaving}
              onOk={() => void handleSaveSetTemplateFromDraft()}
              onCancel={() => {
                setSaveTemplateVisible(false)
                setTemplateSourceProductKey(null)
                saveTemplateForm.resetFields()
              }}
              okText={t('common.save', '保存')}
              cancelText={t('common.cancel', '取消')}
            >
              <Form form={saveTemplateForm} layout="vertical">
                <Form.Item name="templateName" label={t('productCreation.setTemplateName', '模板名称')} rules={[{ required: true, whitespace: true, message: t('productCreation.setTemplateNameRequired', '请输入模板名称') }]}>
                  <Input maxLength={100} placeholder={t('productCreation.setTemplateName', '模板名称')} />
                </Form.Item>
              </Form>
            </Modal>
            <Typography.Text
              type="secondary"
              style={{ display: 'block', marginBottom: 8, fontSize: 12 }}
            >
              {t(
                'productCreation.pasteNavigationHint',
                '点击“商品名称”或“零售价”列头后，可粘贴 Excel 单列；方向键可切换输入框',
              )}
            </Typography.Text>
            <MeasuredTable metricId="domestic-purchase.product-creation.batch-create-modal.table-1"
              columns={productColumns}
              dataSource={products}
              rowKey="key"
              pagination={false}
              size="small"
              rowSelection={{ selectedRowKeys, onChange: (keys) => setSelectedRowKeys(keys) }}
              expandable={{
                expandedRowKeys,
                onExpandedRowsChange: (keys) => {
                  const nextExpandedRowKeys = [...keys]
                  setExpandedRowKeys(nextExpandedRowKeys)
                  setSelectedPasteTarget((target) => (
                    target?.scope === 'subItem'
                    && !nextExpandedRowKeys.some((key) => String(key) === target.setKey)
                      ? null
                      : target
                  ))
                },
                rowExpandable: (record) => record.productType === ProductCreationType.SET,
                expandedRowRender: (record) => (
                  <MeasuredTable metricId="domestic-purchase.product-creation.batch-create-modal.table-2"
                    columns={createSubItemColumns(record.key)}
                    dataSource={record.subItems || []}
                    rowKey="key"
                    pagination={false}
                    size="small"
                    scroll={{ x: 560 }}
                    locale={{ emptyText: <Button type="link" icon={<PlusOutlined />} onClick={() => handleAddSubItem(record.key)}>{t('productCreation.addSetSubItem', '添加套装子项')}</Button> }}
                  />
                ),
              }}
              scroll={{ x: PRODUCT_TABLE_SCROLL_X, y: 340 }}
            />
            <Modal
              title={t('productCreation.manageSetTemplates', '管理套装模板')}
              open={setTemplateManageVisible}
              width={900}
              footer={null}
              onCancel={() => {
                setSetTemplateManageVisible(false)
                setSetTemplateEditing(null)
                setTemplateForm.resetFields()
              }}
            >
              <MeasuredTable<SetProductTemplateSummary> metricId="domestic-purchase.product-creation.batch-create-modal.table-3"
                rowKey="templateId"
                size="small"
                loading={setTemplateLoading}
                pagination={false}
                dataSource={managedSetTemplates}
                columns={[
                  { title: t('productCreation.setTemplateName', '模板名称'), dataIndex: 'templateName', key: 'templateName' },
                  { title: t('domesticProducts.productName', '套装商品名'), dataIndex: 'setProductName', key: 'setProductName' },
                  { title: t('productCreation.setQuantity', '子项数'), dataIndex: 'setQuantity', key: 'setQuantity', width: 90, align: 'center' },
                  {
                    title: t('common.status', '状态'),
                    dataIndex: 'isEnabled',
                    key: 'isEnabled',
                    width: 90,
                    render: (isEnabled: boolean) => <Tag color={isEnabled ? 'green' : 'default'}>{isEnabled ? t('common.enabled', '启用') : t('common.disabled', '停用')}</Tag>,
                  },
                  {
                    title: t('common.action', '操作'),
                    key: 'actions',
                    width: 150,
                    render: (_, record) => (
                      <Space size={4}>
                        <Button type="link" size="small" onClick={() => void handleEditSetTemplate(record.templateId)}>{t('common.edit', '编辑')}</Button>
                        {record.isEnabled && (
                          <Popconfirm
                            title={t('productCreation.confirmDeactivateSetTemplate', '确认停用这个套装模板？')}
                            okText={t('common.confirm', '确定')}
                            cancelText={t('common.cancel', '取消')}
                            onConfirm={() => void handleDeactivateSetTemplate(record.templateId)}
                          >
                            <Button type="link" danger size="small">{t('common.disable', '停用')}</Button>
                          </Popconfirm>
                        )}
                      </Space>
                    ),
                  },
                ]}
                locale={{ emptyText: t('productCreation.noSetTemplates', '暂无套装模板，可从套装草稿保存') }}
              />
              <Modal
                title={setTemplateEditing?.templateName || t('productCreation.editSetTemplate', '编辑套装模板')}
                open={Boolean(setTemplateEditing)}
                width={680}
                confirmLoading={setTemplateSaving}
                onOk={() => void handleSaveSetTemplateEdit()}
                onCancel={() => {
                  setSetTemplateEditing(null)
                  setTemplateForm.resetFields()
                }}
                okText={t('common.save', '保存')}
                cancelText={t('common.cancel', '取消')}
              >
                <Form form={setTemplateForm} layout="vertical">
                  <Row gutter={12}>
                    <Col span={12}>
                      <Form.Item name="templateName" label={t('productCreation.setTemplateName', '模板名称')} rules={[{ required: true, whitespace: true, message: t('productCreation.setTemplateNameRequired', '请输入模板名称') }]}>
                        <Input maxLength={100} />
                      </Form.Item>
                    </Col>
                    <Col span={12}>
                      <Form.Item name="setProductName" label={t('domesticProducts.productName', '套装商品名')} rules={[{ required: true, whitespace: true, message: t('productCreation.setTemplateSetNameRequired', '请填写套装商品名') }]}>
                        <Input maxLength={200} />
                      </Form.Item>
                    </Col>
                  </Row>
                  <Form.Item name="isEnabled" label={t('common.status', '状态')} valuePropName="checked">
                    <Switch checkedChildren={t('common.enabled', '启用')} unCheckedChildren={t('common.disabled', '停用')} />
                  </Form.Item>
                  <Form.List name="subItems">
                    {(fields, { add, remove }) => (
                      <Space direction="vertical" style={{ width: '100%' }} size={8}>
                        <Space align="center">
                          <span>{t('productCreation.setSubItem', '套装子项')}</span>
                          <Button type="link" size="small" icon={<PlusOutlined />} onClick={() => add({ productName: '', privateLabelPrice: undefined })}>{t('common.add', '添加')}</Button>
                        </Space>
                        {fields.map((field, index) => (
                          <Row gutter={8} key={field.key} wrap={false}>
                            <Col flex="auto">
                              <Form.Item name={[field.name, 'productName']} rules={[{ required: true, whitespace: true, message: t('productCreation.setTemplateSubItemNameRequired', '请完整填写模板子项名称') }]}>
                                <Input placeholder={`${t('productCreation.setSubItem', '子项')} ${index + 1}`} />
                              </Form.Item>
                            </Col>
                            <Col flex="150px">
                              <Form.Item name={[field.name, 'privateLabelPrice']} rules={[{ required: true, message: t('productCreation.setTemplateSubItemPriceRequired', '请完整填写模板子项零售价') }]}>
                                <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder={t('productCreation.privateLabelPrice', '零售价')} />
                              </Form.Item>
                            </Col>
                            <Col flex="32px">
                              <Button
                                type="text"
                                danger
                                icon={<DeleteOutlined />}
                                aria-label={t('productCreation.deleteSetTemplateSubItem', '删除模板子项')}
                                onClick={() => remove(field.name)}
                              />
                            </Col>
                          </Row>
                        ))}
                      </Space>
                    )}
                  </Form.List>
                </Form>
              </Modal>
            </Modal>
          </div>
        )}

        {currentStep === 2 && (
          <div>
            <div style={{ marginBottom: 16, padding: '12px 16px', background: '#f5f5f5', borderRadius: 4 }}>
              <Space size="large">
                <span><strong>{t('domesticProducts.supplier', '供应商')}:</strong> {selectedSupplier?.name || form.getFieldValue('supplierCode')}</span>
                <span><strong>{t('productCreation.prefixCode', '前缀码')}:</strong> {form.getFieldValue('prefixCode') || '-'}</span>
                <span><strong>{t('productCreation.productCount', '商品数量')}:</strong> {previewData.length}</span>
              </Space>
            </div>
            <MeasuredTable metricId="domestic-purchase.product-creation.batch-create-modal.table-4" columns={previewColumns} dataSource={previewData} rowKey="key" pagination={false} size="small" scroll={{ x: PREVIEW_TABLE_SCROLL_X, y: 340 }} />
          </div>
        )}

        <div style={{ marginTop: 24, textAlign: 'right' }}>
          <Space>
            {currentStep > 0 && <Button onClick={handlePrev}>{t('productCreation.prevStep', '上一步')}</Button>}
            {currentStep < 2 && <Button type="primary" onClick={handleNext}>{t('productCreation.nextStep', '下一步')}</Button>}
            {currentStep === 2 && <Button type="primary" loading={submitting} onClick={handleSubmit}>{t('productCreation.confirmCreate', '确认创建')}</Button>}
            <Button onClick={handleClose}>{t('common.cancel', '取消')}</Button>
          </Space>
        </div>

        <PrefixCodeManageModal
          visible={manageModalVisible}
          supplierCode={selectedSupplier?.code || ''}
          supplierName={selectedSupplier?.name || ''}
          onClose={() => setManageModalVisible(false)}
          onSuccess={() => {
            setManageModalVisible(false)
            if (selectedSupplier?.code) loadPrefixes(selectedSupplier.code)
          }}
        />
      </Form>
    </Modal>
  )
}
