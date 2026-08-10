import {
  createProductCategory,
  getProductCategoryTree,
  updateProductCategory,
} from './productCategoryService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

// 深度比较对象时忽略键顺序（JSON.stringify 对键序敏感，会让归一化后的等价对象误判失败），数组仍按索引顺序比较。
function findDeepMismatch(actual: unknown, expected: unknown): string | undefined {
  if (Array.isArray(actual) || Array.isArray(expected)) {
    if (!Array.isArray(actual) || !Array.isArray(expected) || actual.length !== expected.length) {
      return '数组结构不一致'
    }
    for (let index = 0; index < actual.length; index++) {
      const mismatch = findDeepMismatch(actual[index], expected[index])
      if (mismatch) {
        return `[${index}]${mismatch}`
      }
    }
    return undefined
  }

  if (isPlainObject(actual) || isPlainObject(expected)) {
    if (!isPlainObject(actual) || !isPlainObject(expected)) {
      return '对象结构不一致'
    }
    const actualKeys = Object.keys(actual)
    if (actualKeys.length !== Object.keys(expected).length) {
      return `键集合不一致（实际 ${actualKeys.sort().join(',')}，期望 ${Object.keys(expected).sort().join(',')}）`
    }
    for (const key of Object.keys(expected)) {
      if (!Object.prototype.hasOwnProperty.call(actual, key)) {
        return `缺少键 ${key}`
      }
      const mismatch = findDeepMismatch(actual[key], expected[key])
      if (mismatch) {
        return `.${key}${mismatch}`
      }
    }
    return undefined
  }

  return actual === expected ? undefined : '值不一致'
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const mismatch = findDeepMismatch(actual, expected)
  if (mismatch) {
    throw new Error(
      `${message}。Expected: ${JSON.stringify(expected)}, received: ${JSON.stringify(actual)}（差异：${mismatch}）`,
    )
  }
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined
const capturedUrls: string[] = []
const nextPayloads: unknown[] = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedInit = init
  capturedUrls.push(String(input))

  return new Response(JSON.stringify(nextPayloads.shift() ?? {}), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  nextPayloads.push({
    success: true,
    data: [
      {
        categoryGUID: 'g1',
        categoryName: '根分类',
        parentGUID: null,
        isActive: true,
        sortOrder: 1,
        children: [
          {
            categoryGUID: 'g2',
            categoryName: '子分类',
            parentGUID: 'g1',
            isActive: false,
            sortOrder: 2,
          },
        ],
      },
    ],
  })

  const tree = await getProductCategoryTree()
  assertEqual(capturedUrl, '/api/react/v1/product-categories/tree', '分类树应调用 tree 接口')
  assertDeepEqual(
    tree,
    [
      {
        guid: 'g1',
        name: '根分类',
        sortOrder: 1,
        isActive: true,
        children: [
          {
            guid: 'g2',
            name: '子分类',
            parentGuid: 'g1',
            sortOrder: 2,
            isActive: false,
          },
        ],
      },
    ],
    '分类树应递归归一化 camelCase categoryGUID/categoryName/parentGUID/children 为 guid/name/parentGuid/isActive',
  )

  nextPayloads.push({
    success: true,
    data: [
      {
        CategoryGUID: 'p1',
        CategoryName: 'P根',
        ParentGUID: null,
        IsActive: true,
        SortOrder: 0,
        Children: [
          {
            CategoryGUID: 'p2',
            CategoryName: 'P子',
            ParentGUID: 'p1',
            IsActive: true,
          },
        ],
      },
    ],
  })

  const pascalTree = await getProductCategoryTree()
  assertDeepEqual(
    pascalTree,
    [
      {
        guid: 'p1',
        name: 'P根',
        sortOrder: 0,
        isActive: true,
        children: [
          {
            guid: 'p2',
            name: 'P子',
            parentGuid: 'p1',
            isActive: true,
          },
        ],
      },
    ],
    '分类树应兼容 PascalCase 字段并递归归一化',
  )

  nextPayloads.push({
    success: true,
    data: [{ CategoryGuid: 'm1', categoryName: '混合形式' }],
  })

  const mixedTree = await getProductCategoryTree()
  assertEqual(mixedTree[0].guid, 'm1', '分类树应兼容 CategoryGuid 形式')
  assertEqual(mixedTree[0].name, '混合形式', '分类树应兼容 categoryName 形式')
  assertEqual(mixedTree[0].isActive, true, '分类树缺省 isActive 应默认启用')

  nextPayloads.push({
    success: true,
    data: {
      categoryGUID: 'new1',
      categoryName: '新分类',
      parentGUID: null,
      isActive: true,
      sortOrder: 3,
    },
  })

  const createdCategory = await createProductCategory({
    name: '新分类',
    parentGuid: 'parent-1',
    sortOrder: 3,
  })
  assertEqual(capturedUrl, '/api/react/v1/product-categories', '创建分类应调用分类接口')
  assertEqual(capturedInit?.method, 'POST', '创建分类应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      categoryName: '新分类',
      parentGUID: 'parent-1',
      sortOrder: 3,
      isActive: true,
    },
    '创建分类应发送 categoryName/parentGUID/sortOrder/isActive',
  )
  assertEqual(createdCategory.guid, 'new1', '创建分类返回应统一走 normalizer')
  assertEqual(createdCategory.name, '新分类', '创建分类返回应归一化 name')

  capturedUrls.length = 0
  nextPayloads.push({
    success: true,
    data: {
      categoryGUID: 'u1',
      categoryName: '更新分类',
      parentGUID: 'parent-2',
      isActive: false,
      sortOrder: 5,
    },
  })

  const updatedCategory = await updateProductCategory('u1', {
    name: '更新分类',
    parentGuid: 'parent-2',
    sortOrder: 5,
    isActive: false,
  })
  assertEqual(capturedUrl, '/api/react/v1/product-categories/u1', '更新分类应调用对应接口')
  assertEqual(capturedInit?.method, 'PUT', '更新分类应使用 PUT')
  assertDeepEqual(
    capturedUrls,
    ['/api/react/v1/product-categories/u1'],
    '调用方已传 isActive 时更新分类不应额外读取分类树',
  )
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      categoryGUID: 'u1',
      categoryName: '更新分类',
      parentGUID: 'parent-2',
      sortOrder: 5,
      isActive: false,
    },
    '更新分类应包含 categoryGUID 并保留调用方传入的 isActive',
  )
  assertEqual(updatedCategory.guid, 'u1', '更新分类返回应统一走 normalizer')
  assertEqual(updatedCategory.isActive, false, '更新分类返回应保留 isActive')

} finally {
  globalThis.fetch = originalFetch
}
