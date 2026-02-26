I will fix the issue where the mobile store selector is empty by sharing the store data from the PC layout to the global state.

### Plan
1.  **Update Global State Definition**: Modify `src/app.ts` to include `userStores` in the `InitialState` interface.
2.  **Populate Global State**: Update `src/layouts/StoreLayout.tsx` to save the fetched stores into `initialState` so it's accessible globally.
3.  **Use Global State in Mobile**: Update `src/pages/StoreHome/index.tsx` to read `userStores` directly from `initialState` instead of fetching it again.

### Implementation Steps
1.  **Modify `src/app.ts`**:
    *   Add `userStores?: UserStoreDto[];` to the `InitialState` interface.
2.  **Modify `src/layouts/StoreLayout.tsx`**:
    *   In `fetchUserStores`, after successfully fetching data, call `setInitialState((s) => ({ ...s, userStores: stores }));`.
3.  **Modify `src/pages/StoreHome/index.tsx`**:
    *   Remove the `fetchUserStores` function and `useEffect`.
    *   Remove the local `userStores` state.
    *   Use `initialState?.userStores || []` for the selector options.