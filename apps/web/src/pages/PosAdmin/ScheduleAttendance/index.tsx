import {
  CalendarOutlined,
  CheckOutlined,
  CloseOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  EnvironmentOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  DatePicker,
  Drawer,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Switch,
  Table,
  Tabs,
  Tag,
  TimePicker,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import isoWeek from 'dayjs/plugin/isoWeek'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  approveAttendanceApproval,
  batchUpsertAttendanceHolidays,
  createAttendanceHoliday,
  createAttendanceSchedule,
  deleteAttendanceHoliday,
  deleteAttendanceSchedule,
  getAttendanceAvailability,
  getAttendanceHolidays,
  getAttendanceLocationSamples,
  getAttendancePunches,
  getAttendanceScheduleWeek,
  getAttendanceSettings,
  getPendingAttendanceApprovals,
  publishAttendanceScheduleWeek,
  rejectAttendanceApproval,
  syncAttendanceHolidays,
  updateAttendanceHoliday,
  updateAttendanceSchedule,
  updateAttendanceSettings,
} from '../../../services/scheduleAttendanceService'
import { getActiveStores } from '../../../services/storeService'
import { getStoreUsersGrid } from '../../../services/storeUserService'
import { useAuthStore } from '../../../store/auth'
import type { StoreUserListDto } from '../../../services/storeUserService'
import type {
  AttendanceApprovalDto,
  AttendanceAvailabilityDto,
  AttendanceHolidayBusinessStatus,
  AttendanceLocationSampleDto,
  AttendancePunchDto,
  AttendancePunchStatus,
  AttendanceReviewStatus,
  AttendanceScheduleDto,
  AttendanceScheduleStatus,
  AttendanceSettingsDto,
  AttendanceStoreHolidayDto,
  BatchUpsertAttendanceHolidayPayload,
  SaveAttendanceHolidayPayload,
  SaveAttendanceSchedulePayload,
  SaveAttendanceSettingsPayload,
} from '../../../types/scheduleAttendance'
import {
  buildStoreOptionsFromUserStores,
  filterStoreOptionsByManagedCodes,
  isStoreCodeInManagedScope,
  shouldSkipStoreQueryForScope,
} from '../../../utils/managedStoreScope'

dayjs.extend(isoWeek)

type TabKey = 'schedules' | 'availability' | 'punches' | 'approvals' | 'holidays' | 'settings'
type ReviewAction = 'approve' | 'reject'

interface AttendanceMapPreview {
  title: string
  latitude: number
  longitude: number
  accuracy?: number
  capturedAt?: string
}

interface ScheduleRow {
  rowKey: string
  storeCode: string
  storeName?: string
  userGuid: string
  userName?: string
  schedulesByDate: Record<string, AttendanceScheduleDto[]>
}

interface ScheduleFormValues {
  storeCode: string
  userGuid: string
  workDate: Dayjs
  timeRange: [Dayjs, Dayjs]
  status?: AttendanceScheduleStatus
  remark?: string
}

interface HolidayFormValues {
  storeCode?: string
  storeCodes?: string[]
  holidayDate: Dayjs
  holidayName: string
  businessStatus: AttendanceHolidayBusinessStatus
  businessTime?: [Dayjs, Dayjs]
  isPaidHoliday?: boolean
  remark?: string
}

interface BatchHolidayFormValues {
  storeCodes: string[]
  holidayDate: Dayjs
  holidayName: string
  businessStatus: AttendanceHolidayBusinessStatus
  businessTime?: [Dayjs, Dayjs]
  isPaidHoliday?: boolean
  remark?: string
}

interface ListState<T> {
  loading: boolean
  items: T[]
  total: number
  page: number
  pageSize: number
}

const initialListState = {
  loading: false,
  items: [],
  total: 0,
  page: 1,
  pageSize: 20,
}

function formatDate(value?: string) {
  return value ? dayjs(value).format('YYYY-MM-DD') : '--'
}

function formatDateTime(value?: string) {
  return value ? dayjs(value).format('YYYY-MM-DD HH:mm') : '--'
}

function formatTime(value?: string) {
  if (!value) return '--'
  const parsed = dayjs(value, ['HH:mm:ss', 'HH:mm'])
  return parsed.isValid() ? parsed.format('HH:mm') : value
}

function toDayjsTime(value?: string) {
  if (!value) return undefined
  const parsed = dayjs(value, ['HH:mm:ss', 'HH:mm'])
  return parsed.isValid() ? parsed : undefined
}

function getIsoWeekStart(value: Dayjs) {
  return value.isoWeekday(1).startOf('day')
}

function getIsoWeekEnd(value: Dayjs) {
  return getIsoWeekStart(value).add(6, 'day')
}

export default function ScheduleAttendancePage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const currentUser = useAuthStore((state) => state.currentUser)
  const managedStoreCodes = access.managedStoreCodes?.()
  const managedStoreCodeKey = managedStoreCodes?.join(',') ?? 'all'
  const visibleStoreCodes = access.visibleStoreCodes?.()
  const visibleStoreCodeKey = visibleStoreCodes?.join(',') ?? 'all'
  const [activeTab, setActiveTab] = useState<TabKey>('schedules')
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [storeCode, setStoreCode] = useState<string | undefined>()
  const [userGuid, setUserGuid] = useState<string | undefined>()
  const [selectedWeek, setSelectedWeek] = useState<Dayjs>(dayjs())
  const [dateRange, setDateRange] = useState<[Dayjs, Dayjs] | null>([getIsoWeekStart(dayjs()), getIsoWeekEnd(dayjs())])
  const [schedules, setSchedules] = useState<ListState<AttendanceScheduleDto>>(initialListState)
  const [storeUsers, setStoreUsers] = useState<StoreUserListDto[]>([])
  const [storeUsersLoading, setStoreUsersLoading] = useState(false)
  const [scheduleHolidays, setScheduleHolidays] = useState<AttendanceStoreHolidayDto[]>([])
  const [scheduleHolidaysLoading, setScheduleHolidaysLoading] = useState(false)
  const [availability, setAvailability] = useState<ListState<AttendanceAvailabilityDto>>(initialListState)
  const [punches, setPunches] = useState<ListState<AttendancePunchDto>>(initialListState)
  const [approvals, setApprovals] = useState<ListState<AttendanceApprovalDto>>(initialListState)
  const [holidays, setHolidays] = useState<ListState<AttendanceStoreHolidayDto>>(initialListState)
  const [settings, setSettings] = useState<AttendanceSettingsDto | null>(null)
  const [settingsLoading, setSettingsLoading] = useState(false)
  const [scheduleDrawerOpen, setScheduleDrawerOpen] = useState(false)
  const [editingSchedule, setEditingSchedule] = useState<AttendanceScheduleDto | null>(null)
  const [holidayDrawerOpen, setHolidayDrawerOpen] = useState(false)
  const [editingHoliday, setEditingHoliday] = useState<AttendanceStoreHolidayDto | null>(null)
  const [batchHolidayDrawerOpen, setBatchHolidayDrawerOpen] = useState(false)
  const [reviewModalOpen, setReviewModalOpen] = useState(false)
  const [reviewTarget, setReviewTarget] = useState<AttendanceApprovalDto | null>(null)
  const [reviewAction, setReviewAction] = useState<ReviewAction>('approve')
  const [mapPreview, setMapPreview] = useState<AttendanceMapPreview | null>(null)
  const [sampleDrawerOpen, setSampleDrawerOpen] = useState(false)
  const [sampleLoading, setSampleLoading] = useState(false)
  const [sampleRows, setSampleRows] = useState<AttendanceLocationSampleDto[]>([])
  const [sampleTitle, setSampleTitle] = useState('')
  const [saving, setSaving] = useState(false)
  const [publishing, setPublishing] = useState(false)
  const [syncingHolidays, setSyncingHolidays] = useState(false)
  const [scheduleForm] = Form.useForm<ScheduleFormValues>()
  const [holidayForm] = Form.useForm<HolidayFormValues>()
  const [batchHolidayForm] = Form.useForm<BatchHolidayFormValues>()
  const [reviewForm] = Form.useForm<{ reviewRemark?: string }>()
  const [settingsForm] = Form.useForm<SaveAttendanceSettingsPayload>()

  const storeNameMap = useMemo(() => new Map(storeOptions.map((item) => [item.value, item.label])), [storeOptions])
  const externalMapUrl = useMemo(() => {
    if (!mapPreview) {
      return ''
    }
    return `https://maps.google.com/?q=${mapPreview.latitude},${mapPreview.longitude}`
  }, [mapPreview])
  const editableStoreOptions = useMemo(
    () => filterStoreOptionsByManagedCodes(storeOptions, managedStoreCodes),
    [managedStoreCodeKey, storeOptions],
  )
  const canEditSelectedStore = isStoreCodeInManagedScope(storeCode, managedStoreCodes)
  const canEditStoreCode = useCallback(
    (targetStoreCode?: string) => isStoreCodeInManagedScope(targetStoreCode, managedStoreCodes),
    [managedStoreCodeKey],
  )
  const canViewStoreCode = useCallback(
    (targetStoreCode?: string) => isStoreCodeInManagedScope(targetStoreCode, visibleStoreCodes),
    [visibleStoreCodeKey],
  )
  const shouldSkipVisibleStoreQuery = useCallback(
    () => shouldSkipStoreQueryForScope(storeCode, visibleStoreCodes),
    [storeCode, visibleStoreCodeKey],
  )
  const weekStart = useMemo(() => getIsoWeekStart(selectedWeek), [selectedWeek])
  const weekEnd = useMemo(() => getIsoWeekEnd(selectedWeek), [selectedWeek])
  const weekDays = useMemo(() => Array.from({ length: 7 }, (_, index) => weekStart.add(index, 'day')), [weekStart])
  const formatWeekLabel = useCallback((value: Dayjs) => {
    const start = getIsoWeekStart(value)
    return t('posAdmin.scheduleAttendance.weekTable.yearWeek', {
      year: start.isoWeekYear(),
      week: String(start.isoWeek()).padStart(2, '0'),
    })
  }, [t])
  const selectedWeekLabel = useMemo(() => formatWeekLabel(weekStart), [formatWeekLabel, weekStart])

  const queryParams = useCallback((page: number, pageSize: number) => ({
    page,
    pageSize,
    storeCode,
    userGuid: userGuid?.trim() || undefined,
    weekStartDate: dateRange?.[0]?.format('YYYY-MM-DD'),
    fromDate: dateRange?.[0]?.format('YYYY-MM-DD'),
    toDate: dateRange?.[1]?.format('YYYY-MM-DD'),
  }), [dateRange, storeCode, userGuid])

  const scheduleQueryParams = useCallback((page: number, pageSize: number) => ({
    page,
    pageSize,
    storeCode,
    userGuid: userGuid?.trim() || undefined,
    weekStartDate: weekStart.format('YYYY-MM-DD'),
    fromDate: weekStart.format('YYYY-MM-DD'),
    toDate: weekEnd.format('YYYY-MM-DD'),
  }), [storeCode, userGuid, weekEnd, weekStart])

  const loadSchedules = useCallback(async (page = schedules.page, pageSize = schedules.pageSize) => {
    if (shouldSkipVisibleStoreQuery()) {
      setSchedules({ ...initialListState, page, pageSize })
      return
    }

    setSchedules((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendanceScheduleWeek(scheduleQueryParams(page, pageSize))
      setSchedules({ loading: false, items: result, total: result.length, page, pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadSchedulesFailed'))
      setSchedules((prev) => ({ ...prev, loading: false }))
    }
  }, [scheduleQueryParams, schedules.page, schedules.pageSize, shouldSkipVisibleStoreQuery, t])

  const loadStoreUsers = useCallback(async () => {
    if (!storeCode || !canViewStoreCode(storeCode)) {
      setStoreUsers([])
      return
    }

    setStoreUsersLoading(true)
    try {
      const result = await getStoreUsersGrid({
        storeCode,
        keyword: userGuid?.trim() || undefined,
        status: 1,
      })
      setStoreUsers(result.items)
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadStoreUsersFailed'))
      setStoreUsers([])
    } finally {
      setStoreUsersLoading(false)
    }
  }, [canViewStoreCode, storeCode, t, userGuid])

  const loadScheduleHolidays = useCallback(async () => {
    if (!storeCode || !canViewStoreCode(storeCode)) {
      setScheduleHolidays([])
      return
    }

    setScheduleHolidaysLoading(true)
    try {
      const result = await getAttendanceHolidays({
        storeCode,
        fromDate: weekStart.format('YYYY-MM-DD'),
        toDate: weekEnd.format('YYYY-MM-DD'),
      })
      setScheduleHolidays(result.items)
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadHolidaysFailed'))
      setScheduleHolidays([])
    } finally {
      setScheduleHolidaysLoading(false)
    }
  }, [canViewStoreCode, storeCode, t, weekEnd, weekStart])

  const loadAvailability = useCallback(async (page = availability.page, pageSize = availability.pageSize) => {
    if (shouldSkipVisibleStoreQuery()) {
      setAvailability({ ...initialListState, page, pageSize })
      return
    }

    setAvailability((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendanceAvailability(queryParams(page, pageSize))
      setAvailability({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadAvailabilityFailed'))
      setAvailability((prev) => ({ ...prev, loading: false }))
    }
  }, [availability.page, availability.pageSize, queryParams, shouldSkipVisibleStoreQuery, t])

  const loadPunches = useCallback(async (page = punches.page, pageSize = punches.pageSize) => {
    if (shouldSkipVisibleStoreQuery()) {
      setPunches({ ...initialListState, page, pageSize })
      return
    }

    setPunches((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendancePunches(queryParams(page, pageSize))
      setPunches({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadPunchesFailed'))
      setPunches((prev) => ({ ...prev, loading: false }))
    }
  }, [punches.page, punches.pageSize, queryParams, shouldSkipVisibleStoreQuery, t])

  const loadApprovals = useCallback(async (page = approvals.page, pageSize = approvals.pageSize) => {
    if (shouldSkipVisibleStoreQuery()) {
      setApprovals({ ...initialListState, page, pageSize })
      return
    }

    setApprovals((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getPendingAttendanceApprovals(queryParams(page, pageSize))
      setApprovals({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadApprovalsFailed'))
      setApprovals((prev) => ({ ...prev, loading: false }))
    }
  }, [approvals.page, approvals.pageSize, queryParams, shouldSkipVisibleStoreQuery, t])

  const loadHolidays = useCallback(async (page = holidays.page, pageSize = holidays.pageSize) => {
    if (shouldSkipVisibleStoreQuery()) {
      setHolidays({ ...initialListState, page, pageSize })
      return
    }

    setHolidays((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendanceHolidays(queryParams(page, pageSize))
      setHolidays({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadHolidaysFailed'))
      setHolidays((prev) => ({ ...prev, loading: false }))
    }
  }, [holidays.page, holidays.pageSize, queryParams, shouldSkipVisibleStoreQuery, t])

  const loadSettings = useCallback(async () => {
    setSettingsLoading(true)
    try {
      const result = await getAttendanceSettings()
      setSettings(result)
      settingsForm.setFieldsValue(result)
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadSettingsFailed'))
    } finally {
      setSettingsLoading(false)
    }
  }, [settingsForm, t])

  const loadActiveTab = useCallback((page = 1) => {
    if (activeTab === 'schedules') {
      void loadSchedules(page)
      void loadStoreUsers()
      void loadScheduleHolidays()
    }
    if (activeTab === 'availability') void loadAvailability(page)
    if (activeTab === 'punches') void loadPunches(page)
    if (activeTab === 'approvals') void loadApprovals(page)
    if (activeTab === 'holidays') void loadHolidays(page)
    if (activeTab === 'settings') void loadSettings()
  }, [activeTab, loadApprovals, loadAvailability, loadHolidays, loadPunches, loadScheduleHolidays, loadSchedules, loadSettings, loadStoreUsers])

  useEffect(() => {
    if (visibleStoreCodes !== null) {
      const stores = buildStoreOptionsFromUserStores(currentUser?.stores)
      setStoreOptions(stores)
      if (storeCode && !stores.some((store) => store.value === storeCode)) {
        setStoreCode(undefined)
      }
      return
    }

    void getActiveStores().then(setStoreOptions).catch((error) => {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadStoresFailed'))
    })
  }, [currentUser?.stores, storeCode, t, visibleStoreCodeKey])

  useEffect(() => {
    loadActiveTab(1)
  }, [activeTab, storeCode, dateRange, selectedWeek])

  const statusLabel = useCallback((type: 'schedule' | 'punch' | 'review', value?: string) => {
    if (!value) return '--'
    return t(`posAdmin.scheduleAttendance.status.${type}.${value}`, value)
  }, [t])

  const scheduleStatusTag = (value: AttendanceScheduleStatus) => (
    <Tag color={value === 'Active' ? 'green' : value === 'Draft' ? 'gold' : 'red'}>{statusLabel('schedule', value)}</Tag>
  )

  const punchStatusTag = (value: AttendancePunchStatus) => {
    const color = value === 'Normal' || value === 'Approved' ? 'green' : value === 'PendingApproval' ? 'orange' : 'red'
    return <Tag color={color}>{statusLabel('punch', value)}</Tag>
  }

  const reviewStatusTag = (value: AttendanceReviewStatus) => {
    const color = value === 'Approved' ? 'green' : value === 'Pending' ? 'orange' : value === 'Rejected' ? 'red' : 'default'
    return <Tag color={color}>{statusLabel('review', value)}</Tag>
  }

  const holidayStatusTag = (value: AttendanceHolidayBusinessStatus) => {
    const color = value === 'Open' ? 'green' : value === 'Partial' ? 'orange' : 'red'
    return <Tag color={color}>{t(`posAdmin.scheduleAttendance.status.holiday.${value}`, value)}</Tag>
  }

  const openScheduleDrawer = (record?: AttendanceScheduleDto, defaults?: Partial<ScheduleFormValues>) => {
    const targetStoreCode = record?.storeCode ?? defaults?.storeCode ?? storeCode
    if (!access.canEditAttendanceSchedule || !canEditStoreCode(targetStoreCode)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    setEditingSchedule(record ?? null)
    scheduleForm.resetFields()
    if (record) {
      scheduleForm.setFieldsValue({
        storeCode: record.storeCode,
        userGuid: record.userGuid,
        workDate: dayjs(record.workDate),
        timeRange: [toDayjsTime(record.startTime), toDayjsTime(record.endTime)].filter(Boolean) as [Dayjs, Dayjs],
        status: record.status,
        remark: record.remark,
      })
    } else {
      scheduleForm.setFieldsValue({
        storeCode,
        status: 'Draft',
        timeRange: [dayjs('09:00', 'HH:mm'), dayjs('17:00', 'HH:mm')],
        ...defaults,
      })
    }
    setScheduleDrawerOpen(true)
  }

  const saveSchedule = async () => {
    try {
      const values = await scheduleForm.validateFields()
      if (!access.canEditAttendanceSchedule || !canEditStoreCode(values.storeCode)) {
        message.error(t('message.noPermission', '无权操作该数据'))
        return
      }

      const payload: SaveAttendanceSchedulePayload = {
        storeCode: values.storeCode,
        userGuid: values.userGuid.trim(),
        workDate: values.workDate.format('YYYY-MM-DD'),
        startTime: values.timeRange[0].format('HH:mm'),
        endTime: values.timeRange[1].format('HH:mm'),
        status: editingSchedule ? values.status : 'Draft',
        remark: values.remark?.trim() || undefined,
      }
      setSaving(true)
      if (editingSchedule) {
        await updateAttendanceSchedule(editingSchedule.scheduleGuid, payload)
        message.success(t('message.updateSuccess'))
      } else {
        await createAttendanceSchedule(payload)
        message.success(t('message.createSuccess'))
      }
      setScheduleDrawerOpen(false)
      void loadSchedules()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.saveScheduleFailed'))
    } finally {
      setSaving(false)
    }
  }

  const confirmDeleteSchedule = (record: AttendanceScheduleDto) => {
    if (!access.canEditAttendanceSchedule || !canEditStoreCode(record.storeCode)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    Modal.confirm({
      title: t('posAdmin.scheduleAttendance.confirm.deleteSchedule'),
      okText: t('common.delete'),
      cancelText: t('common.cancel'),
      okButtonProps: { danger: true },
      onOk: async () => {
        await deleteAttendanceSchedule(record.scheduleGuid)
        message.success(t('message.deleteSuccess'))
        setScheduleDrawerOpen(false)
        void loadSchedules()
      },
    })
  }

  const openHolidayDrawer = (record?: AttendanceStoreHolidayDto) => {
    if (record && (!access.canEditAttendanceHoliday || !canEditStoreCode(record.storeCode))) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    setEditingHoliday(record ?? null)
    holidayForm.resetFields()
    if (record) {
      const openTime = toDayjsTime(record.openTime)
      const closeTime = toDayjsTime(record.closeTime)
      holidayForm.setFieldsValue({
        storeCode: record.storeCode,
        holidayDate: dayjs(record.holidayDate),
        holidayName: record.holidayName,
        businessStatus: record.businessStatus,
        businessTime: openTime && closeTime ? [openTime, closeTime] : undefined,
        isPaidHoliday: record.isPaidHoliday,
        remark: record.remark,
      })
    } else {
      holidayForm.setFieldsValue({
        storeCodes: storeCode && canEditStoreCode(storeCode) ? [storeCode] : [],
        businessStatus: 'Open',
        isPaidHoliday: true,
      })
    }
    setHolidayDrawerOpen(true)
  }

  const saveHoliday = async () => {
    try {
      const values = await holidayForm.validateFields()
      const targetStoreCodes = values.storeCodes?.filter(Boolean) ?? []
      const selectedStoreCodes = editingHoliday
        ? [values.storeCode].filter(Boolean)
        : targetStoreCodes
      if (
        !access.canEditAttendanceHoliday ||
        selectedStoreCodes.length === 0 ||
        selectedStoreCodes.some((item) => !canEditStoreCode(item))
      ) {
        message.error(t('message.noPermission', '无权操作该数据'))
        return
      }
      const payloadStoreCode = values.storeCode ?? targetStoreCodes[0]
      if (!payloadStoreCode) {
        message.error(t('message.noPermission', '无权操作该数据'))
        return
      }

      const payload: SaveAttendanceHolidayPayload = {
        storeCode: payloadStoreCode,
        holidayDate: values.holidayDate.format('YYYY-MM-DD'),
        holidayName: values.holidayName.trim(),
        businessStatus: values.businessStatus,
        openTime: values.businessTime?.[0]?.format('HH:mm'),
        closeTime: values.businessTime?.[1]?.format('HH:mm'),
        isPaidHoliday: values.isPaidHoliday ?? true,
        remark: values.remark?.trim() || undefined,
      }
      setSaving(true)
      if (editingHoliday) {
        await updateAttendanceHoliday(editingHoliday.holidayGuid, payload)
        message.success(t('message.updateSuccess'))
      } else {
        if (targetStoreCodes.length > 1) {
          const result = await batchUpsertAttendanceHolidays({
            storeCodes: targetStoreCodes,
            holidayDate: payload.holidayDate,
            holidayName: payload.holidayName,
            businessStatus: payload.businessStatus,
            openTime: payload.openTime,
            closeTime: payload.closeTime,
            isPaidHoliday: payload.isPaidHoliday,
            remark: payload.remark,
          })
          message.success(t('posAdmin.scheduleAttendance.messages.batchHolidaySuccess', {
            created: result.createdCount,
            updated: result.updatedCount,
          }))
        } else {
          await createAttendanceHoliday(payload)
          message.success(t('message.createSuccess'))
        }
      }
      setHolidayDrawerOpen(false)
      void loadHolidays()
      void loadScheduleHolidays()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.saveHolidayFailed'))
    } finally {
      setSaving(false)
    }
  }

  const openBatchHolidayDrawer = () => {
    if (!access.canEditAttendanceHoliday || !editableStoreOptions.length) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    batchHolidayForm.resetFields()
    batchHolidayForm.setFieldsValue({
      storeCodes: storeCode && canEditStoreCode(storeCode) ? [storeCode] : [],
      businessStatus: 'Open',
      isPaidHoliday: true,
    })
    setBatchHolidayDrawerOpen(true)
  }

  const saveBatchHoliday = async () => {
    try {
      const values = await batchHolidayForm.validateFields()
      if (
        !access.canEditAttendanceHoliday ||
        !values.storeCodes.length ||
        values.storeCodes.some((item) => !canEditStoreCode(item))
      ) {
        message.error(t('message.noPermission', '无权操作该数据'))
        return
      }

      const payload: BatchUpsertAttendanceHolidayPayload = {
        storeCodes: values.storeCodes,
        holidayDate: values.holidayDate.format('YYYY-MM-DD'),
        holidayName: values.holidayName.trim(),
        businessStatus: values.businessStatus,
        openTime: values.businessTime?.[0]?.format('HH:mm'),
        closeTime: values.businessTime?.[1]?.format('HH:mm'),
        isPaidHoliday: values.isPaidHoliday ?? true,
        remark: values.remark?.trim() || undefined,
      }
      setSaving(true)
      const result = await batchUpsertAttendanceHolidays(payload)
      message.success(t('posAdmin.scheduleAttendance.messages.batchHolidaySuccess', {
        created: result.createdCount,
        updated: result.updatedCount,
      }))
      setBatchHolidayDrawerOpen(false)
      void loadHolidays()
      void loadScheduleHolidays()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.saveHolidayFailed'))
    } finally {
      setSaving(false)
    }
  }

  const syncFutureHolidays = async () => {
    if (!storeCode) {
      message.warning(t('posAdmin.scheduleAttendance.messages.selectStoreBeforeHolidaySync'))
      return
    }
    if (!access.canEditAttendanceHoliday || !canEditStoreCode(storeCode)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    setSyncingHolidays(true)
    try {
      const result = await syncAttendanceHolidays({
        storeCode,
        daysAhead: 30,
      })
      message.success(t('posAdmin.scheduleAttendance.messages.holidaySyncSuccess', {
        synced: result.syncedCount,
        created: result.createdCount,
        updated: result.updatedCount,
        skipped: result.skippedCount,
      }))
      void loadHolidays()
      void loadScheduleHolidays()
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.holidaySyncFailed'))
    } finally {
      setSyncingHolidays(false)
    }
  }

  const confirmDeleteHoliday = (record: AttendanceStoreHolidayDto) => {
    if (!access.canEditAttendanceHoliday || !canEditStoreCode(record.storeCode)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    Modal.confirm({
      title: t('posAdmin.scheduleAttendance.confirm.deleteHoliday'),
      okText: t('common.delete'),
      cancelText: t('common.cancel'),
      okButtonProps: { danger: true },
      onOk: async () => {
        await deleteAttendanceHoliday(record.holidayGuid)
        message.success(t('message.deleteSuccess'))
        void loadHolidays()
      },
    })
  }

  const openReviewModal = (record: AttendanceApprovalDto, action: ReviewAction) => {
    if (!access.canReviewAttendance || !canEditStoreCode(record.storeCode)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    setReviewTarget(record)
    setReviewAction(action)
    reviewForm.resetFields()
    setReviewModalOpen(true)
  }

  const submitReview = async () => {
    if (!reviewTarget) return
    if (!access.canReviewAttendance || !canEditStoreCode(reviewTarget.storeCode)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    try {
      const values = await reviewForm.validateFields()
      setSaving(true)
      if (reviewAction === 'approve') {
        await approveAttendanceApproval(reviewTarget.approvalGuid, values)
      } else {
        await rejectAttendanceApproval(reviewTarget.approvalGuid, values)
      }
      message.success(t('posAdmin.scheduleAttendance.messages.reviewSuccess'))
      setReviewModalOpen(false)
      void loadApprovals()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.reviewFailed'))
    } finally {
      setSaving(false)
    }
  }

  const saveSettings = async () => {
    try {
      const values = await settingsForm.validateFields()
      setSettingsLoading(true)
      const result = await updateAttendanceSettings(values)
      setSettings(result)
      settingsForm.setFieldsValue(result)
      message.success(t('message.saveSuccess'))
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.saveSettingsFailed'))
    } finally {
      setSettingsLoading(false)
    }
  }

  const userCell = (record: { userName?: string; userGuid?: string; applicantName?: string; applicantUserGuid?: string }) => (
    <Space direction="vertical" size={0}>
      <Typography.Text>{record.userName || record.applicantName || '--'}</Typography.Text>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>{record.userGuid || record.applicantUserGuid || '--'}</Typography.Text>
    </Space>
  )

  const handlePublishWeek = async () => {
    if (!storeCode) {
      message.warning(t('posAdmin.scheduleAttendance.messages.selectStoreBeforePublish'))
      return
    }
    if (!access.canEditAttendanceSchedule || !canEditStoreCode(storeCode)) {
      message.error(t('message.noPermission', '无权操作该数据'))
      return
    }

    setPublishing(true)
    try {
      await publishAttendanceScheduleWeek({
        storeCode,
        weekStartDate: weekStart.format('YYYY-MM-DD'),
      })
      message.success(t('posAdmin.scheduleAttendance.messages.publishWeekSuccess'))
      void loadSchedules()
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.publishWeekFailed'))
    } finally {
      setPublishing(false)
    }
  }

  const displayStoreUserName = (user: StoreUserListDto) => user.fullName || user.username || user.userGuid

  const holidaysByDate = useMemo(() => {
    const map = new Map<string, AttendanceStoreHolidayDto[]>()
    scheduleHolidays.forEach((holiday) => {
      const dateKey = dayjs(holiday.holidayDate).format('YYYY-MM-DD')
      map.set(dateKey, [...(map.get(dateKey) ?? []), holiday])
    })
    return map
  }, [scheduleHolidays])

  const scheduleRows = useMemo<ScheduleRow[]>(() => {
    const rows = new Map<string, ScheduleRow>()
    storeUsers.forEach((user) => {
      rows.set(user.userGuid, {
        rowKey: `${user.storeCode || storeCode || ''}-${user.userGuid}`,
        storeCode: user.storeCode || storeCode || '',
        storeName: user.storeName || storeNameMap.get(user.storeCode || storeCode || ''),
        userGuid: user.userGuid,
        userName: displayStoreUserName(user),
        schedulesByDate: {},
      })
    })

    schedules.items.forEach((schedule) => {
      if (storeCode && schedule.storeCode !== storeCode) return
      if (userGuid?.trim() && !schedule.userGuid.toLowerCase().includes(userGuid.trim().toLowerCase()) && !schedule.userName?.toLowerCase().includes(userGuid.trim().toLowerCase())) return

      const existing = rows.get(schedule.userGuid) ?? {
        rowKey: `${schedule.storeCode}-${schedule.userGuid}`,
        storeCode: schedule.storeCode,
        storeName: schedule.storeName || storeNameMap.get(schedule.storeCode),
        userGuid: schedule.userGuid,
        userName: schedule.userName,
        schedulesByDate: {},
      }
      const dateKey = dayjs(schedule.workDate).format('YYYY-MM-DD')
      existing.schedulesByDate[dateKey] = [...(existing.schedulesByDate[dateKey] ?? []), schedule]
      rows.set(schedule.userGuid, existing)
    })

    return Array.from(rows.values())
  }, [schedules.items, storeCode, storeNameMap, storeUsers, userGuid])

  const openCreateScheduleForCell = (row: ScheduleRow, date: Dayjs) => {
    if (!access.canEditAttendanceSchedule || !canEditStoreCode(row.storeCode || storeCode)) return
    openScheduleDrawer(undefined, {
      storeCode: row.storeCode || storeCode,
      userGuid: row.userGuid,
      workDate: date,
      status: 'Draft',
    })
  }

  const renderScheduleCell = (row: ScheduleRow, date: Dayjs) => {
    const dateKey = date.format('YYYY-MM-DD')
    const daySchedules = [...(row.schedulesByDate[dateKey] ?? [])].sort((left, right) => left.startTime.localeCompare(right.startTime))
    const canEditRowStore = access.canEditAttendanceSchedule && canEditStoreCode(row.storeCode || storeCode)

    if (!daySchedules.length) {
      return (
        <button
          type="button"
          disabled={!canEditRowStore}
          onClick={() => openCreateScheduleForCell(row, date)}
          style={{
            width: '100%',
            minHeight: 58,
            border: '1px dashed #d9d9d9',
            borderRadius: 6,
            background: '#fafafa',
            color: '#8c8c8c',
            cursor: canEditRowStore ? 'pointer' : 'default',
          }}
        >
          {canEditRowStore ? t('posAdmin.scheduleAttendance.actions.addShift') : t('posAdmin.scheduleAttendance.emptyShift')}
        </button>
      )
    }

    return (
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        {daySchedules.map((schedule) => {
          const canEditSchedule = access.canEditAttendanceSchedule && canEditStoreCode(schedule.storeCode)
          return (
            <button
              key={schedule.scheduleGuid}
              type="button"
              onClick={() => openScheduleDrawer(schedule)}
              style={{
                width: '100%',
                border: '1px solid #91caff',
                borderRadius: 6,
                background: schedule.status === 'Draft' ? '#fffbe6' : schedule.status === 'Cancelled' ? '#fff1f0' : '#e6f4ff',
                padding: '6px 8px',
                textAlign: 'left',
                cursor: canEditSchedule ? 'pointer' : 'default',
              }}
              disabled={!canEditSchedule}
            >
              <Space direction="vertical" size={2}>
                <Typography.Text strong style={{ fontSize: 12 }}>{formatTime(schedule.startTime)} - {formatTime(schedule.endTime)}</Typography.Text>
                {scheduleStatusTag(schedule.status)}
                {schedule.remark ? <Typography.Text type="secondary" style={{ fontSize: 12 }}>{schedule.remark}</Typography.Text> : null}
              </Space>
            </button>
          )
        })}
      </Space>
    )
  }

  const renderHolidayHeaderTag = (holiday: AttendanceStoreHolidayDto) => {
    const color = holiday.businessStatus === 'Closed' ? 'red' : holiday.businessStatus === 'Partial' ? 'orange' : 'green'
    const status = t(`posAdmin.scheduleAttendance.status.holiday.${holiday.businessStatus}`, holiday.businessStatus)
    return (
      <Tooltip
        key={holiday.holidayGuid}
        title={`${t('posAdmin.scheduleAttendance.weekTable.holidayColumn')}: ${holiday.holidayName} (${status})`}
      >
        <Tag color={color} style={{ marginInlineEnd: 0, maxWidth: 130, overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {holiday.holidayName}
        </Tag>
      </Tooltip>
    )
  }

  const renderWeekDayHeader = (day: Dayjs, index: number) => {
    const dateKey = day.format('YYYY-MM-DD')
    const dayHolidays = holidaysByDate.get(dateKey) ?? []
    return (
      <Space direction="vertical" size={2} style={{ width: '100%' }}>
        <Typography.Text strong>{day.format('MM-DD')}</Typography.Text>
        <Typography.Text type="secondary">{t(`posAdmin.scheduleAttendance.weekdays.${index}`)}</Typography.Text>
        {dayHolidays.length ? (
          <Space size={[4, 2]} wrap>
            {dayHolidays.map(renderHolidayHeaderTag)}
          </Space>
        ) : null}
      </Space>
    )
  }

  const scheduleColumns: ColumnsType<ScheduleRow> = [
    {
      title: t('posAdmin.scheduleAttendance.fields.employee'),
      key: 'employee',
      fixed: 'left',
      width: 240,
      render: (_, record) => (
        <Space direction="vertical" size={0} style={{ width: '100%' }}>
          <Typography.Text strong>{record.userName || '--'}</Typography.Text>
        </Space>
      ),
    },
    ...weekDays.map((day, index): ColumnsType<ScheduleRow>[number] => ({
      title: renderWeekDayHeader(day, index),
      key: day.format('YYYY-MM-DD'),
      width: 170,
      render: (_, record) => renderScheduleCell(record, day),
    })),
  ]

  const availabilityColumns: ColumnsType<AttendanceAvailabilityDto> = [
    { title: t('posAdmin.scheduleAttendance.fields.store'), dataIndex: 'storeCode', width: 150, render: (value: string, record) => record.storeName || storeNameMap.get(value) || value },
    { title: t('posAdmin.scheduleAttendance.fields.employee'), key: 'user', width: 220, render: (_, record) => userCell(record) },
    { title: t('posAdmin.scheduleAttendance.fields.weekStartDate'), dataIndex: 'weekStartDate', width: 130, render: formatDate },
    { title: t('posAdmin.scheduleAttendance.fields.availableDate'), dataIndex: 'availableDate', width: 130, render: formatDate },
    { title: t('posAdmin.scheduleAttendance.fields.availableTime'), key: 'time', width: 150, render: (_, record) => `${formatTime(record.startTime)} - ${formatTime(record.endTime)}` },
    { title: t('common.status'), dataIndex: 'status', width: 120, render: scheduleStatusTag },
    { title: t('column.remarks'), dataIndex: 'remark', ellipsis: true },
  ]

  const openPunchMap = (record: AttendancePunchDto) => {
    if (typeof record.locationLatitude !== 'number' || typeof record.locationLongitude !== 'number') {
      message.info(t('posAdmin.scheduleAttendance.messages.locationNotRecorded'))
      return
    }

    setMapPreview({
      title: `${record.userName || record.userGuid} / ${t(`posAdmin.scheduleAttendance.status.punchType.${record.punchType}`, record.punchType)}`,
      latitude: record.locationLatitude,
      longitude: record.locationLongitude,
      accuracy: record.locationAccuracy,
      capturedAt: record.locationCapturedAtUtc || record.punchTimeUtc || record.punchTimeLocal,
    })
  }

  const openLocationSamples = async (record: AttendancePunchDto) => {
    setSampleTitle(`${record.userName || record.userGuid} / ${formatDate(record.workDate)}`)
    setSampleRows([])
    setSampleDrawerOpen(true)
    setSampleLoading(true)
    try {
      const rows = await getAttendanceLocationSamples({
        storeCode: record.storeCode,
        userGuid: record.userGuid,
        fromDate: dayjs(record.workDate).format('YYYY-MM-DD'),
        toDate: dayjs(record.workDate).format('YYYY-MM-DD'),
        storeTimeZone: record.storeTimeZone,
      })
      setSampleRows(rows)
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadLocationSamplesFailed'))
      setSampleRows([])
    } finally {
      setSampleLoading(false)
    }
  }

  const locationSampleColumns: ColumnsType<AttendanceLocationSampleDto> = [
    { title: t('posAdmin.scheduleAttendance.fields.locationCapturedAt'), dataIndex: 'locationCapturedAtUtc', width: 180, render: formatDateTime },
    { title: t('posAdmin.scheduleAttendance.fields.location'), key: 'location', width: 190, render: (_, record) => `${record.locationLatitude.toFixed(6)}, ${record.locationLongitude.toFixed(6)}` },
    { title: t('posAdmin.scheduleAttendance.fields.locationAccuracy'), dataIndex: 'locationAccuracy', width: 120, render: (value?: number) => typeof value === 'number' ? `${Math.round(value)}m` : '--' },
    { title: t('posAdmin.scheduleAttendance.fields.deviceSystem'), dataIndex: 'deviceSystem', width: 120 },
    {
      title: t('column.action'),
      key: 'action',
      width: 120,
      render: (_, record) => (
        <Button
          type="link"
          size="small"
          icon={<EnvironmentOutlined />}
          onClick={() => setMapPreview({
            title: `${sampleTitle} / ${formatDateTime(record.locationCapturedAtUtc)}`,
            latitude: record.locationLatitude,
            longitude: record.locationLongitude,
            accuracy: record.locationAccuracy,
            capturedAt: record.locationCapturedAtUtc,
          })}
        >
          {t('posAdmin.scheduleAttendance.actions.viewMap')}
        </Button>
      ),
    },
  ]

  const punchColumns: ColumnsType<AttendancePunchDto> = [
    { title: t('posAdmin.scheduleAttendance.fields.store'), dataIndex: 'storeCode', width: 150, render: (value: string, record) => record.storeName || storeNameMap.get(value) || value },
    { title: t('posAdmin.scheduleAttendance.fields.employee'), key: 'user', width: 220, render: (_, record) => userCell(record) },
    { title: t('posAdmin.scheduleAttendance.fields.workDate'), dataIndex: 'workDate', width: 130, render: formatDate },
    { title: t('posAdmin.scheduleAttendance.fields.punchType'), dataIndex: 'punchType', width: 120, render: (value: string) => t(`posAdmin.scheduleAttendance.status.punchType.${value}`, value) },
    { title: t('posAdmin.scheduleAttendance.fields.punchTimeLocal'), dataIndex: 'punchTimeLocal', width: 170, render: formatDateTime },
    { title: t('posAdmin.scheduleAttendance.fields.timeZone'), dataIndex: 'storeTimeZone', width: 180 },
    { title: t('common.status'), dataIndex: 'status', width: 150, render: punchStatusTag },
    {
      title: t('posAdmin.scheduleAttendance.fields.location'),
      key: 'location',
      width: 140,
      render: (_, record) => (
        typeof record.locationLatitude === 'number' && typeof record.locationLongitude === 'number' ? (
          <Button type="link" size="small" icon={<EnvironmentOutlined />} onClick={() => openPunchMap(record)}>
            {t('posAdmin.scheduleAttendance.actions.viewMap')}
          </Button>
        ) : (
          <Typography.Text type="secondary">{t('posAdmin.scheduleAttendance.messages.locationNotRecorded')}</Typography.Text>
        )
      ),
    },
    {
      title: t('posAdmin.scheduleAttendance.fields.locationSamples'),
      key: 'locationSamples',
      width: 130,
      render: (_, record) => (
        <Button type="link" size="small" onClick={() => void openLocationSamples(record)}>
          {t('posAdmin.scheduleAttendance.actions.viewSamples')}
        </Button>
      ),
    },
    { title: t('column.remarks'), dataIndex: 'remark', ellipsis: true },
  ]

  const approvalColumns: ColumnsType<AttendanceApprovalDto> = [
    { title: t('posAdmin.scheduleAttendance.fields.store'), dataIndex: 'storeCode', width: 150, render: (value: string, record) => record.storeName || storeNameMap.get(value) || value },
    { title: t('posAdmin.scheduleAttendance.fields.applicant'), key: 'applicant', width: 220, render: (_, record) => userCell(record) },
    { title: t('posAdmin.scheduleAttendance.fields.sourceType'), dataIndex: 'sourceType', width: 120, render: (value: string) => t(`posAdmin.scheduleAttendance.status.sourceType.${value}`, value) },
    { title: t('common.status'), dataIndex: 'reviewStatus', width: 130, render: reviewStatusTag },
    { title: t('posAdmin.scheduleAttendance.fields.reviewedAt'), dataIndex: 'reviewedAt', width: 170, render: formatDateTime },
    { title: t('column.remarks'), dataIndex: 'reviewRemark', ellipsis: true },
    {
      title: t('column.action'),
      key: 'action',
      width: 150,
      render: (_, record) => access.canReviewAttendance && record.reviewStatus === 'Pending' && canEditStoreCode(record.storeCode) ? (
        <Space>
          <Button type="link" size="small" icon={<CheckOutlined />} onClick={() => openReviewModal(record, 'approve')}>{t('posAdmin.scheduleAttendance.actions.approve')}</Button>
          <Button type="link" danger size="small" icon={<CloseOutlined />} onClick={() => openReviewModal(record, 'reject')}>{t('posAdmin.scheduleAttendance.actions.reject')}</Button>
        </Space>
      ) : null,
    },
  ]

  const holidayColumns: ColumnsType<AttendanceStoreHolidayDto> = [
    { title: t('posAdmin.scheduleAttendance.fields.store'), dataIndex: 'storeCode', width: 150, render: (value: string, record) => record.storeName || storeNameMap.get(value) || value },
    { title: t('posAdmin.scheduleAttendance.fields.holidayDate'), dataIndex: 'holidayDate', width: 130, render: formatDate },
    { title: t('posAdmin.scheduleAttendance.fields.holidayName'), dataIndex: 'holidayName', width: 180 },
    { title: t('posAdmin.scheduleAttendance.fields.businessStatus'), dataIndex: 'businessStatus', width: 130, render: holidayStatusTag },
    { title: t('posAdmin.scheduleAttendance.fields.businessTime'), key: 'businessTime', width: 150, render: (_, record) => `${formatTime(record.openTime)} - ${formatTime(record.closeTime)}` },
    { title: t('posAdmin.scheduleAttendance.fields.paidHoliday'), dataIndex: 'isPaidHoliday', width: 110, render: (value: boolean) => value ? t('common.yes') : t('common.no') },
    { title: t('column.remarks'), dataIndex: 'remark', ellipsis: true },
    {
      title: t('column.action'),
      key: 'action',
      width: 150,
      render: (_, record) => access.canEditAttendanceHoliday && canEditStoreCode(record.storeCode) ? (
        <Space>
          <Button type="link" size="small" icon={<EditOutlined />} onClick={() => openHolidayDrawer(record)}>{t('common.edit')}</Button>
          <Button type="link" size="small" danger icon={<DeleteOutlined />} onClick={() => confirmDeleteHoliday(record)}>{t('common.delete')}</Button>
        </Space>
      ) : null,
    },
  ]

  const pagination = (state: ListState<unknown>, loader: (page: number, pageSize: number) => void) => ({
    current: state.page,
    pageSize: state.pageSize,
    total: state.total,
    showSizeChanger: true,
    showTotal: (total: number) => t('common.total', { count: total }),
    onChange: loader,
  })

  const filterBar = (
    <Space wrap>
      <Select
        allowClear
        showSearch
        optionFilterProp="label"
        placeholder={t('posAdmin.scheduleAttendance.filters.store')}
        style={{ width: 180 }}
        value={storeCode}
        options={storeOptions}
        onChange={setStoreCode}
      />
      <Input
        allowClear
        placeholder={t('posAdmin.scheduleAttendance.filters.userGuid')}
        style={{ width: 220 }}
        value={userGuid}
        onChange={(event) => setUserGuid(event.target.value)}
      />
      {activeTab === 'schedules' ? (
        <>
          <DatePicker
            picker="week"
            allowClear={false}
            placeholder={t('posAdmin.scheduleAttendance.weekTable.selectWeek')}
            value={selectedWeek}
            onChange={(value) => setSelectedWeek(value ?? dayjs())}
          />
          <Tag color="blue" style={{ lineHeight: '30px', paddingInline: 12 }}>{selectedWeekLabel}</Tag>
        </>
      ) : (
        <DatePicker.RangePicker
          value={dateRange}
          onChange={(value) => setDateRange(value as [Dayjs, Dayjs] | null)}
        />
      )}
      <Button icon={<ReloadOutlined />} onClick={() => loadActiveTab(1)}>{t('common.search')}</Button>
    </Space>
  )

  const tabItems = [
    {
      key: 'schedules',
      label: t('posAdmin.scheduleAttendance.tabs.schedules'),
      children: (
        <Table
          rowKey="rowKey"
          loading={schedules.loading || storeUsersLoading || scheduleHolidaysLoading}
          columns={scheduleColumns}
          dataSource={storeCode ? scheduleRows : []}
          pagination={false}
          locale={{ emptyText: storeCode ? t('posAdmin.scheduleAttendance.emptyEmployees') : t('posAdmin.scheduleAttendance.selectStoreToSchedule') }}
          scroll={{ x: 1430 }}
        />
      ),
    },
    {
      key: 'availability',
      label: t('posAdmin.scheduleAttendance.tabs.availability'),
      children: (
        <Table
          rowKey="availabilityGuid"
          loading={availability.loading}
          columns={availabilityColumns}
          dataSource={availability.items}
          pagination={pagination(availability, loadAvailability)}
          scroll={{ x: 1000 }}
        />
      ),
    },
    {
      key: 'punches',
      label: t('posAdmin.scheduleAttendance.tabs.punches'),
      children: (
        <Table
          rowKey="punchGuid"
          loading={punches.loading}
          columns={punchColumns}
          dataSource={punches.items}
          pagination={pagination(punches, loadPunches)}
          scroll={{ x: 1350 }}
        />
      ),
    },
    {
      key: 'approvals',
      label: t('posAdmin.scheduleAttendance.tabs.approvals'),
      children: (
        <Table
          rowKey="approvalGuid"
          loading={approvals.loading}
          columns={approvalColumns}
          dataSource={approvals.items}
          pagination={pagination(approvals, loadApprovals)}
          scroll={{ x: 1000 }}
        />
      ),
    },
    {
      key: 'holidays',
      label: t('posAdmin.scheduleAttendance.tabs.holidays'),
      children: (
        <Table
          rowKey="holidayGuid"
          loading={holidays.loading}
          columns={holidayColumns}
          dataSource={holidays.items}
          pagination={pagination(holidays, loadHolidays)}
          scroll={{ x: 1100 }}
        />
      ),
    },
    {
      key: 'settings',
      label: t('posAdmin.scheduleAttendance.tabs.settings'),
      children: (
        <Form form={settingsForm} layout="vertical" disabled={!access.canEditAttendanceSettings || settingsLoading} style={{ maxWidth: 720 }}>
          <Form.Item name="lateGraceMinutes" label={t('posAdmin.scheduleAttendance.fields.lateGraceMinutes')} rules={[{ required: true }]}>
            <InputNumber min={0} max={120} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="earlyLeaveGraceMinutes" label={t('posAdmin.scheduleAttendance.fields.earlyLeaveGraceMinutes')} rules={[{ required: true }]}>
            <InputNumber min={0} max={120} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="allowNoSchedulePunch" label={t('posAdmin.scheduleAttendance.fields.allowNoSchedulePunch')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="requireApprovalForLate" label={t('posAdmin.scheduleAttendance.fields.requireApprovalForLate')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="requireApprovalForEarlyLeave" label={t('posAdmin.scheduleAttendance.fields.requireApprovalForEarlyLeave')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="requireApprovalForNoSchedule" label={t('posAdmin.scheduleAttendance.fields.requireApprovalForNoSchedule')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Space>
            <Button icon={<ReloadOutlined />} loading={settingsLoading} onClick={() => void loadSettings()}>{t('common.refresh')}</Button>
            {access.canEditAttendanceSettings ? (
              <Button type="primary" icon={<SaveOutlined />} loading={settingsLoading} onClick={() => void saveSettings()}>{t('common.save')}</Button>
            ) : null}
            {settings?.updatedAt ? <Typography.Text type="secondary">{t('posAdmin.scheduleAttendance.fields.updatedAt')}: {formatDateTime(settings.updatedAt)}</Typography.Text> : null}
          </Space>
        </Form>
      ),
    },
  ]

  return (
    <Card
      title={<Space><CalendarOutlined />{t('posAdmin.scheduleAttendance.title')}</Space>}
      extra={
        <Space wrap>
          {filterBar}
          {activeTab === 'schedules' && access.canEditAttendanceSchedule ? (
            <>
              <Tooltip title={!storeCode ? t('posAdmin.scheduleAttendance.messages.selectStoreBeforePublish') : !canEditSelectedStore ? t('message.noPermission', '无权操作该数据') : undefined}>
                <Button
                  icon={<CheckOutlined />}
                  disabled={!storeCode || !canEditSelectedStore}
                  loading={publishing}
                  onClick={() => void handlePublishWeek()}
                >
                  {t('posAdmin.scheduleAttendance.actions.publishWeek')}
                </Button>
              </Tooltip>
              <Button type="primary" icon={<PlusOutlined />} disabled={!canEditSelectedStore} onClick={() => openScheduleDrawer()}>{t('posAdmin.scheduleAttendance.actions.createSchedule')}</Button>
            </>
          ) : null}
          {activeTab === 'holidays' && access.canEditAttendanceHoliday ? (
            <>
              <Tooltip title={!storeCode ? t('posAdmin.scheduleAttendance.messages.selectStoreBeforeHolidaySync') : !canEditSelectedStore ? t('message.noPermission', '无权操作该数据') : undefined}>
                <Button
                  icon={<ReloadOutlined />}
                  disabled={!storeCode || !canEditSelectedStore}
                  loading={syncingHolidays}
                  onClick={() => void syncFutureHolidays()}
                >
                  {t('posAdmin.scheduleAttendance.actions.syncFutureHolidays')}
                </Button>
              </Tooltip>
              <Tooltip title={t('posAdmin.scheduleAttendance.messages.batchHolidayOverwriteHint')}>
                <Button
                  icon={<CopyOutlined />}
                  disabled={!editableStoreOptions.length}
                  onClick={() => openBatchHolidayDrawer()}
                >
                  {t('posAdmin.scheduleAttendance.actions.batchHoliday')}
                </Button>
              </Tooltip>
              <Button type="primary" icon={<PlusOutlined />} disabled={Boolean(storeCode && !canEditSelectedStore)} onClick={() => openHolidayDrawer()}>{t('posAdmin.scheduleAttendance.actions.createHoliday')}</Button>
            </>
          ) : null}
        </Space>
      }
    >
      <Tabs activeKey={activeTab} onChange={(key) => setActiveTab(key as TabKey)} items={tabItems} />

      <Drawer
        title={editingSchedule ? t('posAdmin.scheduleAttendance.drawer.editSchedule') : t('posAdmin.scheduleAttendance.drawer.createSchedule')}
        width={520}
        open={scheduleDrawerOpen}
        onClose={() => setScheduleDrawerOpen(false)}
        extra={(
          <Space>
            {editingSchedule ? (
              <Button danger icon={<DeleteOutlined />} onClick={() => confirmDeleteSchedule(editingSchedule)}>{t('common.delete')}</Button>
            ) : null}
            <Button type="primary" loading={saving} onClick={() => void saveSchedule()}>{t('common.save')}</Button>
          </Space>
        )}
      >
        <Form form={scheduleForm} layout="vertical">
          <Form.Item name="storeCode" label={t('posAdmin.scheduleAttendance.fields.store')} rules={[{ required: true, message: t('form.pleaseSelectStore') }]}>
            <Select showSearch optionFilterProp="label" options={editableStoreOptions} />
          </Form.Item>
          <Form.Item name="userGuid" label={t('posAdmin.scheduleAttendance.fields.employeeGuid')} rules={[{ required: true, message: t('posAdmin.scheduleAttendance.validation.userGuidRequired') }]}>
            <Select
              showSearch
              optionFilterProp="label"
              options={storeUsers.map((user) => ({
                value: user.userGuid,
                label: `${displayStoreUserName(user)} / ${user.userGuid}`,
              }))}
            />
          </Form.Item>
          <Form.Item name="workDate" label={t('posAdmin.scheduleAttendance.fields.workDate')} rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="timeRange" label={t('posAdmin.scheduleAttendance.fields.shift')} rules={[{ required: true }]}>
            <TimePicker.RangePicker format="HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          {editingSchedule ? (
            <Form.Item name="status" label={t('common.status')} rules={[{ required: true }]}>
              <Select options={[
                { value: 'Draft', label: statusLabel('schedule', 'Draft') },
                { value: 'Active', label: statusLabel('schedule', 'Active') },
                { value: 'Cancelled', label: statusLabel('schedule', 'Cancelled') },
              ]} />
            </Form.Item>
          ) : null}
          <Form.Item name="remark" label={t('column.remarks')}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Drawer>

      <Drawer
        title={editingHoliday ? t('posAdmin.scheduleAttendance.drawer.editHoliday') : t('posAdmin.scheduleAttendance.drawer.createHoliday')}
        width={520}
        open={holidayDrawerOpen}
        onClose={() => setHolidayDrawerOpen(false)}
        extra={<Button type="primary" loading={saving} onClick={() => void saveHoliday()}>{t('common.save')}</Button>}
      >
        <Form form={holidayForm} layout="vertical">
          {editingHoliday ? (
            <Form.Item name="storeCode" label={t('posAdmin.scheduleAttendance.fields.store')} rules={[{ required: true, message: t('form.pleaseSelectStore') }]}>
              <Select showSearch optionFilterProp="label" options={editableStoreOptions} />
            </Form.Item>
          ) : (
            <Form.Item
              name="storeCodes"
              label={t('posAdmin.scheduleAttendance.fields.stores')}
              rules={[{ required: true, message: t('posAdmin.scheduleAttendance.validation.storeCodesRequired') }]}
            >
              <Select
                mode="multiple"
                showSearch
                optionFilterProp="label"
                maxTagCount="responsive"
                options={editableStoreOptions}
              />
            </Form.Item>
          )}
          <Form.Item name="holidayDate" label={t('posAdmin.scheduleAttendance.fields.holidayDate')} rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="holidayName" label={t('posAdmin.scheduleAttendance.fields.holidayName')} rules={[{ required: true, message: t('posAdmin.scheduleAttendance.validation.holidayNameRequired') }]}>
            <Input />
          </Form.Item>
          <Form.Item name="businessStatus" label={t('posAdmin.scheduleAttendance.fields.businessStatus')} rules={[{ required: true }]}>
            <Select options={[
              { value: 'Open', label: t('posAdmin.scheduleAttendance.status.holiday.Open') },
              { value: 'Closed', label: t('posAdmin.scheduleAttendance.status.holiday.Closed') },
              { value: 'Partial', label: t('posAdmin.scheduleAttendance.status.holiday.Partial') },
            ]} />
          </Form.Item>
          <Form.Item name="businessTime" label={t('posAdmin.scheduleAttendance.fields.businessTime')}>
            <TimePicker.RangePicker format="HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="isPaidHoliday" label={t('posAdmin.scheduleAttendance.fields.paidHoliday')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="remark" label={t('column.remarks')}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Drawer>

      <Drawer
        title={t('posAdmin.scheduleAttendance.drawer.batchHoliday')}
        width={560}
        open={batchHolidayDrawerOpen}
        onClose={() => setBatchHolidayDrawerOpen(false)}
        extra={<Button type="primary" loading={saving} onClick={() => void saveBatchHoliday()}>{t('common.save')}</Button>}
      >
        <Form form={batchHolidayForm} layout="vertical">
          <Form.Item
            name="storeCodes"
            label={t('posAdmin.scheduleAttendance.fields.stores')}
            rules={[{ required: true, message: t('posAdmin.scheduleAttendance.validation.storeCodesRequired') }]}
          >
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              maxTagCount="responsive"
              options={editableStoreOptions}
            />
          </Form.Item>
          <Form.Item name="holidayDate" label={t('posAdmin.scheduleAttendance.fields.holidayDate')} rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="holidayName" label={t('posAdmin.scheduleAttendance.fields.holidayName')} rules={[{ required: true, message: t('posAdmin.scheduleAttendance.validation.holidayNameRequired') }]}>
            <Input />
          </Form.Item>
          <Form.Item name="businessStatus" label={t('posAdmin.scheduleAttendance.fields.businessStatus')} rules={[{ required: true }]}>
            <Select options={[
              { value: 'Open', label: t('posAdmin.scheduleAttendance.status.holiday.Open') },
              { value: 'Closed', label: t('posAdmin.scheduleAttendance.status.holiday.Closed') },
              { value: 'Partial', label: t('posAdmin.scheduleAttendance.status.holiday.Partial') },
            ]} />
          </Form.Item>
          <Form.Item name="businessTime" label={t('posAdmin.scheduleAttendance.fields.businessTime')}>
            <TimePicker.RangePicker format="HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="isPaidHoliday" label={t('posAdmin.scheduleAttendance.fields.paidHoliday')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="remark" label={t('column.remarks')}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Drawer>

      <Drawer
        title={sampleTitle ? `${t('posAdmin.scheduleAttendance.drawer.locationSamples')} - ${sampleTitle}` : t('posAdmin.scheduleAttendance.drawer.locationSamples')}
        width={720}
        open={sampleDrawerOpen}
        onClose={() => setSampleDrawerOpen(false)}
      >
        <Table
          rowKey="sampleGuid"
          size="small"
          loading={sampleLoading}
          columns={locationSampleColumns}
          dataSource={sampleRows}
          pagination={false}
          locale={{ emptyText: t('posAdmin.scheduleAttendance.messages.locationSamplesEmpty') }}
          scroll={{ x: 700 }}
        />
      </Drawer>

      <Modal
        title={mapPreview?.title || t('posAdmin.scheduleAttendance.drawer.locationMap')}
        open={Boolean(mapPreview)}
        footer={null}
        width={760}
        onCancel={() => setMapPreview(null)}
      >
        {mapPreview ? (
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <Space size={[12, 4]} wrap>
              <Typography.Text>
                {t('posAdmin.scheduleAttendance.fields.location')}: {mapPreview.latitude.toFixed(6)}, {mapPreview.longitude.toFixed(6)}
              </Typography.Text>
              {typeof mapPreview.accuracy === 'number' ? (
                <Typography.Text type="secondary">
                  {t('posAdmin.scheduleAttendance.fields.locationAccuracy')}: {Math.round(mapPreview.accuracy)}m
                </Typography.Text>
              ) : null}
              {mapPreview.capturedAt ? (
                <Typography.Text type="secondary">
                  {t('posAdmin.scheduleAttendance.fields.locationCapturedAt')}: {formatDateTime(mapPreview.capturedAt)}
                </Typography.Text>
              ) : null}
            </Space>
            <Typography.Text type="secondary">
              {t('posAdmin.scheduleAttendance.messages.externalMapNotice')}
            </Typography.Text>
            <Button
              type="primary"
              icon={<EnvironmentOutlined />}
              onClick={() => window.open(externalMapUrl, '_blank', 'noopener,noreferrer')}
            >
              {t('posAdmin.scheduleAttendance.actions.openExternalMap')}
            </Button>
          </Space>
        ) : null}
      </Modal>

      <Modal
        title={reviewAction === 'approve' ? t('posAdmin.scheduleAttendance.drawer.approve') : t('posAdmin.scheduleAttendance.drawer.reject')}
        open={reviewModalOpen}
        okText={reviewAction === 'approve' ? t('posAdmin.scheduleAttendance.actions.approve') : t('posAdmin.scheduleAttendance.actions.reject')}
        cancelText={t('common.cancel')}
        okButtonProps={{ danger: reviewAction === 'reject', loading: saving }}
        onOk={() => void submitReview()}
        onCancel={() => setReviewModalOpen(false)}
      >
        <Form form={reviewForm} layout="vertical">
          <Form.Item name="reviewRemark" label={t('posAdmin.scheduleAttendance.fields.reviewRemark')}>
            <Input.TextArea rows={4} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  )
}
