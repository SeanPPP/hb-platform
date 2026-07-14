import type { Key } from 'react'
import type {
  BatchUnbindLocationProductsResult,
  LocationItem,
  LocationProductBinding,
  LocationProductUnbindFailure,
} from '../../../types/location'

interface BatchUnbindCoordinatorOptions {
  bindings: readonly LocationProductBinding[]
  locations: readonly LocationItem[]
  unbind: (bindings: LocationProductBinding[]) => Promise<BatchUnbindLocationProductsResult>
  refresh: () => Promise<LocationItem[] | undefined>
}

interface BatchUnbindCoordinatorOutcome {
  result: BatchUnbindLocationProductsResult
  nextSelectedRowKeys: Key[]
  patchedData?: LocationItem[]
  shouldApplyPatchedData: boolean
}

export function hasUnbindableProducts(location: LocationItem): boolean {
  return location.products?.some((product) => Boolean(product.productCode?.trim())) ?? false
}

export function buildSelectedLocationProductBindings(
  locations: readonly LocationItem[],
  selectedRowKeys: readonly Key[],
): LocationProductBinding[] {
  const selectedLocationGuids = new Set(selectedRowKeys.map((key) => String(key)))
  const seenBindings = new Set<string>()
  const bindings: LocationProductBinding[] = []

  for (const location of locations) {
    if (!selectedLocationGuids.has(location.locationGuid)) {
      continue
    }

    for (const product of location.products ?? []) {
      const productCode = product.productCode?.trim()
      if (!productCode) {
        continue
      }

      // 服务端逐关联发送 DELETE，同一货位和商品代码只能请求一次。
      const bindingKey = JSON.stringify([location.locationGuid, productCode])
      if (seenBindings.has(bindingKey)) {
        continue
      }

      seenBindings.add(bindingKey)
      bindings.push({ locationGuid: location.locationGuid, productCode })
    }
  }

  return bindings
}

export function getSelectableFailedLocationKeys(
  locations: readonly LocationItem[],
  failures: readonly LocationProductUnbindFailure[],
): Key[] {
  const failedLocationGuids = new Set(failures.map((failure) => failure.locationGuid))
  const selectedLocationGuids = new Set<string>()

  for (const location of locations) {
    // 刷新后货位可能已消失或变空，只恢复当前仍能继续处理的失败货位。
    if (failedLocationGuids.has(location.locationGuid) && hasUnbindableProducts(location)) {
      selectedLocationGuids.add(location.locationGuid)
    }
  }

  return Array.from(selectedLocationGuids)
}

export function removeSucceededLocationProductBindings(
  locations: readonly LocationItem[],
  succeededBindings: readonly LocationProductBinding[],
): LocationItem[] {
  const succeededKeys = new Set(
    succeededBindings
      .map((binding) => {
        const productCode = binding.productCode.trim()
        return productCode ? JSON.stringify([binding.locationGuid, productCode]) : undefined
      })
      .filter((key): key is string => Boolean(key)),
  )

  return locations.map((location) => {
    const products = (location.products ?? []).filter((product) => {
      const productCode = product.productCode?.trim()
      if (!productCode) {
        return true
      }

      return !succeededKeys.has(JSON.stringify([location.locationGuid, productCode]))
    })

    // 未命中的货位保留原对象，命中的货位只复制必要层级，避免修改操作前快照。
    return products.length === location.products.length ? location : { ...location, products }
  })
}

export async function coordinateBatchUnbindLocationProducts({
  bindings,
  locations,
  unbind,
  refresh,
}: BatchUnbindCoordinatorOptions): Promise<BatchUnbindCoordinatorOutcome> {
  const result = await unbind([...bindings])

  if (result.succeeded.length === 0) {
    return {
      result,
      nextSelectedRowKeys: getSelectableFailedLocationKeys(locations, result.failed),
      shouldApplyPatchedData: false,
    }
  }

  const refreshedData = await refresh()
  if (refreshedData !== undefined) {
    return {
      result,
      nextSelectedRowKeys: getSelectableFailedLocationKeys(refreshedData, result.failed),
      shouldApplyPatchedData: false,
    }
  }

  // 服务端刷新失败时，本地先剔除成功项，保证下一次重试只发送仍失败的关联。
  const patchedData = removeSucceededLocationProductBindings(locations, result.succeeded)
  return {
    result,
    nextSelectedRowKeys: getSelectableFailedLocationKeys(patchedData, result.failed),
    patchedData,
    shouldApplyPatchedData: true,
  }
}
