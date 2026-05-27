import {
  buildUpdateDeviceRegistrationPayload,
  normalizeDeviceRegistrationDetail,
} from './deviceRegistrationService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const normalizedChineseDetail = normalizeDeviceRegistrationDetail({
  ID: 12,
  设备硬件识别码: 'HW-CN-01',
  系统设备编号: 'POS-CN-01',
  分店代码: 'S001',
  分店名称: '示例分店',
  设备类型: 'POS',
  设备系统: 'Windows',
  设备状态: 1,
  设备状态描述: '已启用',
  备注: '中文字段',
  创建时间: '2026-05-01T10:00:00Z',
  最后修改时间: '2026-05-02T11:00:00Z',
  最后修改人: 'editor-cn',
  创建人: 'creator-cn',
})

assertEqual(normalizedChineseDetail.id, 12, 'Should normalize Chinese ID field')
assertEqual(
  normalizedChineseDetail.hardwareId,
  'HW-CN-01',
  'Should normalize Chinese hardware identifier field',
)
assertEqual(
  normalizedChineseDetail.storeName,
  '示例分店',
  'Should normalize Chinese store name field',
)
assertEqual(
  normalizedChineseDetail.remark,
  '中文字段',
  'Should normalize Chinese remark field',
)
assertEqual(
  normalizedChineseDetail.lastModifiedBy,
  'editor-cn',
  'Should normalize Chinese last modified by field',
)

const normalizedCamelDetail = normalizeDeviceRegistrationDetail({
  id: 13,
  hardwareId: 'HW-EN-01',
  systemDeviceNumber: 'POS-EN-01',
  storeCode: 'S002',
  storeName: 'Example Store',
  deviceType: 'Admin',
  deviceSystem: 'Mac',
  status: 3,
  statusDescription: 'Locked',
  remarks: 'camel field',
  createdAt: '2026-05-03T10:00:00Z',
  lastModified: '2026-05-04T11:00:00Z',
  lastModifiedBy: 'editor-en',
  createdBy: 'creator-en',
})

assertEqual(normalizedCamelDetail.id, 13, 'Should normalize camelCase ID field')
assertEqual(
  normalizedCamelDetail.statusDescription,
  'Locked',
  'Should normalize camelCase status description field',
)
assertEqual(
  normalizedCamelDetail.remark,
  'camel field',
  'Should normalize camelCase remark field',
)

const updateValuesWithRuntimeExtras = {
  deviceType: 'Mobile',
  deviceSystem: 'Android',
  remark: 'updated',
  status: 2,
  statusDescription: 'Disabled',
}

assertDeepEqual(
  buildUpdateDeviceRegistrationPayload(updateValuesWithRuntimeExtras),
  {
    设备类型: 'Mobile',
    设备系统: 'Android',
    备注: 'updated',
  },
  'Update payload should only include editable Chinese DTO fields',
)
