I will restore the Home page to the default Umi template.

### 1. Verify `src/pages/Home/index.tsx`
*   **Current State**: The file already contains the standard Umi guide page (`Guide` component inside `PageContainer`).
*   **Action**: No changes are needed for this file as it already matches the user's request ("default Umi page").

### 2. Verify `.umirc.ts` Routing
*   **Current State**: In the previous turn, I updated the route `/home` to point to `./Home`.
*   **Check**: Ensure this configuration is still active and correct.
    ```typescript
    routes: [
      {
        path: '/home',
        component: './Home',
      }
    ]
    ```
*   **Action**: I will double-check `.umirc.ts` to confirm the routing is definitely pointing to `./Home` and wrapped in the `KeepAliveTabLayout`.

### 3. Conclusion
*   Since the file `src/pages/Home/index.tsx` is already the default page and the route was fixed in the previous step, I likely just need to confirm everything is in place. If the user is still seeing something else, it might be a caching issue or I need to check if `Guide` component was modified.
*   **Self-Correction**: I will also quickly check `src/components/Guide/index.tsx` to ensure it hasn't been modified to show something else.
