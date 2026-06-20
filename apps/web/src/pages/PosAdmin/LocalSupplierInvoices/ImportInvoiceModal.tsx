import { FileSearchOutlined, UploadOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  DatePicker,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Steps,
  Table,
  Typography,
  message,
} from 'antd'
import dayjs from 'dayjs'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  confirmInvoiceImport,
  previewInvoiceImport,
} from '../../../services/localSupplierInvoiceService'
import type {
  LocalSupplierInvoiceImportColumnMapping,
  LocalSupplierInvoiceImportField,
  LocalSupplierInvoiceImportPreviewHeader,
  LocalSupplierInvoiceImportPreviewResponse,
} from '../../../types/localSupplierInvoice'
import {
  buildImportPreviewLines,
  getMissingImportMappings,
  hasDuplicateImportMappings,
  hasRequiredImportMappings,
  isLegacyExcelFileName,
  normalizeImportColumnMapping,
  REQUIRED_IMPORT_FIELDS,
  resolveSourceColumnSampleValue,
} from './importPreview'

interface SelectOption {
  label: string
  value: string
}

interface ImportInvoiceModalProps {
  open: boolean
  storeOptions: SelectOption[]
  supplierOptions: SelectOption[]
  onCancel: () => void
  onCreated: (invoiceGuid: string) => Promise<void> | void
}

const IMPORT_FIELD_LABELS: Record<LocalSupplierInvoiceImportField, string> = {
  itemNumber: 'itemNumber',
  barcode: 'barcode',
  productName: 'productName',
  quantity: 'quantity',
  price: 'price',
}

const IMPORT_FIELD_MAPPING_KEYS: Record<LocalSupplierInvoiceImportField, keyof LocalSupplierInvoiceImportColumnMapping> = {
  itemNumber: 'itemNumberColumnKey',
  barcode: 'barcodeColumnKey',
  productName: 'productNameColumnKey',
  quantity: 'quantityColumnKey',
  price: 'priceColumnKey',
}

function resolveMatchedOptionValue(value: string | undefined, options: SelectOption[]): string | undefined {
  if (!value) {
    return undefined
  }

  return options.some((option) => option.value === value) ? value : undefined
}

export default function ImportInvoiceModal({
  open,
  storeOptions,
  supplierOptions,
  onCancel,
  onCreated,
}: ImportInvoiceModalProps) {
  const { t } = useTranslation()
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  const [currentStep, setCurrentStep] = useState(0)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [previewing, setPreviewing] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [previewData, setPreviewData] = useState<LocalSupplierInvoiceImportPreviewResponse | null>(null)
  const [columnMapping, setColumnMapping] = useState<LocalSupplierInvoiceImportColumnMapping>(
    normalizeImportColumnMapping(),
  )
  const [headerDraft, setHeaderDraft] = useState<LocalSupplierInvoiceImportPreviewHeader>({})

  const mappedPreviewLines = useMemo(
    () => buildImportPreviewLines(previewData?.lines ?? [], columnMapping),
    [columnMapping, previewData],
  )

  const missingMappings = useMemo(
    () => getMissingImportMappings(columnMapping),
    [columnMapping],
  )

  const hasDuplicateMappings = useMemo(
    () => hasDuplicateImportMappings(columnMapping),
    [columnMapping],
  )

  const canAdvanceToPreview = previewData !== null && hasRequiredImportMappings(columnMapping) && !hasDuplicateMappings
  const canConfirmCreate = canAdvanceToPreview
    && Boolean(headerDraft.storeCode)
    && Boolean(headerDraft.supplierCode)
    && Boolean(headerDraft.invoiceNo?.trim())
    && mappedPreviewLines.length > 0

  const parsedStoreText = previewData?.header.storeName || previewData?.header.storeCode || '--'
  const parsedSupplierText = previewData?.header.supplierName || previewData?.header.supplierCode || '--'

  useEffect(() => {
    if (!open) {
      setCurrentStep(0)
      setSelectedFile(null)
      setPreviewing(false)
      setConfirming(false)
      setPreviewData(null)
      setColumnMapping(normalizeImportColumnMapping())
      setHeaderDraft({})
    }
  }, [open])

  const sourceColumnOptions = useMemo(
    () => (previewData?.sourceColumns ?? []).map((column) => ({
      label: `${column.key}. ${column.header || t('posAdmin.invoices.import.unnamedColumn')}`,
      value: column.key,
    })),
    [previewData, t],
  )

  const handleChooseFile = () => {
    fileInputRef.current?.click()
  }

  const handleFileSelected = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] || null
    event.target.value = ''

    if (!file) {
      return
    }

    if (isLegacyExcelFileName(file.name)) {
      message.warning(t('posAdmin.invoices.import.legacyExcelNotSupported'))
      return
    }

    setSelectedFile(file)
  }

  const handlePreviewImport = async () => {
    if (!selectedFile) {
      message.warning(t('posAdmin.invoices.import.selectFileFirst'))
      return
    }

    setPreviewing(true)
    try {
      const response = await previewInvoiceImport(selectedFile)
      setPreviewData(response)
      setColumnMapping(normalizeImportColumnMapping(response.recommendedMapping))
      setHeaderDraft({
        ...response.header,
        storeCode: resolveMatchedOptionValue(response.header.storeCode, storeOptions),
        supplierCode: resolveMatchedOptionValue(response.header.supplierCode, supplierOptions),
        invoiceNo: response.header.invoiceNo?.trim() || undefined,
      })
      setCurrentStep(1)
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.invoices.import.previewFailed'))
    } finally {
      setPreviewing(false)
    }
  }

  const handleConfirmCreate = async () => {
    if (!previewData) {
      return
    }

    setConfirming(true)
    try {
      const result = await confirmInvoiceImport({
        sourceColumns: previewData.sourceColumns,
        header: {
          ...headerDraft,
          invoiceNo: headerDraft.invoiceNo?.trim(),
        },
        mapping: {
          itemNumberColumnKey: columnMapping.itemNumberColumnKey || '',
          barcodeColumnKey: columnMapping.barcodeColumnKey || '',
          productNameColumnKey: columnMapping.productNameColumnKey || '',
          quantityColumnKey: columnMapping.quantityColumnKey || '',
          priceColumnKey: columnMapping.priceColumnKey || '',
        },
        lines: previewData.lines,
      })

      if (!result.invoiceGuid) {
        throw new Error(t('posAdmin.invoices.import.missingInvoiceGuid'))
      }

      message.success(t('posAdmin.invoices.import.createSuccess'))
      await onCreated(result.invoiceGuid)
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('posAdmin.invoices.import.confirmFailed'))
    } finally {
      setConfirming(false)
    }
  }

  const mappingFieldOptions = (field: LocalSupplierInvoiceImportField) => {
    const selectedColumns = new Set(
      REQUIRED_IMPORT_FIELDS
        .filter((currentField) => currentField !== field)
        .map((currentField) => {
          const key = IMPORT_FIELD_MAPPING_KEYS[currentField]
          return columnMapping[key]
        })
        .filter((value): value is string => typeof value === 'string' && value.length > 0),
    )

    return sourceColumnOptions.map((option) => ({
      ...option,
      disabled: selectedColumns.has(option.value),
    }))
  }

  const footer = (
    <Space>
      {currentStep > 0 ? (
        <Button onClick={() => setCurrentStep((step) => Math.max(0, step - 1))}>
          {t('common.previous', '上一步')}
        </Button>
      ) : null}
      <Button onClick={onCancel}>{t('common.cancel')}</Button>
      {currentStep === 0 ? (
        <Button type="primary" icon={<FileSearchOutlined />} loading={previewing} onClick={() => void handlePreviewImport()}>
          {t('posAdmin.invoices.import.parseFile')}
        </Button>
      ) : null}
      {currentStep === 1 ? (
        <Button type="primary" disabled={!canAdvanceToPreview} onClick={() => setCurrentStep(2)}>
          {t('posAdmin.invoices.import.previewCreate')}
        </Button>
      ) : null}
      {currentStep === 2 ? (
        <Button type="primary" loading={confirming} disabled={!canConfirmCreate} onClick={() => void handleConfirmCreate()}>
          {t('posAdmin.invoices.import.confirmCreate')}
        </Button>
      ) : null}
    </Space>
  )

  return (
    <Modal
      open={open}
      title={t('posAdmin.invoices.import.title')}
      onCancel={onCancel}
      width={960}
      footer={footer}
      destroyOnHidden
    >
      <Steps
        current={currentStep}
        items={[
          { title: t('posAdmin.invoices.import.steps.upload') },
          { title: t('posAdmin.invoices.import.steps.mapping') },
          { title: t('posAdmin.invoices.import.steps.preview') },
        ]}
        style={{ marginBottom: 24 }}
      />

      {currentStep === 0 ? (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Alert
            type="info"
            showIcon
            message={t('posAdmin.invoices.import.supportedFormats')}
            description={t('posAdmin.invoices.import.supportedFormatsDescription')}
          />
          <input
            ref={fileInputRef}
            type="file"
            accept=".xlsx,.xlsm,.pdf"
            style={{ display: 'none' }}
            onChange={handleFileSelected}
          />
          <Space wrap>
            <Button icon={<UploadOutlined />} onClick={handleChooseFile}>
              {t('common.upload')}
            </Button>
            <Typography.Text type={selectedFile ? undefined : 'secondary'}>
              {selectedFile?.name || t('posAdmin.invoices.import.noFileSelected')}
            </Typography.Text>
          </Space>
        </Space>
      ) : null}

      {currentStep === 1 && previewData ? (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Alert
            type={hasDuplicateMappings || missingMappings.length > 0 ? 'warning' : 'success'}
            showIcon
            message={t('posAdmin.invoices.import.mappingRequired')}
            description={
              hasDuplicateMappings
                ? t('posAdmin.invoices.import.mappingDuplicate')
                : missingMappings.length > 0
                  ? t('posAdmin.invoices.import.mappingMissingFields', {
                      fields: missingMappings.map((field) => t(`posAdmin.invoices.import.fields.${IMPORT_FIELD_LABELS[field]}`)).join(' / '),
                    })
                  : t('posAdmin.invoices.import.mappingReady')
            }
          />

          <Form layout="vertical">
            <Space wrap style={{ width: '100%' }} size={16}>
              {REQUIRED_IMPORT_FIELDS.map((field) => (
                <Form.Item
                  key={field}
                  label={t(`posAdmin.invoices.import.fields.${IMPORT_FIELD_LABELS[field]}`)}
                  required
                  style={{ minWidth: 220, flex: '1 1 220px', marginBottom: 0 }}
                >
                  <Select
                    value={columnMapping[IMPORT_FIELD_MAPPING_KEYS[field]] ?? undefined}
                    options={mappingFieldOptions(field)}
                    placeholder={t('posAdmin.invoices.import.selectColumn')}
                    onChange={(value) => {
                      // 列映射确认在前端先做互斥控制，避免用户把同一列误选给多个核心字段。
                      setColumnMapping((current) => ({
                        ...current,
                        [IMPORT_FIELD_MAPPING_KEYS[field]]: value,
                      }))
                    }}
                  />
                </Form.Item>
              ))}
            </Space>
          </Form>

          <Table
            rowKey="key"
            pagination={false}
            size="small"
            dataSource={previewData.sourceColumns}
            columns={[
              {
                title: t('posAdmin.invoices.import.sourceColumnIndex'),
                dataIndex: 'key',
                width: 110,
                render: (value: string) => value,
              },
              {
                title: t('posAdmin.invoices.import.sourceColumnName'),
                dataIndex: 'header',
                render: (value?: string) => value || t('posAdmin.invoices.import.unnamedColumn'),
              },
              {
                title: t('posAdmin.invoices.import.sourceSampleValue'),
                key: 'sampleValue',
                render: (_, record) => resolveSourceColumnSampleValue(record, previewData.lines) || '--',
              },
            ]}
          />
        </Space>
      ) : null}

      {currentStep === 2 && previewData ? (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          {!headerDraft.storeCode ? (
            <Alert
              type="warning"
              showIcon
              message={t('posAdmin.invoices.import.storeNeedsSelection')}
              description={t('posAdmin.invoices.import.parsedStoreHint', { value: parsedStoreText })}
            />
          ) : null}
          {!headerDraft.supplierCode ? (
            <Alert
              type="warning"
              showIcon
              message={t('posAdmin.invoices.import.supplierNeedsSelection')}
              description={t('posAdmin.invoices.import.parsedSupplierHint', { value: parsedSupplierText })}
            />
          ) : null}
          {previewData.warnings.length > 0 ? (
            <Alert
              type="warning"
              showIcon
              message={t('posAdmin.invoices.import.warnings')}
              description={
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {previewData.warnings.map((warning, index) => (
                    <li key={index}>{warning}</li>
                  ))}
                </ul>
              }
            />
          ) : null}
          {previewData.errors.length > 0 ? (
            <Alert
              type="error"
              showIcon
              message={t('posAdmin.invoices.import.errors')}
              description={
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {previewData.errors.map((error, index) => (
                    <li key={index}>{error}</li>
                  ))}
                </ul>
              }
            />
          ) : null}

          <Form layout="vertical">
            <Space wrap style={{ width: '100%' }} size={16}>
              <Form.Item
                label={t('column.store')}
                required
                style={{ minWidth: 220, flex: '1 1 220px', marginBottom: 0 }}
              >
                <Select
                  showSearch
                  optionFilterProp="label"
                  value={headerDraft.storeCode}
                  options={storeOptions}
                  placeholder={t('form.pleaseSelectStore')}
                  onChange={(value) => {
                    setHeaderDraft((current) => ({
                      ...current,
                      storeCode: value,
                    }))
                  }}
                />
              </Form.Item>
              <Form.Item
                label={t('column.supplier')}
                required
                style={{ minWidth: 220, flex: '1 1 220px', marginBottom: 0 }}
              >
                <Select
                  showSearch
                  optionFilterProp="label"
                  value={headerDraft.supplierCode}
                  options={supplierOptions}
                  placeholder={t('form.pleaseSelectSupplier')}
                  onChange={(value) => {
                    setHeaderDraft((current) => ({
                      ...current,
                      supplierCode: value,
                    }))
                  }}
                />
              </Form.Item>
              <Form.Item
                label={t('posAdmin.invoices.invoiceNo')}
                required
                style={{ minWidth: 220, flex: '1 1 220px', marginBottom: 0 }}
              >
                <Input
                  value={headerDraft.invoiceNo}
                  placeholder={t('posAdmin.invoices.invoiceNoRequired')}
                  onChange={(event) => {
                    setHeaderDraft((current) => ({
                      ...current,
                      invoiceNo: event.target.value,
                    }))
                  }}
                />
              </Form.Item>
              <Form.Item
                label={t('posAdmin.invoices.orderDate')}
                style={{ minWidth: 220, flex: '1 1 220px', marginBottom: 0 }}
              >
                <DatePicker
                  style={{ width: '100%' }}
                  value={headerDraft.orderDate ? dayjs(headerDraft.orderDate) : null}
                  onChange={(value) => {
                    setHeaderDraft((current) => ({
                      ...current,
                      orderDate: value ? value.format('YYYY-MM-DD') : undefined,
                    }))
                  }}
                />
              </Form.Item>
              <Form.Item
                label={t('column.totalAmount')}
                style={{ minWidth: 220, flex: '1 1 220px', marginBottom: 0 }}
              >
                <Input value={headerDraft.totalAmount?.toFixed(2) || '--'} readOnly />
              </Form.Item>
              <Form.Item
                label={t('posAdmin.invoices.import.previewLineCount')}
                style={{ minWidth: 220, flex: '1 1 220px', marginBottom: 0 }}
              >
                <Input value={String(mappedPreviewLines.length)} readOnly />
              </Form.Item>
            </Space>
          </Form>

          <Table
            rowKey="key"
            size="small"
            pagination={{ pageSize: 8, showSizeChanger: false }}
            dataSource={mappedPreviewLines}
            columns={[
              {
                title: t('posAdmin.invoices.import.previewRow'),
                key: 'rowNumber',
                width: 90,
                render: (_, record, index) => record.rowNumber ?? index + 1,
              },
              {
                title: t('posAdmin.invoiceDetail.itemNumber'),
                dataIndex: 'itemNumber',
                render: (value: string) => value || '--',
              },
              {
                title: t('posAdmin.invoiceDetail.barcode'),
                dataIndex: 'barcode',
                render: (value: string) => value || '--',
              },
              {
                title: t('posAdmin.invoiceDetail.productName'),
                dataIndex: 'productName',
                render: (value: string) => value || '--',
              },
              {
                title: t('posAdmin.invoiceDetail.quantity'),
                dataIndex: 'quantity',
                width: 110,
                align: 'right',
                render: (value?: number) => (value === undefined ? '--' : value),
              },
              {
                title: t('posAdmin.invoices.import.price'),
                dataIndex: 'price',
                width: 120,
                align: 'right',
                render: (value?: number) => (value === undefined ? '--' : value.toFixed(2)),
              },
              {
                title: t('posAdmin.invoiceDetail.amount'),
                dataIndex: 'amount',
                width: 120,
                align: 'right',
                render: (value?: number) => (value === undefined ? '--' : value.toFixed(2)),
              },
            ]}
          />
        </Space>
      ) : null}
    </Modal>
  )
}
