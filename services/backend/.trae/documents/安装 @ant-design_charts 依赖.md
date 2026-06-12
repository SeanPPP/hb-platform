## 修复计划

### 问题
前端构建失败，缺少 `@ant-design/charts` 依赖包。销售看板页面使用了图表组件（Line、Column 等），但项目中没有安装这个依赖。

### 修复方案
在 `package.json` 的 `dependencies` 中添加 `@ant-design/charts` 包。

### 修改内容
在 `package.json` 第 14 行后添加：
```json
"@ant-design/charts": "^2.0.0",
```

### 执行步骤
1. 修改 `package.json` 添加依赖
2. 运行 `npm install` 安装新依赖
3. 重新运行 `npm run dev` 启动前端服务

### 预期结果
前端服务正常启动，销售看板页面可以正常显示图表。