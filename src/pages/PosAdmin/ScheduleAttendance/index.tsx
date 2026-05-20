import {
  CalendarOutlined,
  CheckOutlined,
  CloseOutlined,
  DeleteOutlined,
  EditOutlined,
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
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  approveAttendanceApproval,
  createAttendanceHoliday,
  createAttendanceSchedule,
  deleteAttendanceHoliday,
  deleteAttendanceSchedule,
  getAttendanceAvailability,
  getAttendanceHolidays,
  getAttendancePunches,
  getAttendanceScheduleWeek,
  getAttendanceSettings,
  getPendingAttendanceApprovals,
  publishAttendanceScheduleWeek,
  rejectAttendanceApproval,
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
  AttendancePunchDto,
  AttendancePunchStatus,
  AttendanceReviewStatus,
  AttendanceScheduleDto,
  AttendanceScheduleStatus,
  AttendanceSettingsDto,
  AttendanceStoreHolidayDto,
  SaveAttendanceHolidayPayload,
  SaveAttendanceSchedulePayload,
  SaveAttendanceSettingsPayload,
} from '../../../types/scheduleAttendance'

type TabKey = 'schedules' | 'availability' | 'punches' | 'approvals' | 'holidays' | 'settings'
type ReviewAction = 'approve' | 'reject'

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
  storeCode: string
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

export default function ScheduleAttendancePage() {
  const { t } = useTranslation()
  const access = useAuthStore((state) => state.access)
  const [activeTab, setActiveTab] = useState<TabKey>('schedules')
  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [storeCode, setStoreCode] = useState<string | undefined>()
  const [userGuid, setUserGuid] = useState<string | undefined>()
  const [dateRange, setDateRange] = useState<[Dayjs, Dayjs] | null>([dayjs().startOf('week').add(1, 'day'), dayjs().startOf('week').add(7, 'day')])
  const [schedules, setSchedules] = useState<ListState<AttendanceScheduleDto>>(initialListState)
  const [storeUsers, setStoreUsers] = useState<StoreUserListDto[]>([])
  const [storeUsersLoading, setStoreUsersLoading] = useState(false)
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
  const [reviewModalOpen, setReviewModalOpen] = useState(false)
  const [reviewTarget, setReviewTarget] = useState<AttendanceApprovalDto | null>(null)
  const [reviewAction, setReviewAction] = useState<ReviewAction>('approve')
  const [saving, setSaving] = useState(false)
  const [publishing, setPublishing] = useState(false)
  const [scheduleForm] = Form.useForm<ScheduleFormValues>()
  const [holidayForm] = Form.useForm<HolidayFormValues>()
  const [reviewForm] = Form.useForm<{ reviewRemark?: string }>()
  const [settingsForm] = Form.useForm<SaveAttendanceSettingsPayload>()

  const storeNameMap = useMemo(() => new Map(storeOptions.map((item) => [item.value, item.label])), [storeOptions])
  const weekStart = useMemo(() => (dateRange?.[0] ?? dayjs()).startOf('day'), [dateRange])
  const weekDays = useMemo(() => Array.from({ length: 7 }, (_, index) => weekStart.add(index, 'day')), [weekStart])

  const queryParams = useCallback((page: number, pageSize: number) => ({
    page,
    pageSize,
    storeCode,
    userGuid: userGuid?.trim() || undefined,
    weekStartDate: dateRange?.[0]?.format('YYYY-MM-DD'),
    fromDate: dateRange?.[0]?.format('YYYY-MM-DD'),
    toDate: dateRange?.[1]?.format('YYYY-MM-DD'),
  }), [dateRange, storeCode, userGuid])

  const loadSchedules = useCallback(async (page = schedules.page, pageSize = schedules.pageSize) => {
    setSchedules((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendanceScheduleWeek(queryParams(page, pageSize))
      setSchedules({ loading: false, items: result, total: result.length, page, pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadSchedulesFailed'))
      setSchedules((prev) => ({ ...prev, loading: false }))
    }
  }, [queryParams, schedules.page, schedules.pageSize, t])

  const loadStoreUsers = useCallback(async () => {
    if (!storeCode) {
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
  }, [storeCode, t, userGuid])

  const loadAvailability = useCallback(async (page = availability.page, pageSize = availability.pageSize) => {
    setAvailability((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendanceAvailability(queryParams(page, pageSize))
      setAvailability({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadAvailabilityFailed'))
      setAvailability((prev) => ({ ...prev, loading: false }))
    }
  }, [availability.page, availability.pageSize, queryParams, t])

  const loadPunches = useCallback(async (page = punches.page, pageSize = punches.pageSize) => {
    setPunches((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendancePunches(queryParams(page, pageSize))
      setPunches({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadPunchesFailed'))
      setPunches((prev) => ({ ...prev, loading: false }))
    }
  }, [punches.page, punches.pageSize, queryParams, t])

  const loadApprovals = useCallback(async (page = approvals.page, pageSize = approvals.pageSize) => {
    setApprovals((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getPendingAttendanceApprovals(queryParams(page, pageSize))
      setApprovals({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadApprovalsFailed'))
      setApprovals((prev) => ({ ...prev, loading: false }))
    }
  }, [approvals.page, approvals.pageSize, queryParams, t])

  const loadHolidays = useCallback(async (page = holidays.page, pageSize = holidays.pageSize) => {
    setHolidays((prev) => ({ ...prev, loading: true, page, pageSize }))
    try {
      const result = await getAttendanceHolidays(queryParams(page, pageSize))
      setHolidays({ loading: false, items: result.items, total: result.total, page: result.page, pageSize: result.pageSize })
    } catch (error) {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadHolidaysFailed'))
      setHolidays((prev) => ({ ...prev, loading: false }))
    }
  }, [holidays.page, holidays.pageSize, queryParams, t])

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
    }
    if (activeTab === 'availability') void loadAvailability(page)
    if (activeTab === 'punches') void loadPunches(page)
    if (activeTab === 'approvals') void loadApprovals(page)
    if (activeTab === 'holidays') void loadHolidays(page)
    if (activeTab === 'settings') void loadSettings()
  }, [activeTab, loadApprovals, loadAvailability, loadHolidays, loadPunches, loadSchedules, loadSettings, loadStoreUsers])

  useEffect(() => {
    void getActiveStores().then(setStoreOptions).catch((error) => {
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.loadStoresFailed'))
    })
  }, [t])

  useEffect(() => {
    loadActiveTab(1)
  }, [activeTab, storeCode, dateRange])

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
      holidayForm.setFieldsValue({ storeCode, businessStatus: 'Open', isPaidHoliday: true })
    }
    setHolidayDrawerOpen(true)
  }

  const saveHoliday = async () => {
    try {
      const values = await holidayForm.validateFields()
      const payload: SaveAttendanceHolidayPayload = {
        storeCode: values.storeCode,
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
        await createAttendanceHoliday(payload)
        message.success(t('message.createSuccess'))
      }
      setHolidayDrawerOpen(false)
      void loadHolidays()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      console.error(error)
      message.error(t('posAdmin.scheduleAttendance.messages.saveHolidayFailed'))
    } finally {
      setSaving(false)
    }
  }

  const confirmDeleteHoliday = (record: AttendanceStoreHolidayDto) => {
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
    setReviewTarget(record)
    setReviewAction(action)
    reviewForm.resetFields()
    setReviewModalOpen(true)
  }

  const submitReview = async () => {
    if (!reviewTarget) return
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
    if (!access.canEditAttendanceSchedule) return
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

    if (!daySchedules.length) {
      return (
        <button
          type="button"
          disabled={!access.canEditAttendanceSchedule}
          onClick={() => openCreateScheduleForCell(row, date)}
          style={{
            width: '100%',
            minHeight: 58,
            border: '1px dashed #d9d9d9',
            borderRadius: 6,
            background: '#fafafa',
            color: '#8c8c8c',
            cursor: access.canEditAttendanceSchedule ? 'pointer' : 'default',
          }}
        >
          {access.canEditAttendanceSchedule ? t('posAdmin.scheduleAttendance.actions.addShift') : t('posAdmin.scheduleAttendance.emptyShift')}
        </button>
      )
    }

    return (
      <Space direction="vertical" size={6} style={{ width: '100%' }}>
        {daySchedules.map((schedule) => (
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
              cursor: access.canEditAttendanceSchedule ? 'pointer' : 'default',
            }}
            disabled={!access.canEditAttendanceSchedule}
          >
            <Space direction="vertical" size={2}>
              <Typography.Text strong style={{ fontSize: 12 }}>{formatTime(schedule.startTime)} - {formatTime(schedule.endTime)}</Typography.Text>
              {scheduleStatusTag(schedule.status)}
              {schedule.remark ? <Typography.Text type="secondary" style={{ fontSize: 12 }}>{schedule.remark}</Typography.Text> : null}
            </Space>
          </button>
        ))}
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
        <Space direction="vertical" size={0}>
          <Typography.Text strong>{record.userName || '--'}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{record.userGuid}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{record.storeName || storeNameMap.get(record.storeCode) || record.storeCode}</Typography.Text>
        </Space>
      ),
    },
    ...weekDays.map((day, index): ColumnsType<ScheduleRow>[number] => ({
      title: (
        <Space direction="vertical" size={0}>
          <Typography.Text>{day.format('MM-DD')}</Typography.Text>
          <Typography.Text type="secondary">{t(`posAdmin.scheduleAttendance.weekdays.${index}`)}</Typography.Text>
        </Space>
      ),
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

  const punchColumns: ColumnsType<AttendancePunchDto> = [
    { title: t('posAdmin.scheduleAttendance.fields.store'), dataIndex: 'storeCode', width: 150, render: (value: string, record) => record.storeName || storeNameMap.get(value) || value },
    { title: t('posAdmin.scheduleAttendance.fields.employee'), key: 'user', width: 220, render: (_, record) => userCell(record) },
    { title: t('posAdmin.scheduleAttendance.fields.workDate'), dataIndex: 'workDate', width: 130, render: formatDate },
    { title: t('posAdmin.scheduleAttendance.fields.punchType'), dataIndex: 'punchType', width: 120, render: (value: string) => t(`posAdmin.scheduleAttendance.status.punchType.${value}`, value) },
    { title: t('posAdmin.scheduleAttendance.fields.punchTimeLocal'), dataIndex: 'punchTimeLocal', width: 170, render: formatDateTime },
    { title: t('posAdmin.scheduleAttendance.fields.timeZone'), dataIndex: 'storeTimeZone', width: 180 },
    { title: t('common.status'), dataIndex: 'status', width: 150, render: punchStatusTag },
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
      render: (_, record) => access.canReviewAttendance && record.reviewStatus === 'Pending' ? (
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
      render: (_, record) => access.canEditAttendanceHoliday ? (
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
      <DatePicker.RangePicker
        value={dateRange}
        onChange={(value) => setDateRange(value as [Dayjs, Dayjs] | null)}
      />
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
          loading={schedules.loading || storeUsersLoading}
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
          scroll={{ x: 1200 }}
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
              <Tooltip title={!storeCode ? t('posAdmin.scheduleAttendance.messages.selectStoreBeforePublish') : undefined}>
                <Button
                  icon={<CheckOutlined />}
                  disabled={!storeCode}
                  loading={publishing}
                  onClick={() => void handlePublishWeek()}
                >
                  {t('posAdmin.scheduleAttendance.actions.publishWeek')}
                </Button>
              </Tooltip>
              <Button type="primary" icon={<PlusOutlined />} onClick={() => openScheduleDrawer()}>{t('posAdmin.scheduleAttendance.actions.createSchedule')}</Button>
            </>
          ) : null}
          {activeTab === 'holidays' && access.canEditAttendanceHoliday ? (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => openHolidayDrawer()}>{t('posAdmin.scheduleAttendance.actions.createHoliday')}</Button>
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
            <Select showSearch optionFilterProp="label" options={storeOptions} />
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
          <Form.Item name="storeCode" label={t('posAdmin.scheduleAttendance.fields.store')} rules={[{ required: true, message: t('form.pleaseSelectStore') }]}>
            <Select showSearch optionFilterProp="label" options={storeOptions} />
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
