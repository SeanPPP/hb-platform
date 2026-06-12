我将在 StoreLayout 的顶部导航栏中添加一个 "Return to Home" 链接，该链接仅对非普通用户（如管理员或内部员工）可见。

### 1. 修改 StoreLayout.tsx

*   **引入 `useAccess`**：
    *   使用 Umi 的 `useAccess` hook 来获取当前用户的权限状态。
    *   根据 `src/access.ts`，我们可以使用 `access.isAdmin` 或 `access.isManager` 或 `access.isWarehouseStaff` 来判断是否为内部人员。
    *   或者反向判断：如果不仅仅是 `isUser`。

*   **条件渲染链接**：
    *   在顶部导航栏（Top Bar）的 "Log Out" 之前或 "My Details" 之后，添加一个新的 `<span>` 或 `<a>` 标签。
    *   链接地址：`http://localhost:8000/home` (或者相对路径 `/home`，如果是在同一应用下)。
    *   可见性条件：`access.isAdmin || access.isManager || access.isWarehouseStaff` （即非纯普通用户）。

*   **实现细节**：
    ```tsx
    const access = useAccess();
    // ...
    {/* Top Bar */}
    <div className={styles.topBar}>
      <div className={styles.container}>
        {/* ... existing items ... */}
        
        {(access.isAdmin || access.isManager) && (
           <a href="http://localhost:8000/home" style={{ color: 'inherit', marginLeft: 16 }}>
              Backend Home
           </a>
        )}

        <span onClick={handleLogout}>Log Out</span>
      </div>
    </div>
    ```

    *   注意：用户要求链接是 `http://localhost:8000/home`，这通常意味着跳回到管理端首页。
    *   用户提示“普通用户不显示”。根据 `access.ts`，我们可以检查 `!access.isUser` 或者 `access.isAdmin`。
    *   假设 "普通用户" 对应 `isUser` 角色，而管理员对应 `isAdmin`。为了安全起见，我会使用 `access.isAdmin` 作为判断条件，或者根据您的具体角色定义调整。

    **修正计划**：
    引入 `useAccess`。
    判断条件：`access.isAdmin` (假设只有管理员能看)。
    添加链接：`<a href="http://localhost:8000/home" ...>Return Home</a>`。

    *等待，如果是同一应用，是否应该用 `<Link>`？*
    如果 `/home` 是另一个布局（如 Ant Design Pro 的管理后台布局），则可以直接跳转。
    如果是完全不同的端口（比如现在是 8001，要跳回 8000），则必须用绝对 URL。
    用户明确给了 `http://localhost:8000/home`，这看起来像是一个硬编码的绝对路径。我会按此实现。

### 2. 执行计划
修改 `src/layouts/StoreLayout.tsx`：
1.  引入 `useAccess` from `@umijs/max`。
2.  在组件内调用 `const access = useAccess();`。
3.  在 Top Bar 区域添加条件渲染的链接。
