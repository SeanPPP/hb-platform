import type { WarehouseProductsTableQuery } from '../../../services/warehouseProductService';
import {
  ALL_PRODUCTS_FILTER_KEY,
  resolveCategoryProductFilterMode,
} from '../Categories/categoryProductFilters';

export type WarehouseProductColumnFilters = Record<string, string[]>;
export type WarehouseProductTableFilters = Record<string, readonly unknown[] | null | undefined>;

export function setFilterValues(
  filters: WarehouseProductColumnFilters,
  key: string,
  values?: Array<string | number | boolean | undefined | null>,
) {
  const normalizedValues = (values ?? [])
    .map((value) => value === undefined || value === null ? '' : String(value).trim())
    .filter(Boolean);
  if (!normalizedValues.length) {
    if (!(key in filters)) {
      return filters;
    }
    const nextFilters = { ...filters };
    delete nextFilters[key];
    return nextFilters;
  }
  return {
    ...filters,
    [key]: normalizedValues,
  };
}

export function buildRangeFilterTokens(min?: string | number, max?: string | number) {
  const tokens: string[] = [];
  if (min !== undefined && min !== null && String(min).trim()) {
    tokens.push(`gte:${String(min).trim()}`);
  }
  if (max !== undefined && max !== null && String(max).trim()) {
    tokens.push(`lte:${String(max).trim()}`);
  }
  return tokens;
}

export function findFilterTokenValue(values: string[] | undefined, prefix: 'gte:' | 'lte:') {
  return values?.find((value) => value.startsWith(prefix))?.slice(prefix.length) ?? '';
}

export function normalizeTableFilters(filters: WarehouseProductTableFilters): WarehouseProductColumnFilters {
  const filterKeyMap: Record<string, string> = {
    name: 'productName',
    labelPrice: 'oemPrice',
  };
  return Object.entries(filters).reduce<WarehouseProductColumnFilters>((current, [key, value]) => {
    if (key === 'categoryName' || !value?.length) {
      return current;
    }
    const mappedFilterKey = filterKeyMap[key] ?? key;
    const normalizedValues = value.map((item) => String(item).trim());
    return setFilterValues(current, mappedFilterKey, normalizedValues);
  }, {});
}

export function resolveCategoryFilterValueFromTableFilters(filters: WarehouseProductTableFilters) {
  const categoryValues = filters.categoryName?.map((value) => String(value).trim()).filter(Boolean) ?? [];
  return categoryValues[0] || ALL_PRODUCTS_FILTER_KEY;
}

export function buildCategoryQueryValue(
  categoryValue: string,
): Pick<WarehouseProductsTableQuery, 'categoryGuid' | 'uncategorizedOnly'> {
  const filterMode = resolveCategoryProductFilterMode(categoryValue);
  if (filterMode.type === 'category') {
    return { categoryGuid: filterMode.categoryGuid, uncategorizedOnly: false };
  }
  if (filterMode.type === 'uncategorized') {
    return { categoryGuid: undefined, uncategorizedOnly: true };
  }
  return { categoryGuid: undefined, uncategorizedOnly: false };
}

export function getSingleFilterValue(values?: string[]) {
  return values?.length === 1 ? values[0] : undefined;
}
