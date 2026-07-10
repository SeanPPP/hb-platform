import type { StoreOrderCategoryNode, StoreOrderProductQuery } from "@/modules/shop/types";

export interface HomeProductQueryInput {
  storeCode?: string | null;
  keyword: string;
  categoryGUID?: string;
  grade?: string;
  pageNumber: number;
  pageSize: number;
}

export interface VisibleCategoryRow {
  node: StoreOrderCategoryNode;
  depth: number;
  hasChildren: boolean;
  isExpanded: boolean;
}

export function buildHomeProductQuery(input: HomeProductQueryInput): StoreOrderProductQuery {
  const keyword = input.keyword.trim();

  return {
    storeCode: input.storeCode ?? undefined,
    // Home 单搜索框只查货号/条码；不传 productName，避免扩大成更慢的商品名模糊搜索。
    itemNumber: keyword || undefined,
    categoryGUID: input.categoryGUID,
    grade: input.grade,
    pageNumber: input.pageNumber,
    pageSize: input.pageSize,
    sortBy: "Default",
  };
}

export function buildCategoryNameMap(tree: StoreOrderCategoryNode[]) {
  const map = new Map<string, string>();
  const stack = [...tree];

  while (stack.length) {
    const node = stack.pop()!;
    map.set(node.categoryGUID, node.categoryName);
    stack.push(...(node.children ?? []));
  }

  return map;
}

export function flattenVisibleCategories(
  tree: StoreOrderCategoryNode[],
  expandedCategoryGUIDs: string[]
): VisibleCategoryRow[] {
  const expanded = new Set(expandedCategoryGUIDs);
  const rows: VisibleCategoryRow[] = [];

  function visit(nodes: StoreOrderCategoryNode[], depth: number) {
    for (const node of nodes) {
      const hasChildren = Boolean(node.children?.length);
      const isExpanded = expanded.has(node.categoryGUID);
      rows.push({ node, depth, hasChildren, isExpanded });

      if (hasChildren && isExpanded) {
        visit(node.children ?? [], depth + 1);
      }
    }
  }

  visit(tree, 0);
  return rows;
}
