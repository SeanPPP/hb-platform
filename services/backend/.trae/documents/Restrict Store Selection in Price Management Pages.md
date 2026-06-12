# 计划：限制价格管理页面的分店选择权限

## 目标
修改 "分店多码商品价格管理" (`StoreMultiCodePrices`) 和 "分店零售价管理" (`StoreRetailPrices`) 页面，使非管理员用户只能查看和查询自己所属分店的数据。

## 步骤

### 1. 修改 `StoreMultiCodePrices/index.tsx`
- **文件**: `src/pages/PosAdmin/StoreMultiCodePrices/index.tsx`
- **操作**:
    - 引入 `useModel` 获取全局状态 `initialState`。
    - 从 `currentUser` 获取当前用户信息及关联分店 (`stores`)。
    - 判断用户是否为管理员 (`isAdmin`)。
    - **修改分店加载逻辑**:
        - **管理员**: 保持原逻辑，调用 `getActiveStores` 获取所有分店。
        - **非管理员**: 直接使用 `currentUser.stores` 填充下拉选项。
    - **设置默认查询**:
        - 非管理员且有分店数据时，自动选中第一个分店作为默认查询条件。

### 2. 修改 `StoreRetailPrices/index.tsx`
- **文件**: `src/pages/PosAdmin/StoreRetailPrices/index.tsx`
- **操作**:
    - 引入 `useModel` 获取全局状态。
    - 获取 `currentUser` 和 `isAdmin` 状态。
    - **修改筛选加载逻辑**:
        - **管理员**: 调用 `getActiveStores` 获取所有分店。
        - **非管理员**: 使用 `currentUser.stores`。
    - **设置默认查询**:
        - 确保默认选中用户所属的第一个分店。

## 验证
- **管理员**: 可以看到所有分店，可以查询任意分店。
- **非管理员**: 只能看到自己关联的分店，默认查询自己的分店，无法查询其他分店。