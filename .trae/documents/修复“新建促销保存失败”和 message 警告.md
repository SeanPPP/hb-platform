## 原因分析
- 保存失败高概率由“商品编码为空”的行导致：我们在新建时初始化了 `products: [{ productCode: '', unitWeight: 1 }]`，且已隐藏商品编码输入，用户未替换该空行就会提交空的 `productCode`，后端校验失败。
- antd 警告来源于直接使用静态 `message` 函数，React 19 + 动态主题下需使用 `App` 组件提供的上下文实例（`App.useApp()`）。该警告不影响保存，但建议修复。

## 修复方案
1) 移除新建时的空商品行
- 文件：`src/pages/PosAdmin/Promotions/index.tsx`
- 位置：`openCreate` 初始化表单（约 71-89 行）
- 修改：`products: []`（不再添加空行）

2) 保存前对商品明细做前端校验与清理
- 文件：`src/pages/PosAdmin/Promotions/index.tsx`
- 位置：`saveEditor`（约 120-159 行）构造 `payload` 处
- 调整：
  - 过滤掉 `productCode` 为空的行：`const items = (v.products||[]).filter(p=>p.productCode)`
  - 若 `items.length===0`，提示“至少选择一个商品”，并阻止提交
  - 统一权重默认与校验：`unitWeight = Number(p.unitWeight || 1)`，并校验 `>=1`
  - 将 `payload.products` 设置为清理后的 `items`

3) 修复 message 警告（可选，推荐）
- 文件：`src/pages/PosAdmin/Promotions/index.tsx` 与 `src/pages/PosAdmin/Promotions/ProductPicker.tsx`
- 做法：
  - 引入 `import { App } from 'antd'`
  - 在组件内：`const { message } = App.useApp()`
  - 将所有 `message.*` 调用替换为实例消息（不改语义）
- 若全局已有 `App` Provider（Umi 4 支持在布局或 `app.ts` 配置），可直接使用；否则需在顶层布局包裹 `App`。

## 验证
- 新建促销：未选商品时点击保存，前端提示；选择商品后保存成功，不再空编码。
- 编辑促销：仍可保存，且不会因空行导致失败。
- 控制台不再出现 antd message 静态函数的警告（完成第 3 步后）。