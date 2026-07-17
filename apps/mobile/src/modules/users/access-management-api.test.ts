import assert from "node:assert/strict";
import Module from "node:module";

interface RequestRecord {
  body?: unknown;
  method: "get" | "post";
  url: string;
}

async function run() {
  Object.assign(globalThis, { __DEV__: false });

  const mockModule = (name: string, exports: object) => {
    const filename = require.resolve(name);
    const module = new Module(filename);
    module.filename = filename;
    module.loaded = true;
    module.exports = exports;
    require.cache[filename] = module;
  };

  // Node 测试只验证 HTTP 契约，不加载 Expo 原生运行时。
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    NativeModules: {},
    Platform: {
      OS: "ios",
      select: <T>(values: { ios?: T; default?: T }) =>
        values.ios ?? values.default,
    },
  });
  mockModule("expo-secure-store", {
    getItemAsync: async () => null,
    setItemAsync: async () => undefined,
    deleteItemAsync: async () => undefined,
  });
  mockModule("expo-location", {
    hasStartedLocationUpdatesAsync: async () => false,
    stopLocationUpdatesAsync: async () => undefined,
  });
  mockModule("@react-native-async-storage/async-storage", {
    default: {
      getItem: async () => null,
      setItem: async () => undefined,
      removeItem: async () => undefined,
    },
  });

  const { apiClient } = await import("../../shared/api/client");
  const {
    assignUserAccessRoles,
    assignUserAccessStores,
    assignUserDirectPermissions,
    fetchAccessPermissionCatalog,
    fetchAccessRoleCatalog,
    fetchAccessStoreCatalog,
    fetchUserAccessPermissionState,
    fetchUserAccessPermissionAccess,
    fetchUserAccessRoles,
    fetchUserAccessStores,
    normalizeAccessPermissionCatalog,
    normalizeAccessRoleList,
    normalizeUserAccessPermissionState,
    normalizeUserAccessPermissionAccess,
    normalizeUserAccessStoreList,
  } = await import("./access-management-api");
  const {
    accessPermissionCatalogQueryKey,
    accessRoleCatalogQueryKey,
    accessStoreCatalogQueryKey,
    userAccessPermissionStateQueryKey,
    userAccessPermissionAccessQueryKey,
    userAccessRolesQueryKey,
    userAccessStoresQueryKey,
  } = await import("./access-management-hooks");

  const requests: RequestRecord[] = [];
  const originalGet = apiClient.get;
  const originalPost = apiClient.post;
  apiClient.get = (async (url: string) => {
    requests.push({ method: "get", url });
    if (url.endsWith("/stores")) {
      return {
        data: {
          success: true,
          data: [
            {
              StoreGUID: "store-1",
              StoreCode: "S1",
              StoreName: "Store 1",
              IsActive: true,
              IsPrimary: true,
              AssignedAt: "2026-01-01T00:00:00Z",
            },
          ],
        },
      };
    }
    if (url.endsWith("/roles") && url.includes("/Users/")) {
      return {
        data: {
          success: true,
          data: [
            { RoleGUID: "role-1", RoleName: "StoreStaff", IsActive: true },
          ],
        },
      };
    }
    if (url.endsWith("/permissions/state")) {
      return {
        data: {
          success: true,
          data: {
            UserGuid: "user-1",
            IsSuperAdmin: false,
            ImplicitAllPermissions: false,
            InheritedPermissionCodes: ["Users.View"],
            DirectPermissionCodes: ["Users.Edit"],
            EffectivePermissionCodes: ["Users.View", "Users.Edit"],
            InheritedSources: [
              { RoleName: "StoreStaff", PermissionCodes: ["Users.View"] },
            ],
          },
        },
      };
    }
    if (url.endsWith("/access-permissions")) {
      return {
        data: {
          success: true,
          data: {
            state: {
              userGuid: "user-1",
              isSuperAdmin: false,
              implicitAllPermissions: false,
              inheritedPermissionCodes: ["Users.View"],
              directPermissionCodes: ["Users.Edit"],
              effectivePermissionCodes: ["Users.View", "Users.Edit"],
              inheritedSources: [
                { roleName: "StoreStaff", permissionCodes: ["Users.View"] },
              ],
            },
            categories: [
              {
                category: "用户管理",
                displayName: "用户管理",
                description: "用户管理相关权限",
                permissions: [
                  {
                    name: "Users.View",
                    displayName: "查看用户",
                    description: "页面 /system/users - 查看用户列表与详情",
                    category: "用户管理",
                    isSystemPermission: false,
                  },
                ],
              },
            ],
          },
        },
      };
    }
    if (url === "/Roles/active") {
      return { data: [{ roleGUID: "role-1", roleName: "StoreStaff" }] };
    }
    if (url === "/Roles/permissions") {
      return {
        data: [
          {
            category: "Users",
            displayName: "Users",
            permissions: [
              {
                name: "Users.View",
                displayName: "View users",
                category: "Users",
                isSystemPermission: true,
              },
            ],
          },
        ],
      };
    }
    throw new Error(`unexpected GET ${url}`);
  }) as typeof apiClient.get;
  apiClient.post = (async (url: string, body?: unknown) => {
    requests.push({ body, method: "post", url });
    return { data: { success: true, data: true } };
  }) as typeof apiClient.post;

  try {
    const encodedUserGuid = "user%2Fa%3F%20b";
    const userGuid = "user/a? b";
    const [stores, roles, state, roleCatalog, permissionCatalog] =
      await Promise.all([
        fetchUserAccessStores(userGuid),
        fetchUserAccessRoles(userGuid),
        fetchUserAccessPermissionState(userGuid),
        fetchAccessRoleCatalog(),
        fetchAccessPermissionCatalog(),
      ]);
    const storeCatalog = await fetchAccessStoreCatalog(async () => [
      {
        storeGUID: "catalog-store",
        storeCode: "CAT",
        storeName: "Catalog store",
      },
    ]);
    const permissionAccess = await fetchUserAccessPermissionAccess(userGuid);

    assert.deepEqual(stores, [
      {
        storeGUID: "store-1",
        storeCode: "S1",
        storeName: "Store 1",
        isActive: true,
        isManageable: true,
        assignedAt: "2026-01-01T00:00:00Z",
      },
    ]);
    assert.deepEqual(roles, [
      { roleGUID: "role-1", roleName: "StoreStaff", isActive: true },
    ]);
    assert.deepEqual(state, {
      userGuid: "user-1",
      isSuperAdmin: false,
      implicitAllPermissions: false,
      inheritedPermissionCodes: ["Users.View"],
      directPermissionCodes: ["Users.Edit"],
      effectivePermissionCodes: ["Users.View", "Users.Edit"],
      inheritedSources: [
        { roleName: "StoreStaff", permissionCodes: ["Users.View"] },
      ],
    });
    assert.equal(permissionAccess.state.userGuid, "user-1");
    assert.deepEqual(
      permissionAccess.categories.flatMap((category) =>
        category.permissions.map((permission) => permission.name),
      ),
      ["Users.View"],
      "聚合接口只暴露服务端已经按操作者范围裁剪的权限",
    );
    assert.deepEqual(
      normalizeUserAccessPermissionAccess({
        state: permissionAccess.state,
        categories: permissionAccess.categories,
      }),
      permissionAccess,
    );
    assert.equal(roleCatalog.length, 1);
    assert.equal(permissionCatalog[0]?.permissions[0]?.name, "Users.View");
    assert.deepEqual(storeCatalog, [
      {
        storeGUID: "catalog-store",
        storeCode: "CAT",
        storeName: "Catalog store",
      },
    ]);
    assert.deepEqual(userAccessStoresQueryKey(userGuid), [
      "userAccessManagement",
      "stores",
      userGuid,
    ]);
    assert.deepEqual(userAccessRolesQueryKey(userGuid), [
      "userAccessManagement",
      "roles",
      userGuid,
    ]);
    assert.deepEqual(userAccessPermissionStateQueryKey(userGuid), [
      "userAccessManagement",
      "permissionState",
      userGuid,
    ]);
    assert.deepEqual(userAccessPermissionAccessQueryKey(userGuid), [
      "userAccessManagement",
      "permissionAccess",
      userGuid,
    ]);
    assert.deepEqual(accessRoleCatalogQueryKey, [
      "userAccessManagement",
      "roleCatalog",
    ]);
    assert.deepEqual(accessPermissionCatalogQueryKey, [
      "userAccessManagement",
      "permissionCatalog",
    ]);
    assert.deepEqual(accessStoreCatalogQueryKey, [
      "userAccessManagement",
      "storeCatalog",
    ]);

    await assignUserAccessStores({
      userGuid,
      assignments: [
        { storeGUID: "store-1", isPrimary: false },
        { storeGUID: "store-2", isPrimary: true },
      ],
    });
    await assignUserAccessRoles({
      userGuid,
      roleGuids: ["role-store-manager", "role-1"],
      roleCatalog: [
        {
          roleGUID: "role-store-manager",
          roleName: "StoreManager",
          isActive: true,
        },
        { roleGUID: "role-1", roleName: "StoreStaff", isActive: true },
      ],
    });
    await assignUserDirectPermissions({
      userGuid,
      permissions: ["Users.View"],
    });

    assert.deepEqual(requests, [
      { method: "get", url: `/Users/guid/${encodedUserGuid}/stores` },
      { method: "get", url: `/Users/guid/${encodedUserGuid}/roles` },
      {
        method: "get",
        url: `/Users/guid/${encodedUserGuid}/permissions/state`,
      },
      { method: "get", url: "/Roles/active" },
      { method: "get", url: "/Roles/permissions" },
      {
        method: "get",
        url: `/Users/guid/${encodedUserGuid}/access-permissions`,
      },
      {
        method: "post",
        url: `/Users/guid/${encodedUserGuid}/stores`,
        body: [
          { StoreGUID: "store-1", IsPrimary: false },
          { StoreGUID: "store-2", IsPrimary: true },
        ],
      },
      {
        method: "post",
        url: `/Users/guid/${encodedUserGuid}/roles`,
        body: { RoleGuids: ["role-1"] },
      },
      {
        method: "post",
        url: `/Users/guid/${encodedUserGuid}/permissions`,
        body: { permissions: ["Users.View"] },
      },
    ]);
    requests.forEach(({ url }) => {
      assert.equal(url.startsWith("/api"), false, `${url} 不应重复添加 /api`);
    });

    assert.throws(
      () => normalizeUserAccessStoreList({ success: false, message: "denied" }),
      /denied/,
    );
    assert.throws(
      () => normalizeAccessRoleList([{ roleName: "missing-guid" }]),
      /Invalid access management response/,
    );
    assert.throws(
      () => normalizeAccessPermissionCatalog([{ category: "Users" }]),
      /Invalid access management response/,
    );
    assert.throws(
      () => normalizeUserAccessPermissionState(null),
      /Invalid access management response/,
    );
  } finally {
    apiClient.get = originalGet;
    apiClient.post = originalPost;
  }
}

run()
  .then(() => console.log("user access management API tests passed"))
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
