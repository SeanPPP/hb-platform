import { EditOutlined, EyeOutlined, HistoryOutlined, LockOutlined, PlusOutlined, ReloadOutlined, SaveOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Button,
  Card,
  Descriptions,
  Drawer,
  Form,
  Input,
  List,
  Modal,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tabs,
  Tag,
  Transfer,
  Tree,
  Typography,
  message,
} from 'antd'
import type { TransferDirection } from 'antd/es/transfer'
import type { ColumnsType } from 'antd/es/table'
import type { DataNode } from 'antd/es/tree'
import type { Dispatch, Key, SetStateAction } from 'react'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { HasPermission } from '../../../components/Access'
import PageContainer from '../../../components/PageContainer'
import { P } from '../../../types/permissions'
import {
  assignRolesToUser,
  assignStoresToUser,
  assignPermissionsToUser,
  createUser,
  getUserByGuid,
  getUserLoginRecords,
  getUserPermissionState,
  getUserRoles,
  getUserStores,
  getUsers,
  updateUser,
  updateUserPassword,
} from '../../../services/userService'
import { getActiveRoles } from '../../../services/roleService'
import { getPermissions } from '../../../services/roleService'
import { getStores } from '../../../services/storeService'
import type { CreateUserDto, UpdateUserDto, UserDetailDto, UserDto, UserLoginRecordDto, UserPermissionStateDto, UserStoreDto } from '../../../types/user'
import type { RoleOptionDto, PermissionCategoryDto } from '../../../types/role'
import type { StoreDto } from '../../../types/store'
import { getRoleColor, getStoreColor } from '../../../utils/userTableColors'
import { useAuthStore } from '../../../store/auth'
import {
  areRoleGuidsAllowedForScopedManager,
  buildScopedStoreAssignments,
  filterRoleOptionsForScopedManager,
  filterStoresForManager,
  filterUsersVisibleToScopedManager,
  getManagedStores,
  getScopedStoreGuidsForQuery,
  hasForbiddenRoleForScopedManager,
  isScopedStoreManager,
  isStoreVisibleToManager,
  mergeUsersByGuid,
} from './userScope'
import {
  arePermissionSetsEqual,
  buildDirectPermissionPayload,
  buildPermissionSourceMap,
  deriveDirectPermissionKeysFromChecked,
  uniquePermissionCodes,
} from './userPermissions'
import { formatUserLocalDateTime } from './time'

export default function SystemUsersPage() {
  const { t } = useTranslation()
  const currentUser = useAuthStore((state) => state.currentUser)
  const access = useAuthStore((state) => state.access)
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<UserDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)

  const [selectedStoreGuid, setSelectedStoreGuid] = useState<string | undefined>(undefined)
  const [selectedRoleGuid, setSelectedRoleGuid] = useState<string | undefined>(undefined)

  const [sortBy, setSortBy] = useState<string | undefined>(undefined)
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend' | null>(null)

  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [roleOptions, setRoleOptions] = useState<{ label: string; value: string; roleName: string }[]>([])

  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailUser, setDetailUser] = useState<UserDetailDto | null>(null)
  const [detailStores, setDetailStores] = useState<UserStoreDto[]>([])

  const [loginRecordsOpen, setLoginRecordsOpen] = useState(false)
  const [loginRecordsLoading, setLoginRecordsLoading] = useState(false)
  const [loginRecordsUser, setLoginRecordsUser] = useState<UserDto | null>(null)
  const [loginRecords, setLoginRecords] = useState<UserLoginRecordDto[]>([])
  const [loginRecordsPage, setLoginRecordsPage] = useState(1)
  const [loginRecordsPageSize, setLoginRecordsPageSize] = useState(10)
  const [loginRecordsTotal, setLoginRecordsTotal] = useState(0)

  const [editOpen, setEditOpen] = useState(false)
  const [editLoading, setEditLoading] = useState(false)
  const [editingUser, setEditingUser] = useState<UserDetailDto | null>(null)
  const [editTab, setEditTab] = useState('info')
  const [form] = Form.useForm<UpdateUserDto>()

  const [allRoles, setAllRoles] = useState<RoleOptionDto[]>([])
  const [roleTargetKeys, setRoleTargetKeys] = useState<string[]>([])
  const [roleLoading, setRoleLoading] = useState(false)
  const [roleSaving, setRoleSaving] = useState(false)

  const [allStores, setAllStores] = useState<StoreDto[]>([])
  const [storeTargetKeys, setStoreTargetKeys] = useState<string[]>([])
  const [storeManageableKeys, setStoreManageableKeys] = useState<string[]>([])
  const [storeLoading, setStoreLoading] = useState(false)
  const [storeSaving, setStoreSaving] = useState(false)

  const [permCategories, setPermCategories] = useState<PermissionCategoryDto[]>([])
  const [permissionState, setPermissionState] = useState<UserPermissionStateDto | null>(null)
  const [directPermKeys, setDirectPermKeys] = useState<string[]>([])
  const [originalDirectPermKeys, setOriginalDirectPermKeys] = useState<string[]>([])
  const [permLoading, setPermLoading] = useState(false)
  const [permSaving, setPermSaving] = useState(false)

  const [resetPwdLoading, setResetPwdLoading] = useState(false)
  const [resetPwdOpen, setResetPwdOpen] = useState(false)
  const [resetPwdForm] = Form.useForm<{ newPassword: string }>()

  const [createOpen, setCreateOpen] = useState(false)
  const [createLoading, setCreateLoading] = useState(false)
  const [createTab, setCreateTab] = useState('info')
  const [createForm] = Form.useForm<CreateUserDto & { confirmPassword: string }>()

  const [createRoleTargetKeys, setCreateRoleTargetKeys] = useState<string[]>([])
  const [createRoleLoading, setCreateRoleLoading] = useState(false)

  const [createStoreTargetKeys, setCreateStoreTargetKeys] = useState<string[]>([])
  const [createStoreManageableKeys, setCreateStoreManageableKeys] = useState<string[]>([])
  const [createStoreLoading, setCreateStoreLoading] = useState(false)

  const sortedStores = useMemo(
    () => [...allStores].sort((a, b) => a.storeName.localeCompare(b.storeName)),
    [allStores],
  )

  const isCurrentUserScoped = isScopedStoreManager(currentUser, access)
  const managedStores = useMemo(() => getManagedStores(currentUser, access), [access, currentUser])
  const managedStoreKey = managedStores.map((store) => store.storeGUID).join('|')
  const canLoadRoleOptions = access.canReadRole || access.hasPermission(P.Users.ManageRoles)
  const canManageUserPermissions = access.hasPermission(P.Users.ManageRoles)
  const canEditUserPermissions = canManageUserPermissions

  const visibleStoreOptions = useMemo(() => {
    if (isCurrentUserScoped) {
      return managedStores.map((store) => ({
        label: `${store.storeName} (${store.storeCode})`,
        value: store.storeGUID,
      }))
    }

    return storeOptions
  }, [isCurrentUserScoped, managedStores, storeOptions])

  const managedStoreDetails = useMemo<StoreDto[]>(
    () => managedStores.map((store) => ({
      storeGUID: store.storeGUID,
      storeName: store.storeName,
      storeCode: store.storeCode,
      isActive: true,
      createdAt: '',
      updatedAt: '',
    })),
    [managedStores],
  )

  const sortStoreGuidsFromStores = (keys: string[], stores: StoreDto[]) =>
    [...keys].sort((a, b) => {
      const storeA = stores.find((item) => item.storeGUID === a)
      const storeB = stores.find((item) => item.storeGUID === b)
      if (!storeA || !storeB) return 0
      return storeA.storeName.localeCompare(storeB.storeName)
    })

  const sortStoreGuids = (keys: string[]) => sortStoreGuidsFromStores(keys, sortedStores)

  const sortScopedUsers = (
    users: UserDto[],
    currentSortBy?: string,
    currentSortOrder?: 'ascend' | 'descend' | null,
  ) => {
    if (!currentSortBy || !currentSortOrder) {
      return users
    }

    const direction = currentSortOrder === 'ascend' ? 1 : -1
    return [...users].sort((left, right) => {
      const leftValue = currentSortBy === 'roleNames' || currentSortBy === 'storeNames'
        ? (left[currentSortBy] ?? []).join(',')
        : String(left[currentSortBy as keyof UserDto] ?? '')
      const rightValue = currentSortBy === 'roleNames' || currentSortBy === 'storeNames'
        ? (right[currentSortBy] ?? []).join(',')
        : String(right[currentSortBy as keyof UserDto] ?? '')
      return leftValue.localeCompare(rightValue) * direction
    })
  }

  const handleStoreTargetChange = (nextTargetKeys: Key[]) => {
    const next = sortStoreGuids(nextTargetKeys.map(String))
    setStoreTargetKeys(next)
    setStoreManageableKeys((current) => current.filter((storeGUID) => next.includes(storeGUID)))
  }

  const handleCreateStoreTargetChange = (nextTargetKeys: Key[]) => {
    const next = sortStoreGuids(nextTargetKeys.map(String))
    setCreateStoreTargetKeys(next)
    setCreateStoreManageableKeys((current) => current.filter((storeGUID) => next.includes(storeGUID)))
  }

  const toggleStoreManageable = (
    storeGUID: string,
    checked: boolean,
    setter: Dispatch<SetStateAction<string[]>>,
  ) => {
    setter((current) => {
      const next = new Set(current)
      if (checked) {
        next.add(storeGUID)
      } else {
        next.delete(storeGUID)
      }
      return sortStoreGuids(Array.from(next))
    })
  }

  const renderManageableStoreControls = (
    targetKeys: string[],
    manageableKeys: string[],
    setter: Dispatch<SetStateAction<string[]>>,
  ) => {
    const selectedStores = targetKeys
      .map((storeGUID) => sortedStores.find((item) => item.storeGUID === storeGUID))
      .filter((item): item is StoreDto => Boolean(item))

    if (!selectedStores.length) {
      return null
    }

    return (
      <List
        size="small"
        style={{ marginTop: 12 }}
        dataSource={selectedStores}
        renderItem={(store) => (
          <List.Item
            actions={[
              <Switch
                key="manageable"
                checked={manageableKeys.includes(store.storeGUID)}
                onChange={(checked) => toggleStoreManageable(store.storeGUID, checked, setter)}
              />,
            ]}
          >
            <Space>
              <Typography.Text>{`${store.storeName} (${store.storeCode})`}</Typography.Text>
              {manageableKeys.includes(store.storeGUID) ? (
                <Tag color="processing">{t('system.users.manageableStore', '可管理')}</Tag>
              ) : (
                <Tag>{t('system.users.linkedOnlyStore', '普通关联')}</Tag>
              )}
            </Space>
          </List.Item>
        )}
      />
    )
  }

  const loadData = async (nextPage = page, nextPageSize = pageSize, currentSortBy?: string, currentSortOrder?: 'ascend' | 'descend' | null) => {
    setLoading(true)
    try {
      if (isCurrentUserScoped) {
        const scopedStoreGuids = getScopedStoreGuidsForQuery(selectedStoreGuid, managedStores)

        if (!scopedStoreGuids.length) {
          setData([])
          setTotal(0)
          setPage(nextPage)
          setPageSize(nextPageSize)
          return
        }

        const queryBase = {
          search: keyword || undefined,
          roleGuid: selectedRoleGuid,
          sortBy: currentSortBy || undefined,
          sortDirection: currentSortOrder === 'ascend' ? 'asc' : currentSortOrder === 'descend' ? 'desc' : undefined,
        }
        const scopedStorePageSize = 500
        const loadAllUsersForStore = async (storeGuid: string) => {
          const firstPage = await getUsers({
            page: 1,
            pageSize: scopedStorePageSize,
            storeGuid,
            ...queryBase,
          })
          const pageCount = Math.ceil(firstPage.total / (firstPage.pageSize || scopedStorePageSize))
          if (pageCount <= 1) {
            return firstPage.items
          }

          const restPages = await Promise.all(
            Array.from({ length: pageCount - 1 }, (_, index) =>
              getUsers({
                page: index + 2,
                pageSize: scopedStorePageSize,
                storeGuid,
                ...queryBase,
              }),
            ),
          )
          return [firstPage, ...restPages].flatMap((result) => result.items)
        }

        const results = await Promise.all(
          scopedStoreGuids.map((storeGuid) => loadAllUsersForStore(storeGuid)),
        )
        const mergedUsers = sortScopedUsers(
          filterUsersVisibleToScopedManager(mergeUsersByGuid(results.flat())),
          currentSortBy,
          currentSortOrder,
        )
        const startIndex = (nextPage - 1) * nextPageSize
        setData(mergedUsers.slice(startIndex, startIndex + nextPageSize))
        setTotal(mergedUsers.length)
        setPage(nextPage)
        setPageSize(nextPageSize)
        return
      }

      const result = await getUsers({
        page: nextPage,
        pageSize: nextPageSize,
        search: keyword || undefined,
        storeGuid: selectedStoreGuid,
        roleGuid: selectedRoleGuid,
        sortBy: currentSortBy || undefined,
        sortDirection: currentSortOrder === 'ascend' ? 'asc' : currentSortOrder === 'descend' ? 'desc' : undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadListFailed', '加载用户列表失败'))
    } finally {
      setLoading(false)
    }
  }

  const loadLoginRecords = async (
    user: UserDto,
    nextPage = loginRecordsPage,
    nextPageSize = loginRecordsPageSize,
  ) => {
    setLoginRecordsLoading(true)
    try {
      const result = await getUserLoginRecords(user.userGUID, {
        page: nextPage,
        pageSize: nextPageSize,
      })
      setLoginRecords(result.items)
      setLoginRecordsTotal(result.total)
      setLoginRecordsPage(result.page)
      setLoginRecordsPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadLoginRecordsFailed', '加载登录记录失败'))
    } finally {
      setLoginRecordsLoading(false)
    }
  }

  const handleOpenLoginRecords = async (record: UserDto) => {
    setLoginRecordsUser(record)
    setLoginRecordsOpen(true)
    setLoginRecords([])
    setLoginRecordsTotal(0)
    setLoginRecordsPage(1)
    setLoginRecordsPageSize(10)
    await loadLoginRecords(record, 1, 10)
  }

  useEffect(() => {
    if (!currentUser) {
      return
    }

    void loadData(1, pageSize, undefined, undefined)
  }, [currentUser?.userGUID, isCurrentUserScoped, managedStoreKey])

  useEffect(() => {
    void (async () => {
      if (isCurrentUserScoped) {
        setStoreOptions(managedStores.map((store) => ({
          label: `${store.storeName} (${store.storeCode})`,
          value: store.storeGUID,
        })))
      } else {
        try {
          const storeResult = await getStores({ page: 1, pageSize: 200, sortField: 'storeName', sortOrder: 'asc' })
          setStoreOptions(storeResult.items.map((s) => ({
            label: `${s.storeName} (${s.storeCode})`,
            value: s.storeGUID,
          })))
        } catch (error) {
          console.error(error)
          setStoreOptions([])
        }
      }

      if (canLoadRoleOptions) {
        try {
          const roles = await getActiveRoles()
          const nextRoles = isCurrentUserScoped ? filterRoleOptionsForScopedManager(roles) : roles
          setRoleOptions(nextRoles.map((r) => ({ label: r.roleName, value: r.roleGUID, roleName: r.roleName })))
        } catch (error) {
          console.error(error)
          setRoleOptions([])
        }
      } else {
        setRoleOptions([])
      }
    })()
  }, [canLoadRoleOptions, isCurrentUserScoped, managedStoreKey])

  useEffect(() => {
    if (isCurrentUserScoped) {
      if (selectedStoreGuid && !isStoreVisibleToManager(selectedStoreGuid, managedStores)) {
        setSelectedStoreGuid(undefined)
      }
      return
    }

    if (selectedStoreGuid && !visibleStoreOptions.some((option) => option.value === selectedStoreGuid)) {
      setSelectedStoreGuid(undefined)
    }
  }, [isCurrentUserScoped, managedStoreKey, managedStores, selectedStoreGuid, visibleStoreOptions])

  useEffect(() => {
    if (selectedRoleGuid && !roleOptions.some((option) => option.value === selectedRoleGuid)) {
      setSelectedRoleGuid(undefined)
    }
  }, [roleOptions, selectedRoleGuid])

  const reloadUserDetail = async (userGuid: string) => {
    const [detail, stores] = await Promise.all([getUserByGuid(userGuid), getUserStores(userGuid).catch(() => [])])
    setDetailUser(detail)
    setDetailStores(isCurrentUserScoped ? filterStoresForManager(stores, managedStores) : stores)
    return detail
  }

  const handleViewDetail = async (record: UserDto) => {
    if (isCurrentUserScoped && (!data.some((item) => item.userGUID === record.userGUID) || hasForbiddenRoleForScopedManager(record))) {
      message.error(t('system.users.detailOutOfScope', '无权查看该用户详情'))
      return
    }

    setDetailOpen(true)
    setDetailLoading(true)
    setDetailUser(null)
    setDetailStores([])
    try {
      const [detail, stores] = await Promise.all([
        getUserByGuid(record.userGUID),
        getUserStores(record.userGUID).catch(() => []),
      ])
      if (isCurrentUserScoped && hasForbiddenRoleForScopedManager(detail)) {
        message.error(t('system.users.detailOutOfScope', '无权查看该用户详情'))
        setDetailOpen(false)
        return
      }
      setDetailUser(detail)
      setDetailStores(isCurrentUserScoped ? filterStoresForManager(stores, managedStores) : stores)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadDetailFailed', '加载用户详情失败'))
      setDetailOpen(false)
    } finally {
      setDetailLoading(false)
    }
  }

  const loadRoleData = async (userGuid: string) => {
    setRoleLoading(true)
    try {
      if (!canLoadRoleOptions) {
        setAllRoles([])
        setRoleTargetKeys([])
        return
      }

      const [roles, userRoles] = await Promise.all([getActiveRoles(), getUserRoles(userGuid)])
      setAllRoles(isCurrentUserScoped ? filterRoleOptionsForScopedManager(roles) : roles)
      setRoleTargetKeys(userRoles.map((item) => item.roleGUID))
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadRolesFailed', '加载角色数据失败'))
    } finally {
      setRoleLoading(false)
    }
  }

  const loadStoreData = async (userGuid: string) => {
    setStoreLoading(true)
    try {
      if (isCurrentUserScoped) {
        const userStores = await getUserStores(userGuid)
        const scopedUserStores = filterStoresForManager(userStores, managedStores)
        setAllStores(managedStoreDetails)
        setStoreTargetKeys(sortStoreGuidsFromStores(scopedUserStores.map((item) => item.storeGUID), managedStoreDetails))
        setStoreManageableKeys(sortStoreGuidsFromStores(
          scopedUserStores.filter((item) => item.isManageable).map((item) => item.storeGUID),
          managedStoreDetails,
        ))
        return
      }

      const [stores, userStores] = await Promise.all([
        getStores({ page: 1, pageSize: 200, sortField: 'storeName', sortOrder: 'asc' }),
        getUserStores(userGuid),
      ])
      setAllStores(stores.items)
      setStoreTargetKeys(sortStoreGuids(userStores.map((item) => item.storeGUID)))
      setStoreManageableKeys(sortStoreGuids(userStores.filter((item) => item.isManageable).map((item) => item.storeGUID)))
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadStoresFailed', '加载分店数据失败'))
    } finally {
      setStoreLoading(false)
    }
  }

  const loadPermData = async (userGuid: string) => {
    setPermLoading(true)
    try {
      const [categories, nextPermissionState] = await Promise.all([
        getPermissions(),
        getUserPermissionState(userGuid),
      ])
      setPermCategories(categories)
      setPermissionState(nextPermissionState)
      setDirectPermKeys(nextPermissionState.directPermissionCodes)
      setOriginalDirectPermKeys(nextPermissionState.directPermissionCodes)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadPermsFailed', '加载权限数据失败'))
    } finally {
      setPermLoading(false)
    }
  }

  const handleEdit = async (record: UserDto) => {
    if (isCurrentUserScoped && (!data.some((item) => item.userGUID === record.userGUID) || hasForbiddenRoleForScopedManager(record))) {
      message.error(t('system.users.editOutOfScope', '无权编辑该用户'))
      return
    }

    setEditOpen(true)
    setEditLoading(true)
    setEditingUser(null)
    setEditTab('info')
    setPermissionState(null)
    setDirectPermKeys([])
    setOriginalDirectPermKeys([])
    form.resetFields()
    try {
      const detail = await getUserByGuid(record.userGUID)
      if (isCurrentUserScoped && hasForbiddenRoleForScopedManager(detail)) {
        message.error(t('system.users.editOutOfScope', '无权编辑该用户'))
        setEditOpen(false)
        return
      }
      setEditingUser(detail)
      form.setFieldsValue({
        username: detail.username,
        email: detail.email,
        fullName: detail.fullName,
        isActive: detail.isActive,
      })
      void loadRoleData(record.userGUID)
      void loadStoreData(record.userGUID)
      void loadPermData(record.userGUID)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.loadEditFailed', '加载用户编辑数据失败'))
      setEditOpen(false)
    } finally {
      setEditLoading(false)
    }
  }

  const handleEditSubmit = async () => {
    if (!editingUser) return
    try {
      const values = await form.validateFields()
      setEditLoading(true)
      const updated = await updateUser(editingUser.userGUID, values)
      message.success(t('system.users.updateSuccess', '用户信息已更新'))
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) {
        setDetailUser(updated)
      }
      void loadData(page, pageSize, sortBy, sortOrder)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.users.updateFailed', '更新用户失败'))
    } finally {
      setEditLoading(false)
    }
  }

  const handleSaveRoles = async () => {
    if (!editingUser) return
    if (isCurrentUserScoped && !areRoleGuidsAllowedForScopedManager(roleTargetKeys, allRoles)) {
      message.error(t('system.users.roleAssignForbidden', '店长不能分配管理员、店长或仓库经理角色'))
      return
    }
    setRoleSaving(true)
    try {
      await assignRolesToUser(editingUser.userGUID, { roleGuids: roleTargetKeys })
      message.success(t('system.users.roleAssignSuccess', '角色分配成功'))
      void loadData(page, pageSize, sortBy, sortOrder)
      const updated = await getUserByGuid(editingUser.userGUID)
      void loadPermData(editingUser.userGUID)
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) setDetailUser(updated)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.roleAssignFailed', '角色分配失败'))
    } finally {
      setRoleSaving(false)
    }
  }

  const handleSavePermissions = async () => {
    if (!editingUser || !canEditUserPermissions) return
    setPermSaving(true)
    try {
      const permissions = buildDirectPermissionPayload(directPermKeys)
      await assignPermissionsToUser(editingUser.userGUID, { permissions })
      message.success(t('system.users.permissionAssignSuccess', '用户直接权限已保存'))
      await loadPermData(editingUser.userGUID)
      void loadData(page, pageSize, sortBy, sortOrder)
      const updated = await getUserByGuid(editingUser.userGUID)
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) setDetailUser(updated)
    } catch (error) {
      console.error(error)
      message.error(t('system.users.permissionAssignFailed', '用户直接权限保存失败'))
    } finally {
      setPermSaving(false)
    }
  }

  const handleSaveStores = async () => {
    if (!editingUser) return
    setStoreSaving(true)
    try {
      const storeAssignments = isCurrentUserScoped
        ? buildScopedStoreAssignments(
          await getUserStores(editingUser.userGUID),
          storeTargetKeys,
          storeManageableKeys,
          managedStores,
        )
        : storeTargetKeys.map((storeGUID) => ({
          storeGUID,
          accessLevel: 'ReadWrite',
          isManageable: storeManageableKeys.includes(storeGUID),
        }))

      await assignStoresToUser(
        editingUser.userGUID,
        storeAssignments,
      )
      message.success(t('system.users.storeAssignSuccess', '分店分配成功'))
      void loadData(page, pageSize, sortBy, sortOrder)
      const updated = await getUserByGuid(editingUser.userGUID)
      setEditingUser(updated)
      if (detailUser?.userGUID === updated.userGUID) {
        void reloadUserDetail(updated.userGUID)
      }
    } catch (error) {
      console.error(error)
      message.error(t('system.users.storeAssignFailed', '分店分配失败'))
    } finally {
      setStoreSaving(false)
    }
  }

  const handleResetPassword = async () => {
    if (!editingUser) return
    try {
      const values = await resetPwdForm.validateFields()
      setResetPwdLoading(true)
      await updateUserPassword(editingUser.userGUID, {
        newPassword: values.newPassword,
        passwordFormat: 'raw',
      })
      message.success(t('system.users.resetPasswordSuccess', '密码重置成功'))
      setResetPwdOpen(false)
      resetPwdForm.resetFields()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.users.resetPasswordFailed', '密码重置失败'))
    } finally {
      setResetPwdLoading(false)
    }
  }

  const handleOpenCreate = async () => {
    setCreateOpen(true)
    setCreateTab('info')
    createForm.resetFields()
    setCreateRoleTargetKeys([])
    setCreateStoreTargetKeys([])
    setCreateStoreManageableKeys([])
    setCreateRoleLoading(true)
    setCreateStoreLoading(true)
    try {
      const [roles, stores] = await Promise.all([
        canLoadRoleOptions ? getActiveRoles() : Promise.resolve([]),
        isCurrentUserScoped
          ? Promise.resolve(managedStoreDetails)
          : getStores({ page: 1, pageSize: 200, sortField: 'storeName', sortOrder: 'asc' }).then((result) => result.items),
      ])
      setAllRoles(isCurrentUserScoped ? filterRoleOptionsForScopedManager(roles) : roles)
      setAllStores(stores)
    } catch (error) {
      console.error(error)
    } finally {
      setCreateRoleLoading(false)
      setCreateStoreLoading(false)
    }
  }

  const handleCreateSubmit = async () => {
    try {
      const values = await createForm.validateFields()
      if (isCurrentUserScoped && !areRoleGuidsAllowedForScopedManager(createRoleTargetKeys, allRoles)) {
        message.error(t('system.users.roleAssignForbidden', '店长不能分配管理员、店长或仓库经理角色'))
        return
      }
      setCreateLoading(true)
      const payload: CreateUserDto = {
        username: values.username,
        email: values.email,
        password: values.password,
        passwordFormat: 'raw',
        fullName: values.fullName,
        isActive: values.isActive ?? true,
        roleGuids: createRoleTargetKeys,
        storeGuids: createStoreTargetKeys,
      }
      const created = await createUser(payload)
      if (createStoreTargetKeys.length > 0) {
        await assignStoresToUser(
          created.userGUID,
          createStoreTargetKeys.map((storeGUID) => ({
            storeGUID,
            accessLevel: 'ReadWrite',
            isManageable: createStoreManageableKeys.includes(storeGUID),
          })),
        )
      }
      message.success(t('system.users.createUserSuccess', '用户创建成功'))
      setCreateOpen(false)
      createForm.resetFields()
      setCreateRoleTargetKeys([])
      setCreateStoreTargetKeys([])
      setCreateStoreManageableKeys([])
      void loadData(1, pageSize, sortBy, sortOrder)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('system.users.createUserFailed', '用户创建失败'))
    } finally {
      setCreateLoading(false)
    }
  }

  const inheritedPermSet = useMemo(() => {
    return new Set(permissionState?.inheritedPermissionCodes ?? [])
  }, [permissionState])

  const allPermissionCodes = useMemo(() => {
    return new Set(permCategories.flatMap((cat) => cat.permissions.map((permission) => permission.name)))
  }, [permCategories])

  const directPermSet = useMemo(() => {
    return new Set(directPermKeys)
  }, [directPermKeys])

  const effectivePermSet = useMemo(() => {
    return new Set(uniquePermissionCodes([
      ...(permissionState?.inheritedPermissionCodes ?? []),
      ...directPermKeys,
    ]))
  }, [directPermKeys, permissionState])

  const permissionSourceMap = useMemo(() => {
    return buildPermissionSourceMap(permissionState?.inheritedSources ?? [])
  }, [permissionState])

  const hasDirectPermChanges = useMemo(() => {
    return !arePermissionSetsEqual(directPermKeys, originalDirectPermKeys)
  }, [directPermKeys, originalDirectPermKeys])

  const permTreeData = useMemo<DataNode[]>(() => {
    return permCategories.map((cat) => ({
      key: `category:${cat.category}`,
      title: <strong>{cat.displayName}</strong>,
      children: cat.permissions.map((p) => ({
        key: p.name,
        title: (
          <Space size={4}>
            <span>{p.displayName}</span>
            {(permissionSourceMap[p.name] ?? []).map((roleName) => (
              <Tag key={roleName} color={getRoleColor(roleName)} style={{ fontSize: 11, lineHeight: '18px', padding: '0 4px' }}>
                {roleName}
              </Tag>
            ))}
            {directPermSet.has(p.name) ? (
              <Tag color="blue" style={{ fontSize: 11, lineHeight: '18px', padding: '0 4px' }}>
                {t('system.users.directPermissionTag', '直接授权')}
              </Tag>
            ) : null}
          </Space>
        ),
      })),
    }))
  }, [directPermSet, permCategories, permissionSourceMap, t])

  const checkedPermKeys = useMemo(() => {
    return Array.from(effectivePermSet)
  }, [effectivePermSet])

  const handlePermissionCheck = (
    nextCheckedKeys: Key[] | { checked: Key[]; halfChecked: Key[] },
    info: { checked: boolean; node: { key: Key } },
  ) => {
    if (!canEditUserPermissions) return

    const changedKey = String(info.node.key)
    const checkedKeys = Array.isArray(nextCheckedKeys)
      ? nextCheckedKeys
      : nextCheckedKeys.checked
    const checkedPermissionKeys = checkedKeys.map(String)

    if (!info.checked && inheritedPermSet.has(changedKey)) {
      if (directPermSet.has(changedKey)) {
        message.info(t('system.users.directPermissionRemovedInheritedKept', '已移除直接授权，该权限仍由角色继承保留'))
      } else {
        message.info(t('system.users.inheritedPermissionReadonly', '该权限来自角色，请到角色权限管理调整'))
      }
    }

    setDirectPermKeys(
      deriveDirectPermissionKeysFromChecked({
        checkedPermissionKeys,
        allPermissionCodes: Array.from(allPermissionCodes),
        inheritedPermissionCodes: Array.from(inheritedPermSet),
        currentDirectPermissionCodes: directPermKeys,
      }),
    )
  }

  const renderLoginRecordStatus = (record: UserLoginRecordDto) => {
    if (record.status === 'revoked' || record.isRevoked) {
      return <Tag color="warning">{t('system.users.loginRecordRevoked', '已撤销')}</Tag>
    }
    if (record.status === 'expired' || record.isExpired) {
      return <Tag>{t('system.users.loginRecordExpired', '已过期')}</Tag>
    }
    return <Tag color="success">{t('system.users.loginRecordActive', '有效')}</Tag>
  }

  const loginRecordColumns: ColumnsType<UserLoginRecordDto> = [
    {
      title: t('system.users.loginAt', '登录时间'),
      dataIndex: 'loginAt',
      width: 180,
      render: (value: string) => formatUserLocalDateTime(value, t('common.emptyValue')),
    },
    {
      title: t('system.users.loginIp', '登录 IP'),
      dataIndex: 'ipAddress',
      width: 150,
      render: (value: string | undefined) => value || t('common.emptyValue'),
    },
    {
      title: t('common.status', '状态'),
      dataIndex: 'status',
      width: 110,
      render: (_value: string, record) => renderLoginRecordStatus(record),
    },
    {
      title: t('system.users.expiresAt', '过期时间'),
      dataIndex: 'expiresAt',
      width: 180,
      render: (value: string) => formatUserLocalDateTime(value, t('common.emptyValue')),
    },
    {
      title: 'UserAgent',
      dataIndex: 'userAgent',
      ellipsis: true,
      render: (value: string | undefined) => value || t('common.emptyValue'),
    },
  ]

  const columns: ColumnsType<UserDto> = [
    {
      title: t('system.users.rowIndex', '#'),
      key: 'rowIndex',
      width: 60,
      align: 'center',
      render: (_: unknown, __: UserDto, index: number) => (page - 1) * pageSize + index + 1,
    },
    { title: t('system.users.username', '用户名'), dataIndex: 'username', width: 180, sorter: true, sortOrder: sortBy === 'username' ? sortOrder : null },
    { title: t('system.users.fullName', '姓名'), dataIndex: 'fullName', width: 160, sorter: true, sortOrder: sortBy === 'fullName' ? sortOrder : null, render: (value) => value || t('common.emptyValue') },
    { title: t('system.users.email', '邮箱'), dataIndex: 'email', width: 220 },
    {
      title: t('system.users.roles', '角色'),
      dataIndex: 'roleNames',
      width: 220,
      sorter: true,
      sortOrder: sortBy === 'roleNames' ? sortOrder : null,
      render: (value: string[]) =>
        value?.length ? value.map((item) => <Tag key={item} color={getRoleColor(item)}>{item}</Tag>) : t('common.emptyValue'),
    },
    {
      title: t('system.users.linkedStores', '关联分店'),
      dataIndex: 'storeNames',
      width: 240,
      sorter: true,
      sortOrder: sortBy === 'storeNames' ? sortOrder : null,
      render: (value: string[]) => {
        const stores = [...(value || [])].sort((left, right) => left.localeCompare(right))
        if (!stores.length) return t('common.emptyValue')
        return (
          <Space wrap size={[4, 4]}>
            {stores.slice(0, 2).map((store) => (
              <Tag key={store} color={getStoreColor(store)}>{store}</Tag>
            ))}
            {stores.length > 2 ? <Tag>+{stores.length - 2}</Tag> : null}
          </Space>
        )
      },
    },
    {
      title: t('common.status', '状态'),
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.active', '启用') : t('common.inactive', '停用')}</Tag>
      ),
    },
    {
      title: t('system.users.lastLogin', '最近登录'),
      dataIndex: 'lastLoginAt',
      width: 190,
      sorter: true,
      sortOrder: sortBy === 'lastLoginAt' ? sortOrder : null,
      render: (_value: string | undefined, record) => (
        <Space direction="vertical" size={0}>
          <Typography.Text>{formatUserLocalDateTime(record.lastLoginAt, t('common.emptyValue'))}</Typography.Text>
          {record.lastLoginIp ? (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {record.lastLoginIp}
            </Typography.Text>
          ) : null}
        </Space>
      ),
    },
    {
      title: t('common.action', '操作'),
      key: 'action',
      width: 260,
      fixed: 'right',
      render: (_, record) => (
        <Space size={0}>
          <Button type="link" icon={<EyeOutlined />} onClick={() => void handleViewDetail(record)}>
            {t('common.view', '详情')}
          </Button>
          <Button type="link" icon={<HistoryOutlined />} onClick={() => void handleOpenLoginRecords(record)}>
            {t('system.users.loginRecords', '登录记录')}
          </Button>
          <HasPermission code={P.Users.Edit}>
            <Button type="link" icon={<EditOutlined />} onClick={() => void handleEdit(record)}>
              {t('common.edit', '编辑')}
            </Button>
          </HasPermission>
        </Space>
      ),
    },
  ]

  const editTabItems = [
    {
      key: 'info',
      label: t('system.users.basicInfo', '基本信息'),
      children: (
        <Spin spinning={editLoading}>
          <Form form={form} layout="vertical" style={{ maxWidth: 480 }}>
            <Form.Item label={t('system.users.username', '用户名')} name="username" rules={[{ required: true, message: t('system.users.usernameRequired', '请输入用户名') }]}>
              <Input />
            </Form.Item>
            <Form.Item
              label={t('system.users.email', '邮箱')}
              name="email"
              rules={[
                { required: true, message: t('system.users.emailRequired', '请输入邮箱') },
                { type: 'email', message: t('system.users.emailInvalid', '邮箱格式不正确') },
              ]}
            >
              <Input />
            </Form.Item>
            <Form.Item label={t('system.users.fullName', '姓名')} name="fullName">
              <Input />
            </Form.Item>
            <Form.Item label={t('common.status', '状态')} name="isActive" valuePropName="checked">
              <Switch checkedChildren={t('common.active', '启用')} unCheckedChildren={t('common.inactive', '停用')} />
            </Form.Item>
            <Form.Item>
              <Button type="primary" loading={editLoading} onClick={() => void handleEditSubmit()}>
                {t('system.users.saveBasicInfo', '保存基本信息')}
              </Button>
            </Form.Item>
            <HasPermission code={P.Users.ResetPassword}>
              <div style={{ paddingTop: 16, borderTop: '1px solid #f0f0f0' }}>
                <Button icon={<LockOutlined />} onClick={() => setResetPwdOpen(true)} danger>
                    {t('system.users.resetPassword', '重置密码')}
                  </Button>
              </div>
            </HasPermission>
          </Form>
        </Spin>
      ),
    },
    {
      key: 'roles',
      label: (
        <HasPermission code={P.Users.ManageRoles} fallback={<span>{t('system.users.roles', '角色')}</span>}>
          <span>{t('system.users.roles', '角色')}</span>
        </HasPermission>
      ),
      children: (
        <HasPermission code={P.Users.ManageRoles} fallback={<Typography.Text type="secondary">{t('system.users.noRolePermission', '无权限管理角色')}</Typography.Text>}>
          <Spin spinning={roleLoading}>
            <div style={{ marginBottom: 12 }}>
              <Typography.Text type="secondary">
                {t('system.users.assignedRoles', '当前用户已分配 {{count}} 个角色', { count: roleTargetKeys.length })}
              </Typography.Text>
            </div>
            <Transfer
              dataSource={allRoles.map((role) => ({
                key: role.roleGUID,
                title: role.roleName,
                description: role.description || '',
              }))}
              targetKeys={roleTargetKeys}
              onChange={(nextTargetKeys: Key[], _direction: TransferDirection, _moveKeys: Key[]) => {
                setRoleTargetKeys(nextTargetKeys.map(String))
              }}
              render={(item) => item.title}
              titles={[t('system.users.availableRoles', '可选角色'), t('system.users.assignedRolesLabel', '已分配角色')]}
              listStyle={{ width: 320, height: 400 }}
              showSearch
            />
            <div style={{ marginTop: 16, textAlign: 'right' }}>
              <Button type="primary" loading={roleSaving} onClick={() => void handleSaveRoles()}>
                {t('system.users.saveRoleAssign', '保存角色分配')}
              </Button>
            </div>
          </Spin>
        </HasPermission>
      ),
    },
    {
      key: 'stores',
      label: (
        <HasPermission code={P.Users.ManageStores} fallback={<span>{t('system.users.stores', '分店')}</span>}>
          <span>{t('system.users.stores', '分店')}</span>
        </HasPermission>
      ),
      children: (
        <HasPermission code={P.Users.ManageStores} fallback={<Typography.Text type="secondary">{t('system.users.noStorePermission', '无权限管理分店')}</Typography.Text>}>
          <Spin spinning={storeLoading}>
            <div style={{ marginBottom: 12 }}>
              <Typography.Text type="secondary">
                {t('system.users.assignedStores', '当前用户已关联 {{count}} 个分店', { count: storeTargetKeys.length })}
              </Typography.Text>
            </div>
            <Transfer
              dataSource={sortedStores.map((store) => ({
                key: store.storeGUID,
                title: `${store.storeName} (${store.storeCode})`,
                description: store.address || '',
              }))}
              targetKeys={storeTargetKeys}
              onChange={(nextTargetKeys: Key[], _direction: TransferDirection, _moveKeys: Key[]) => {
                handleStoreTargetChange(nextTargetKeys)
              }}
              render={(item) => item.title}
              titles={[t('system.users.availableStores', '可选分店'), t('system.users.assignedStoresLabel', '已分配分店')]}
              listStyle={{ width: 320, height: 400 }}
              showSearch
            />
            {renderManageableStoreControls(storeTargetKeys, storeManageableKeys, setStoreManageableKeys)}
            <div style={{ marginTop: 16, textAlign: 'right' }}>
              <Button type="primary" loading={storeSaving} onClick={() => void handleSaveStores()}>
                {t('system.users.saveStoreAssign', '保存分店分配')}
              </Button>
            </div>
          </Spin>
        </HasPermission>
      ),
    },
    {
      key: 'permissions',
      label: t('system.users.permissions', '权限'),
      children: (
        <Spin spinning={permLoading}>
          <div style={{ marginBottom: 12 }}>
            <Typography.Text type="secondary">
              {t(
                'system.users.permEditDesc',
                '当前有效权限共 {{effectiveCount}} 项，其中角色继承 {{inheritedCount}} 项，直接授权 {{directCount}} 项。角色标签表示继承来源。',
                {
                  effectiveCount: effectivePermSet.size,
                  inheritedCount: inheritedPermSet.size,
                  directCount: directPermKeys.length,
                },
              )}
            </Typography.Text>
            {!canManageUserPermissions ? (
              <Typography.Text type="secondary" style={{ display: 'block', marginTop: 4 }}>
                {t('system.users.noPermissionManagePermission', '无权限编辑用户直接权限')}
              </Typography.Text>
            ) : null}
          </div>
          {permCategories.length === 0 ? (
            <Typography.Text type="secondary">{t('system.users.noPermData', '暂无权限数据')}</Typography.Text>
          ) : (
            <>
              <Tree
                treeData={permTreeData}
                checkedKeys={checkedPermKeys}
                checkable
                disabled={!canEditUserPermissions}
                selectable={false}
                defaultExpandAll
                onCheck={handlePermissionCheck}
                style={{ background: '#fafafa', padding: 12, borderRadius: 8 }}
              />
              {canEditUserPermissions ? (
                <div style={{ marginTop: 16, textAlign: 'right' }}>
                  <Button
                    type="primary"
                    icon={<SaveOutlined />}
                    loading={permSaving}
                    disabled={!hasDirectPermChanges}
                    onClick={() => void handleSavePermissions()}
                  >
                    {t('system.users.savePermissionAssign', '保存权限分配')}
                  </Button>
                </div>
              ) : null}
            </>
          )}
        </Spin>
      ),
    },
  ]

  return (
    <PageContainer title={t('menu.systemUsers', '用户管理')} subtitle={t('system.users.pageSubtitle', '管理用户的基本信息、角色、分店和权限。')}>
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder={t('system.users.searchPlaceholder', '搜索用户名 / 姓名 / 邮箱')}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 280 }}
            allowClear
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('system.users.filterByStore', '按分店过滤')}
            style={{ width: 220 }}
            value={selectedStoreGuid}
            onChange={(value) => setSelectedStoreGuid(value)}
            options={visibleStoreOptions}
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('system.users.filterByRole', '按角色过滤')}
            style={{ width: 180 }}
            value={selectedRoleGuid}
            onChange={(value) => setSelectedRoleGuid(value)}
            options={roleOptions}
          />
          <Button type="primary" onClick={() => void loadData(1, pageSize, sortBy, sortOrder)}>
            {t('common.query', '查询')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize, sortBy, sortOrder)}>
            {t('common.refresh', '刷新')}
          </Button>
          <HasPermission code={P.Users.Create}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => void handleOpenCreate()}>
              {t('system.users.createUser', '创建用户')}
            </Button>
          </HasPermission>
        </Space>

        <Table
          rowKey="userGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 1360 }}
          onChange={(_pagination, _filters, sorter) => {
            const currentSorter = Array.isArray(sorter) ? sorter[0] : sorter
            const field = currentSorter?.field || currentSorter?.column?.dataIndex
            const order = currentSorter?.order as 'ascend' | 'descend' | undefined

            if (field && order) {
              setSortBy(String(field))
              setSortOrder(order)
              void loadData(1, pageSize, String(field), order)
            } else {
              setSortBy(undefined)
              setSortOrder(null)
              void loadData(1, pageSize, undefined, undefined)
            }
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize, sortBy, sortOrder)
            },
          }}
        />
      </Card>

      <Drawer
        title={detailUser ? t('system.users.userDetailTitle', '用户详情 - {{name}}', { name: detailUser.username }) : t('system.users.userDetail', '用户详情')}
        width={820}
        open={detailOpen}
        onClose={() => {
          setDetailOpen(false)
          setDetailUser(null)
          setDetailStores([])
        }}
        destroyOnHidden
      >
        {detailLoading ? (
          <Typography.Text type="secondary">{t('system.users.loadingDetail', '正在加载用户详情...')}</Typography.Text>
        ) : !detailUser ? (
          <Typography.Text type="danger">{t('system.users.userNotFound', '未找到用户信息')}</Typography.Text>
        ) : (
          <Space direction="vertical" size={16} style={{ width: '100%' }}>
            <Descriptions bordered column={2}>
              <Descriptions.Item label={t('system.users.username', '用户名')}>{detailUser.username}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.fullName', '姓名')}>{detailUser.fullName || t('common.emptyValue')}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.email', '邮箱')}>{detailUser.email}</Descriptions.Item>
              <Descriptions.Item label={t('common.status', '状态')}>
                <Tag color={detailUser.isActive ? 'success' : 'default'}>
                  {detailUser.isActive ? t('common.active', '启用') : t('common.inactive', '停用')}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.users.lastLogin', '最近登录')}>{formatUserLocalDateTime(detailUser.lastLoginAt, t('common.emptyValue'))}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.lastLoginIp', '最近登录 IP')}>{detailUser.lastLoginIp || t('common.emptyValue')}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.roles', '角色')} span={2}>
                <Space wrap>
                  {detailUser.roleNames?.length ? detailUser.roleNames.map((item) => <Tag key={item}>{item}</Tag>) : t('common.emptyValue')}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.users.permissions', '权限')} span={2}>
                <Space wrap>
                  {detailUser.permissions?.length
                    ? detailUser.permissions.map((p) => <Tag key={p} color="green">{p}</Tag>)
                    : t('common.emptyValue')}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label={t('system.users.createdAt', '创建时间')}>{detailUser.createdAt}</Descriptions.Item>
              <Descriptions.Item label={t('system.users.updatedAt', '更新时间')}>{detailUser.updatedAt}</Descriptions.Item>
            </Descriptions>

            <Card title={t('system.users.linkedStores', '关联分店')} size="small">
              <List
                dataSource={detailStores}
                locale={{ emptyText: t('system.users.noLinkedStores', '暂无关联分店') }}
                renderItem={(item) => (
                  <List.Item>
                    <Space>
                      <Typography.Text strong>{item.storeName}</Typography.Text>
                      <Tag>{item.storeCode}</Tag>
                      {item.isManageable ? <Tag color="processing">{t('system.users.manageableStore', '可管理')}</Tag> : null}
                    </Space>
                  </List.Item>
                )}
              />
            </Card>
          </Space>
        )}
      </Drawer>

      <Modal
        title={
          loginRecordsUser
            ? t('system.users.loginRecordsTitle', '登录记录 - {{name}}', { name: loginRecordsUser.username })
            : t('system.users.loginRecords', '登录记录')
        }
        width={960}
        open={loginRecordsOpen}
        footer={[
          <Button
            key="refresh"
            icon={<ReloadOutlined />}
            loading={loginRecordsLoading}
            onClick={() => {
              if (loginRecordsUser) {
                void loadLoginRecords(loginRecordsUser, loginRecordsPage, loginRecordsPageSize)
              }
            }}
          >
            {t('common.refresh', '刷新')}
          </Button>,
          <Button
            key="close"
            onClick={() => {
              setLoginRecordsOpen(false)
              setLoginRecordsUser(null)
              setLoginRecords([])
            }}
          >
            {t('common.close', '关闭')}
          </Button>,
        ]}
        onCancel={() => {
          setLoginRecordsOpen(false)
          setLoginRecordsUser(null)
          setLoginRecords([])
        }}
        destroyOnHidden
      >
        <Table<UserLoginRecordDto>
          rowKey="sessionId"
          size="small"
          loading={loginRecordsLoading}
          columns={loginRecordColumns}
          dataSource={loginRecords}
          scroll={{ x: 860 }}
          pagination={{
            current: loginRecordsPage,
            pageSize: loginRecordsPageSize,
            total: loginRecordsTotal,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => {
              if (loginRecordsUser) {
                void loadLoginRecords(loginRecordsUser, nextPage, nextPageSize)
              }
            },
          }}
        />
      </Modal>

      <Drawer
        title={editingUser ? t('system.users.editUserTitle', '编辑用户 - {{name}}', { name: editingUser.username }) : t('system.users.editUser', '编辑用户')}
        width={860}
        open={editOpen}
        onClose={() => {
          setEditOpen(false)
          setEditingUser(null)
          setPermissionState(null)
          setDirectPermKeys([])
          form.resetFields()
        }}
        destroyOnHidden
      >
        <Tabs activeKey={editTab} onChange={setEditTab} items={editTabItems} />
      </Drawer>

      <Modal
        title={t('system.users.resetPassword', '重置密码')}
        open={resetPwdOpen}
        onCancel={() => {
          setResetPwdOpen(false)
          resetPwdForm.resetFields()
        }}
        onOk={() => void handleResetPassword()}
        confirmLoading={resetPwdLoading}
        destroyOnHidden
      >
        <Form form={resetPwdForm} layout="vertical">
          <Form.Item
            label={t('system.users.newPasswordLabel', '请输入新密码')}
            name="newPassword"
            rules={[
              { required: true, message: t('system.users.newPasswordRequired', '请输入新密码') },
              { min: 6, message: t('system.users.passwordMinLength', '密码至少6位') },
            ]}
          >
            <Input.Password placeholder={t('system.users.newPasswordPlaceholder', '输入新密码')} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={t('system.users.createUser', '创建用户')}
        width={860}
        open={createOpen}
        onCancel={() => {
          if (createLoading) return
          setCreateOpen(false)
          createForm.resetFields()
          setCreateRoleTargetKeys([])
          setCreateStoreTargetKeys([])
          setCreateStoreManageableKeys([])
        }}
        footer={createTab === 'info' ? [
          <Button key="cancel" onClick={() => {
            setCreateOpen(false)
            createForm.resetFields()
            setCreateRoleTargetKeys([])
            setCreateStoreTargetKeys([])
            setCreateStoreManageableKeys([])
          }}>
            {t('common.cancel', '取消')}
          </Button>,
          <Button key="submit" type="primary" loading={createLoading} onClick={() => void handleCreateSubmit()}>
            {t('common.create', '新建')}
          </Button>,
        ] : null}
        destroyOnHidden
      >
        <Tabs
          activeKey={createTab}
          onChange={setCreateTab}
          items={[
            {
              key: 'info',
              label: t('system.users.basicInfo', '基本信息'),
              children: (
                <Form form={createForm} layout="vertical" style={{ maxWidth: 480 }}>
                  <Form.Item
                    label={t('system.users.username', '用户名')}
                    name="username"
                    rules={[
                      { required: true, message: t('system.users.usernameRequired', '请输入用户名') },
                      { min: 3, max: 50, message: t('system.users.usernameLengthInvalid', '用户名长度3-50个字符') },
                    ]}
                  >
                    <Input />
                  </Form.Item>
                  <Form.Item
                    label={t('system.users.email', '邮箱')}
                    name="email"
                    rules={[
                      { required: true, message: t('system.users.emailRequired', '请输入邮箱') },
                      { type: 'email', message: t('system.users.emailInvalid', '邮箱格式不正确') },
                    ]}
                  >
                    <Input />
                  </Form.Item>
                  <Form.Item
                    label={t('system.users.password', '密码')}
                    name="password"
                    rules={[
                      { required: true, message: t('system.users.passwordRequired', '请输入密码') },
                      { min: 6, message: t('system.users.passwordMinLength', '密码至少6位') },
                    ]}
                  >
                    <Input.Password />
                  </Form.Item>
                  <Form.Item
                    label={t('system.users.confirmPassword', '确认密码')}
                    name="confirmPassword"
                    dependencies={['password']}
                    rules={[
                      { required: true, message: t('system.users.confirmPasswordRequired', '请确认密码') },
                      ({ getFieldValue }) => ({
                        validator(_, value) {
                          if (!value || getFieldValue('password') === value) return Promise.resolve()
                          return Promise.reject(new Error(t('system.users.confirmPasswordMismatch', '两次输入的密码不一致')))
                        },
                      }),
                    ]}
                  >
                    <Input.Password />
                  </Form.Item>
                  <Form.Item label={t('system.users.fullName', '姓名')} name="fullName">
                    <Input />
                  </Form.Item>
                  <Form.Item label={t('common.status', '状态')} name="isActive" valuePropName="checked" initialValue={true}>
                    <Switch checkedChildren={t('common.active', '启用')} unCheckedChildren={t('common.inactive', '停用')} />
                  </Form.Item>
                </Form>
              ),
            },
            {
              key: 'roles',
              label: t('system.users.roles', '角色'),
              children: (
                <Spin spinning={createRoleLoading}>
                  <div style={{ marginBottom: 12 }}>
                    <Typography.Text type="secondary">
                      {t('system.users.createSelectedRoles', '已选择 {{count}} 个角色', { count: createRoleTargetKeys.length })}
                    </Typography.Text>
                  </div>
                  <Transfer
                    dataSource={allRoles.map((role) => ({
                      key: role.roleGUID,
                      title: role.roleName,
                      description: role.description || '',
                    }))}
                    targetKeys={createRoleTargetKeys}
                    onChange={(nextTargetKeys: Key[]) => {
                      setCreateRoleTargetKeys(nextTargetKeys.map(String))
                    }}
                    render={(item) => item.title}
                    titles={[t('system.users.availableRoles', '可选角色'), t('system.users.assignedRolesLabel', '已分配角色')]}
                    listStyle={{ width: 320, height: 400 }}
                    showSearch
                  />
                </Spin>
              ),
            },
            {
              key: 'stores',
              label: t('system.users.stores', '分店'),
              children: (
                <Spin spinning={createStoreLoading}>
                  <div style={{ marginBottom: 12 }}>
                    <Typography.Text type="secondary">
                      {t('system.users.createSelectedStores', '已选择 {{count}} 个分店', { count: createStoreTargetKeys.length })}
                    </Typography.Text>
                  </div>
                  <Transfer
                    dataSource={sortedStores.map((store) => ({
                      key: store.storeGUID,
                      title: `${store.storeName} (${store.storeCode})`,
                      description: store.address || '',
                    }))}
                    targetKeys={createStoreTargetKeys}
                    onChange={(nextTargetKeys: Key[]) => {
                      handleCreateStoreTargetChange(nextTargetKeys)
                    }}
                    render={(item) => item.title}
                    titles={[t('system.users.availableStores', '可选分店'), t('system.users.assignedStoresLabel', '已分配分店')]}
                    listStyle={{ width: 320, height: 400 }}
                    showSearch
                  />
                  {renderManageableStoreControls(createStoreTargetKeys, createStoreManageableKeys, setCreateStoreManageableKeys)}
                </Spin>
              ),
            },
          ]}
        />
      </Modal>
    </PageContainer>
  )
}
