I will modify `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\SystemSettings\DeviceManagement\index.tsx` to implement the requested filtering changes.

**Planned Changes:**

1.  **Update Table Columns (`columns` definition):**
    *   **Enable Search for Branch Code**: Remove `search: false` from the `分店代码` column configuration.
    *   **Disable Search for Hardware IDs**: Add `search: false` to `设备硬件识别码` (Device Hardware ID) and `系统设备编号` (System Device Number) columns.
    *   **Verify Device Status**: Ensure `设备状态` (Device Status) remains searchable (default behavior).

2.  **Update Data Request Logic (`request` function):**
    *   Modify the `request` function in `ProTable` to correctly map the frontend search parameters to the backend's `FilterModel`.
    *   Specifically, map `params.分店代码` and `params.设备状态` to the `FilterModel` expected by the `DeviceRegistrationReactService`.
    *   `分店代码` will use `contains` match.
    *   `设备状态` will use `equals` match.

**Technical Detail:**
The backend `DeviceRegistrationReactService` already supports these filters via `FilterModel`, so no backend changes are required. I will only update the frontend to pass these parameters correctly.