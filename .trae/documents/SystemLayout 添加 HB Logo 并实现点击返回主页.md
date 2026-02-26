我将在 `SystemLayout.tsx` 中添加 HB logo，并实现点击返回后台主页的功能。

### 计划
1.  **添加必要的导入**: 引入 `history`、`Link` 和相关图标组件。
2.  **创建 Header 组件**: 添加一个 header 区域，包含 HB logo（参考 StoreLayout 的设计）。
3.  **添加点击事件**: 点击 logo 时使用 `history.push('/home')` 跳转到后台主页。
4.  **添加样式**: 添加光标样式 `cursor: 'pointer'` 提示可点击。

### 实施步骤
1.  **修改 `src/layouts/SystemLayout.tsx`**:
    *   添加 `import { history, Link } from '@umijs/max';`
    *   添加 `import { HomeOutlined } from '@ant-design/icons';`
    *   添加 `import styles from './SystemLayout.less';`
    *   创建 header JSX 结构，包含 HB logo。
    *   添加 `onClick={() => history.push('/home')}` 到 logo div。
2.  **创建 `src/layouts/SystemLayout.less`**:
    *   添加 `.logo`、`.logoInner`、`.hbLogo`、`.hbCircle`、`.hbText`、`.brandText`、`.brandMain`、`.brandSub`、`.hot`、`.bargain` 等样式类。