## 目标
- 在“新建收银用户”和“编辑收银用户”弹窗中，实时显示用户条码的 Code128 图片预览

## 技术方案
- 使用现有依赖 react-barcode（已在项目中安装）渲染 Code128 条码
- 在页面引入 Barcode 组件：import Barcode from 'react-barcode'
- 通过 antd Form.useWatch 监听 userBarcode 字段值（createForm / editForm），实时更新预览
- 条码为空时不显示预览；有值时显示 Code128 SVG

## 修改点
- 文件：src/pages/PosAdmin/CashRegisterUsers/index.tsx
- 变更：
  - 引入 react-barcode
  - 在新建弹窗的“用户条码”表单项下添加预览区域，使用 createBarcodeVal 渲染
  - 在编辑弹窗的“用户条码”表单项下添加预览区域，使用 editBarcodeVal 渲染
  - 预览样式：适度宽度（如 280-320px），高度由组件自适应；行颜色与背景保持默认

## 验证
- 新建弹窗自动生成的13位条码应立即显示为 Code128 图片
- 编辑弹窗加载原条码后显示图片；点击“更换条码”后图片随值更新
- 提交与校验逻辑不受影响