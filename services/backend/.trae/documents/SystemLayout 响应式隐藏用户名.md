1. 修改 `SystemLayout.tsx`：给用户名 span 添加 `className={styles.userName}`
2. 修改 `SystemLayout.less`：添加响应式媒体查询，在小屏幕下隐藏用户名显示

这样在小屏幕（768px 以下）时，只显示头像图标，不显示用户名文本。