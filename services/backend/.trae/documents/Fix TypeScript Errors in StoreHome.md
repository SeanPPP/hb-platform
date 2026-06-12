I will fix the missing imports and state declarations in `src/pages/StoreHome/index.tsx` that caused the TypeScript errors.

### Plan
1.  **Fix Imports**: Add `import { getUserStores, UserStoreDto } from '@/services/storeService';` to the top of the file.
2.  **Fix State Initialization**: Update `useModel` to include `setInitialState`.
3.  **Fix State Variable**: Add `const [userStores, setUserStores] = useState<UserStoreDto[]>([]);` to the component body.

### Implementation Steps
1.  **Modify `src/pages/StoreHome/index.tsx`**:
    *   Insert the missing import statement.
    *   Update the `useModel` destructuring.
    *   Insert the `userStores` state declaration.