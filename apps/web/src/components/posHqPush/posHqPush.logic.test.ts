import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  defaultPushProductsToHqUpdateFields,
  pushProductsToHqUpdateFieldOptions,
} from '../../types/posProduct'
import {
  PUSH_TO_HQ_STORE_DIMENSION_FIELDS,
  buildPushToHqStoreOptionLabel,
  buildPushToHqStoreSelectOptions,
  createPushToHqStoreOptionsGuard,
  getNextPushToHqStoreSelection,
  getPushToHqStoreSelectAllState,
  hasPushToHqTargetStoreError,
  isPushToHqTargetStoreRequired,
  normalizePushToHqStoreOptions,
} from './storeSelection'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${message}。Expected: ${expectedText}, received: ${actualText}`)
  }
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

function extractSection(source: string, startText: string, endText: string) {
  const startIndex = source.indexOf(startText)
  assert(startIndex >= 0, `未找到代码片段：${startText}`)

  const endIndex = source.indexOf(endText, startIndex)
  assert(endIndex >= 0, `未找到结束片段：${endText}`)

  return source.slice(startIndex, endIndex)
}

const modalFile = path.resolve(process.cwd(), 'src/components/posHqPush/PosHqPushModal.tsx')
const modalSource = readFileSync(modalFile, 'utf8')
const zhLocaleSource = readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8')
const enLocaleSource = readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8')

async function main() {
  const failures: string[] = []

  const normalizeFailure = await runTest('HQ 分店选项应归一化最新接口数据', () => {
    const normalized = normalizePushToHqStoreOptions([
      { storeCode: ' 1001 ', storeName: 'Sunnybank' },
      { storeCode: '1001', storeName: 'Duplicate Sunnybank' },
      { storeCode: '1002', storeName: 'Garden City' },
      { storeCode: '   ', storeName: 'BlankCode' },
      { storeName: 'NoCode' },
      null,
      'not-an-object',
    ])

    assertDeepEqual(
      normalized,
      [
        { storeCode: '1001', storeName: 'Sunnybank' },
        { storeCode: '1002', storeName: 'Garden City' },
      ],
      '应保留带 storeCode 的合法选项并去除空白编码',
    )
    assertDeepEqual(normalizePushToHqStoreOptions(null), [], '非数组输入应归一为空数组')
  })
  if (normalizeFailure) failures.push(normalizeFailure)

  const labelFailure = await runTest('HQ 分店选项应显示名称（编码）', () => {
    assertEqual(
      buildPushToHqStoreOptionLabel({ storeCode: '1001', storeName: 'Sunnybank' }),
      'Sunnybank（1001）',
      '有名称时应显示 名称（编码）',
    )
    assertEqual(
      buildPushToHqStoreOptionLabel({ storeCode: '1001' }),
      '1001',
      '缺少名称时应回退为编码',
    )
    assertDeepEqual(
      buildPushToHqStoreSelectOptions([
        { storeCode: '1001', storeName: 'Sunnybank' },
        { storeCode: '1002' },
      ]),
      [
        { value: '1001', label: 'Sunnybank（1001）' },
        { value: '1002', label: '1002' },
      ],
      'Select options 应使用 storeCode 作为 value，名称（编码）作为 label',
    )
  })
  if (labelFailure) failures.push(labelFailure)

  const selectAllStateFailure = await runTest('全选所有分店复选框应支持 checked/indeterminate', () => {
    const allCodes = ['1001', '1002', '1003']
    assertDeepEqual(
      getPushToHqStoreSelectAllState(allCodes, allCodes),
      { checked: true, indeterminate: false },
      '全选时应为 checked',
    )
    assertDeepEqual(
      getPushToHqStoreSelectAllState(['1001'], allCodes),
      { checked: false, indeterminate: true },
      '部分选择时应为 indeterminate',
    )
    assertDeepEqual(
      getPushToHqStoreSelectAllState([], allCodes),
      { checked: false, indeterminate: false },
      '未选择时不应显示 checked 或 indeterminate',
    )
    assertDeepEqual(
      getNextPushToHqStoreSelection(true, allCodes),
      allCodes,
      '勾选全选应返回最新全部选项',
    )
    assertDeepEqual(
      getNextPushToHqStoreSelection(false, allCodes),
      [],
      '取消全选应清空选择',
    )
  })
  if (selectAllStateFailure) failures.push(selectAllStateFailure)

  const targetStoreValidationFailure = await runTest('分店维度字段必须至少选择一个目标分店', () => {
    assertDeepEqual(
      PUSH_TO_HQ_STORE_DIMENSION_FIELDS,
      ['supplierCode', 'storePurchasePrice', 'storeRetailPrice', 'storeMultiCodes'],
      '分店维度字段应只包含供应商编码、分店进货价、分店零售价和分店一品多码',
    )
    assertEqual(isPushToHqTargetStoreRequired(['storeRetailPrice']), true, '分店零售价应要求目标分店')
    assertEqual(isPushToHqTargetStoreRequired(['supplierCode']), true, '供应商编码应要求目标分店')
    assertEqual(isPushToHqTargetStoreRequired(['storePurchasePrice']), true, '分店进货价应要求目标分店')
    assertEqual(isPushToHqTargetStoreRequired(['storeMultiCodes']), true, '分店一品多码应要求目标分店')
    assertEqual(
      PUSH_TO_HQ_STORE_DIMENSION_FIELDS.includes('productType'),
      false,
      '商品类型不应加入分店维度字段清单',
    )
    assertEqual(isPushToHqTargetStoreRequired(['productType']), false, '仅商品类型不应要求目标分店')
    assertEqual(isPushToHqTargetStoreRequired(['productName']), false, '仅全局字段不应要求目标分店')
    assertEqual(isPushToHqTargetStoreRequired([]), false, '空字段选择不应要求目标分店')

    assertEqual(
      hasPushToHqTargetStoreError(['storeRetailPrice'], []),
      true,
      '分店维度字段且未选目标分店时应校验失败',
    )
    assertEqual(
      hasPushToHqTargetStoreError(['storeRetailPrice'], ['1001']),
      false,
      '分店维度字段且已选目标分店时应通过',
    )
    assertEqual(
      hasPushToHqTargetStoreError(['productName'], []),
      false,
      '仅全局字段且未选目标分店时应通过',
    )
    assertEqual(
      hasPushToHqTargetStoreError(['productType'], []),
      false,
      '仅商品类型且未选目标分店时应通过',
    )
  })
  if (targetStoreValidationFailure) failures.push(targetStoreValidationFailure)

  const optionsFetchGuardFailure = await runTest('HQ 分店选项获取应单飞并忽略过期响应', () => {
    const guard = createPushToHqStoreOptionsGuard()

    const first = guard.begin()
    assertEqual(first, 1, '首次请求应占用单飞锁并返回请求序号')
    assertEqual(guard.isBusy(), true, '请求进行中应视为忙碌')
    assertEqual(guard.begin(), -1, '同一时刻应只允许一个 HQ 分店选项请求')
    assertEqual(guard.isLatest(first), true, '进行中的请求应被视为最新请求')

    guard.invalidate()
    assertEqual(guard.isLatest(first), false, '取消后旧请求不应再写入状态')
    assertEqual(guard.isBusy(), false, '取消应释放单飞锁')

    const second = guard.begin()
    assertEqual(second, 3, '取消应递增请求序号，之后应允许重新获取最新选项')
    assertEqual(guard.isLatest(first), false, '新请求开始后旧响应应被忽略')
    assertEqual(guard.isLatest(second), true, '新请求应是最新请求')

    guard.complete(first)
    assertEqual(guard.isBusy(), true, '过期请求完成不应释放新请求占用的单飞锁')
    guard.complete(second)
    assertEqual(guard.isBusy(), false, '最新请求完成应释放单飞锁')
  })
  if (optionsFetchGuardFailure) failures.push(optionsFetchGuardFailure)

  const productTypeFieldFailure = await runTest('共享发送 HQ 字段应在英文名称后包含商品类型', () => {
    const englishNameIndex = pushProductsToHqUpdateFieldOptions.findIndex(
      (field) => field.value === 'englishName',
    )
    assertEqual(
      pushProductsToHqUpdateFieldOptions[englishNameIndex + 1]?.value,
      'productType',
      '商品类型应紧跟英文名称',
    )
    assertDeepEqual(
      pushProductsToHqUpdateFieldOptions.find((field) => field.value === 'productType'),
      {
        value: 'productType',
        labelKey: 'containers.updateFields.hqProductType',
        fallbackLabel: '商品类型',
      },
      '商品类型选项应使用共享中英文文案键',
    )
  })
  if (productTypeFieldFailure) failures.push(productTypeFieldFailure)

  const modalUiFailure = await runTest('共享发送 HQ 弹窗应复用分店选择控件并保持 640px 管理后台视觉', () => {
    assertEqual(defaultPushProductsToHqUpdateFields.length, 17, '发送弹窗应默认勾选 17 个 HQ 字段')
    assertDeepEqual(
      defaultPushProductsToHqUpdateFields,
      pushProductsToHqUpdateFieldOptions.map((field) => field.value),
      '默认选项应覆盖发送 HQ 字段清单',
    )
    assert(
      modalSource.includes('width="min(640px, calc(100vw - 32px))"'),
      '弹窗应保持 640px 基线并限制窄屏最大宽度',
    )
    assert(
      modalSource.includes('mode="multiple"') &&
        modalSource.includes('maxTagCount="responsive"') &&
        modalSource.includes('popupRender='),
      '分店选择应使用多选 Select、responsive 标签折叠和 popupRender',
    )

    const selectSection = extractSection(
      modalSource,
      '<Select',
      'popupRender=',
    )
    assert(
      selectSection.includes('optionFilterProp="label"') &&
        selectSection.includes('buildPushToHqStoreSelectOptions(storeOptions)'),
      '分店 Select 应显示名称（编码）并支持按 label 搜索',
    )
    assert(
      modalSource.includes("t('posAdmin.products.pushToHqTargetStoresLabel'"),
      '分店选择上方应有独立标签',
    )

    const popupSection = extractSection(
      modalSource,
      'popupRender=',
      '<Checkbox.Group',
    )
    assert(
      popupSection.includes('indeterminate={selectAllState.indeterminate}') &&
        popupSection.includes('checked={selectAllState.checked}') &&
        popupSection.includes("t('posAdmin.products.selectAllStores'") &&
        popupSection.includes('getNextPushToHqStoreSelection(') &&
        modalSource.includes('getPushToHqStoreSelectAllState(targetStoreCodes, allStoreCodes)'),
      'popupRender 中应有带 checked/indeterminate 的全选所有分店复选框',
    )

    assert(
      modalSource.indexOf('<Select') < modalSource.indexOf('<Checkbox.Group'),
      '分店选择应位于 17 个字段上方',
    )
    assert(
      modalSource.includes('setTargetStoreCodes(storeOptions.map((option) => option.storeCode))'),
      '每次打开应默认全选最新 HQ 分店选项',
    )
    assert(
      modalSource.includes('defaultPushProductsToHqUpdateFields') &&
        modalSource.includes('pushProductsToHqUpdateFieldOptions.map'),
      '字段清单应复用共享 17 字段定义',
    )
    assert(
      modalSource.includes("t('containers.updateFields.selectAtLeastOne'") &&
        modalSource.includes('hasPushToHqTargetStoreError(selectedFields, targetStoreCodes)') &&
        modalSource.includes("t('posAdmin.products.pushToHqTargetStoresRequired'"),
      '空字段仍保留现有校验，分店维度字段未选目标分店时应拦截提交',
    )
    assert(
      modalSource.includes("t('posAdmin.products.pushToHqTargetStoresHint'") &&
        modalSource.includes("'containers.updateFields.hqCreateHint'"),
      '说明应同时保留目标分店约束和新记录完整创建说明',
    )
    assert(
      modalSource.includes('storeOptionsError') &&
        modalSource.includes('okButtonProps=') &&
        modalSource.includes("t('common.retry'") &&
        modalSource.includes('onRetryStoreOptions'),
      '选项获取失败应禁止确认并提供重试',
    )
    assert(
      modalSource.includes('cancelButtonProps={{ disabled: confirmLoading }}') &&
        modalSource.includes('closable={!confirmLoading}') &&
        modalSource.includes('keyboard={!confirmLoading}') &&
        modalSource.includes('maskClosable={!confirmLoading}') &&
        modalSource.includes('if (confirmLoading) return') &&
        modalSource.includes('onCancel={handleCancel}'),
      '提交进行中应禁用取消按钮并忽略关闭、ESC 和 mask 取消，防止重开重复提交',
    )
  })
  if (modalUiFailure) failures.push(modalUiFailure)

  const localeFailure = await runTest('目标分店与商品类型文案应同时提供中英文翻译', () => {
    assert(
      zhLocaleSource.includes('"pushToHqTargetStoresLabel"') &&
        zhLocaleSource.includes('"pushToHqTargetStoresRequired"') &&
        zhLocaleSource.includes('"pushToHqTargetStoresHint"') &&
        zhLocaleSource.includes('"pushToHqStoreOptionsLoadFailed"') &&
        zhLocaleSource.includes('"hqProductType": "商品类型"'),
      '中文 locale 应包含目标分店文案和商品类型文案',
    )
    assert(
      enLocaleSource.includes('"pushToHqTargetStoresLabel"') &&
        enLocaleSource.includes('"pushToHqTargetStoresRequired"') &&
        enLocaleSource.includes('"pushToHqTargetStoresHint"') &&
        enLocaleSource.includes('"pushToHqStoreOptionsLoadFailed"') &&
        enLocaleSource.includes('"hqProductType": "Product Type"'),
      '英文 locale 应包含目标分店文案和商品类型文案',
    )
  })
  if (localeFailure) failures.push(localeFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('posHqPush.logic.test: ok')
}

await main()
