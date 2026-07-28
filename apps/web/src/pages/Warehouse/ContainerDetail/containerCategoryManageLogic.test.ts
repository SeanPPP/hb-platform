import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import {
  buildContainerCategoryParentOptions,
  collectContainerCategoryDescendantGuids,
  executeContainerCategoryMutation,
  resolveContainerCategoryTargetAfterMutation,
  resolveContainerCategorySelectionAfterRefresh,
} from './containerCategoryManageLogic'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const categories: WarehouseCategoryNode[] = [
  {
    categoryGUID: 'food',
    categoryName: 'Food',
    chineseName: '食品',
    isActive: true,
    children: [
      {
        categoryGUID: 'snack',
        parentGUID: 'food',
        categoryName: 'Snack',
        chineseName: '零食',
        isActive: true,
        children: [
          {
            categoryGUID: 'chips',
            parentGUID: 'snack',
            categoryName: 'Chips',
            chineseName: '薯片',
            isActive: true,
          },
        ],
      },
      {
        categoryGUID: 'drink',
        parentGUID: 'food',
        categoryName: 'Drink',
        chineseName: '饮料',
        isActive: true,
      },
    ],
  },
  {
    categoryGUID: 'home',
    categoryName: 'Home',
    chineseName: '家居',
    isActive: true,
  },
]
const categoriesAfterChipsDelete: WarehouseCategoryNode[] = [
  {
    ...categories[0],
    children: [
      {
        ...categories[0].children![0],
        children: [],
      },
      categories[0].children![1],
    ],
  },
  categories[1],
]

assertDeepEqual(
  collectContainerCategoryDescendantGuids(categories[0]),
  ['snack', 'chips', 'drink'],
  '应递归收集分类的全部后代',
)

assertDeepEqual(
  buildContainerCategoryParentOptions(categories, 'snack').map((option) => option.value),
  ['food', 'drink', 'home'],
  '编辑分类时父级选项应排除自身和全部后代，并保留祖先与兄弟分类',
)

assertDeepEqual(
  resolveContainerCategorySelectionAfterRefresh(categories, 'food', 'food', {
    kind: 'create',
    categoryGuid: 'drink',
    fallbackCategoryGuid: 'drink',
  }),
  {
    managedCategoryGuid: 'drink',
    activeTargetCategoryGuid: 'drink',
  },
  '新增分类后应选中新分类，并将其设为当前目标分类',
)

assertDeepEqual(
  resolveContainerCategorySelectionAfterRefresh(categories, 'snack', 'home', {
    kind: 'update',
    categoryGuid: 'snack',
    fallbackCategoryGuid: 'snack',
  }),
  {
    managedCategoryGuid: 'snack',
    activeTargetCategoryGuid: 'home',
  },
  '编辑分类后应保持原目标分类，并继续选中编辑项',
)

assertDeepEqual(
  resolveContainerCategorySelectionAfterRefresh(categoriesAfterChipsDelete, 'chips', 'chips', {
    kind: 'delete',
    categoryGuid: 'chips',
    fallbackCategoryGuid: 'snack',
  }),
  {
    managedCategoryGuid: 'snack',
    activeTargetCategoryGuid: undefined,
  },
  '删除当前目标分类后管理树应回退父级，但商品目标分类必须清空',
)

assertEqual(
  resolveContainerCategorySelectionAfterRefresh(categories, 'snack', 'missing', {
    kind: 'update',
    categoryGuid: 'snack',
    fallbackCategoryGuid: 'snack',
  }).activeTargetCategoryGuid,
  undefined,
  '刷新后已不存在的目标分类应清空',
)

assertEqual(
  resolveContainerCategoryTargetAfterMutation('food', {
    kind: 'create',
    categoryGuid: 'new-category',
    fallbackCategoryGuid: 'new-category',
  }),
  'new-category',
  '新增写入成功后应立即把新分类设为目标，不依赖分类树刷新',
)

assertEqual(
  resolveContainerCategoryTargetAfterMutation('chips', {
    kind: 'delete',
    categoryGuid: 'chips',
    fallbackCategoryGuid: 'snack',
  }),
  undefined,
  '删除写入成功后应立即清空匹配的目标 GUID，不依赖分类树刷新',
)

const committedChanges: string[] = []
let refreshAttempts = 0
const createRefreshError = new Error('分类树刷新失败')
const refreshTreeWithRetry = async () => {
  refreshAttempts += 1
  if (refreshAttempts === 1) {
    throw createRefreshError
  }
  return categories
}
const createOutcome = await executeContainerCategoryMutation(
  async () => ({
    kind: 'create',
    categoryGuid: 'created-category',
    fallbackCategoryGuid: 'created-category',
  }),
  refreshTreeWithRetry,
  (change) => committedChanges.push(change.categoryGuid),
)

assertDeepEqual(
  committedChanges,
  ['created-category'],
  '新增成功后即使刷新失败也应且只应提交一次选择同步',
)
assertEqual(
  createOutcome.refreshError,
  createRefreshError,
  '新增成功后的刷新错误应独立返回，不能把已成功写入误报为失败',
)
assertDeepEqual(
  await refreshTreeWithRetry(),
  categories,
  '刷新失败后重试应重新调用分类树接口并恢复最新树',
)
assertEqual(
  refreshAttempts,
  2,
  '首次刷新失败后重试应恰好多发起一次分类树请求',
)

const deleteCommittedChanges: string[] = []
const deleteRefreshError = new Error('删除后刷新失败')
const deleteOutcome = await executeContainerCategoryMutation(
  async () => ({
    kind: 'delete',
    categoryGuid: 'chips',
    fallbackCategoryGuid: 'snack',
  }),
  async () => {
    throw deleteRefreshError
  },
  (change) => deleteCommittedChanges.push(change.categoryGuid),
)

assertDeepEqual(
  deleteCommittedChanges,
  ['chips'],
  '删除成功后即使刷新失败也应立即同步一次删除结果',
)
assertEqual(
  deleteOutcome.refreshError,
  deleteRefreshError,
  '删除成功后的刷新错误应独立返回，不能保留已删除的目标 GUID',
)

console.log('container category manage logic tests passed')
