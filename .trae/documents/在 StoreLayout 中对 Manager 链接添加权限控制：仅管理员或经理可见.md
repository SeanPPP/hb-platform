我将修改 StoreLayout，使“Return to Home”链接（代码中显示为 "Manager" 链接，根据用户提供的 snippet）仅对非“无角色”且非“User”角色的用户显示。

### 1. 修改 StoreLayout.tsx

*   **当前代码**：
    ```tsx
    // Line 206
    <span onClick={() => history.push('/home')} style={{ cursor: 'pointer' }}>Manager</span>
    ```
    *注意：之前我以为我加的是 `<a>` 标签，但读取到的文件内容显示是一个 `<span>`，且文字是 "Manager"。这可能是我记忆有误，或者之前的 edit 没生效，或者被用户修改了。不管怎样，我将基于**读取到的文件内容**进行修改。*

*   **修改逻辑**：
    *   使用 `access` 对象进行判断。
    *   条件：`!access.isUser && (access.isAdmin || access.isManager || access.isWarehouseStaff || access.isStoreStaff)`。
    *   或者更简单的逻辑：如果不只是 `isUser`，且不仅仅是 `no role`。
    *   通常，如果用户是 Admin/Manager，他们可能同时也拥有 User 角色（取决于系统设计）。
    *   用户指令：“无角色 和用户（user）角色 不显示”。
    *   这意味着：只有当用户拥有 **除了 User 以外的其他角色**（如 Admin, Manager）时才显示。或者简单地说，`isAdmin || isManager`。

    *   让我们看看 `access.ts` 的定义：
        ```typescript
        const isAdmin = hasRole('Admin') || hasRole('管理员');
        const isManager = hasRole('Manager') || hasRole('经理');
        const isUser = hasRole('User') || hasRole('用户');
        ```
    *   如果我只用 `access.isAdmin || access.isManager`，这已经排除了仅有 User 角色的用户（假设 User 角色不包含 Admin 权限）。
    *   如果用户既是 Admin 又是 User，`isAdmin` 为 true，应该显示。
    *   如果用户只是 User，`isAdmin` 为 false，`isManager` 为 false，不显示。
    *   如果用户无角色，所有均为 false，不显示。

    **结论**：使用 `(access.isAdmin || access.isManager)` 作为判断条件是符合“无角色和User角色不显示”的要求的。如果还有其他角色（如 WarehouseStaff）也需要显示，可以加上。基于之前的上下文 "Manager" 链接，可能主要针对管理者。

*   **修正代码**：
    将：
    ```tsx
    <span onClick={() => history.push('/home')} style={{ cursor: 'pointer' }}>Manager</span>
    ```
    修改为：
    ```tsx
    {(access.isAdmin || access.isManager) && (
       <span onClick={() => history.push('/home')} style={{ cursor: 'pointer' }}>Manager</span>
    )}
    ```
    *注意：用户之前的指令是“添加 返回 http://localhost:8000/home 链接”。现在的代码是 `history.push('/home')`。如果这是同一个应用内的路由，那是对的。如果是跳出到另一个应用，应该用 `window.location.href`。鉴于用户现在的代码是 `history.push`，我将保持这个行为，只添加条件渲染。*

    **另外**，我之前的 `SearchReplace` 似乎把 "Return to Home" 链接加进去了，但读取的文件里没有？
    啊，读取结果显示 Line 206 是 `<span onClick={() => history.push('/home')} style={{ cursor: 'pointer' }}>Manager</span>`。
    这说明我之前的修改可能被覆盖了，或者用户手动改成了 "Manager"。
    我将遵循当前文件内容，对这个 "Manager" 链接添加权限控制。

### 2. 执行计划
修改 `src/layouts/StoreLayout.tsx`：
1.  找到 `<span onClick={() => history.push('/home')} ...>Manager</span>`。
2.  包裹在 `{(access.isAdmin || access.isManager) && ( ... )}` 中。
