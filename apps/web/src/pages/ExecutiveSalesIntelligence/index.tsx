import {
  ExecutiveBranchPerformance,
  ExecutiveHourlyTraffic,
  getExecutiveBranchPerformance,
  getExecutiveHourlyTraffic,
  getWeeklyPerformanceHierarchy,
  WeeklyHierarchyData,
} from '../../services/salesDashboardService';
import { RightOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useAuthStore } from '../../store/auth';
import { DatePicker, message, Radio, Switch, Tag } from 'antd';
import dayjs, { Dayjs } from 'dayjs';
import isoWeek from 'dayjs/plugin/isoWeek';
import weekday from 'dayjs/plugin/weekday';
import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import BranchPerformanceTable from './components/BranchPerformanceTable';
import HourlyTrafficDensity from './components/HourlyTrafficDensity';
import WeeklyPerformanceCardHeader from './components/WeeklyPerformanceCardHeader';
import WeeklyPerformanceHierarchy from './components/WeeklyPerformanceHierarchy';
import styles from './styles.module.css';
dayjs.extend(isoWeek);
dayjs.extend(weekday);

const { RangePicker } = DatePicker;

type CompareMode = 'by-week' | 'by-date';

type QuickSelectOption =
  | 'today'
  | 'yesterday'
  | 'thisWeek'
  | 'lastWeek'
  | 'thisMonth'
  | 'lastMonth';

const quickSelectDateRanges: Record<
  QuickSelectOption,
  { start: Dayjs; end: Dayjs }
> = {
  today: { start: dayjs().startOf('day'), end: dayjs().endOf('day') },
  yesterday: {
    start: dayjs().subtract(1, 'day').startOf('day'),
    end: dayjs().subtract(1, 'day').endOf('day'),
  },
  thisWeek: { start: dayjs().startOf('week'), end: dayjs().endOf('week') },
  lastWeek: {
    start: dayjs().subtract(1, 'week').startOf('week'),
    end: dayjs().subtract(1, 'week').endOf('week'),
  },
  thisMonth: { start: dayjs().startOf('month'), end: dayjs().endOf('month') },
  lastMonth: {
    start: dayjs().subtract(1, 'month').startOf('month'),
    end: dayjs().subtract(1, 'month').endOf('month'),
  },
};

const quickSelectOptions: { key: QuickSelectOption; label: string }[] = [
  { key: 'today', label: 'Today' },
  { key: 'yesterday', label: 'Yesterday' },
  { key: 'thisWeek', label: 'This Week' },
  { key: 'lastWeek', label: 'Last Week' },
  { key: 'thisMonth', label: 'This Month' },
  { key: 'lastMonth', label: 'Last Month' },
];

interface DateRange {
  startDate: Dayjs;
  endDate: Dayjs;
  compareStartDate: Dayjs;
  compareEndDate: Dayjs;
  compareMode: CompareMode;
  isManualCompareDate?: boolean;
}

const presetRanges: Record<string, [Dayjs, Dayjs]> = {
  Today: [dayjs().startOf('day'), dayjs().endOf('day')],
  'This Week': [dayjs().startOf('week'), dayjs().endOf('week')],
  'Last Week': [
    dayjs().subtract(1, 'week').startOf('week'),
    dayjs().subtract(1, 'week').endOf('week'),
  ],
  'This Month': [dayjs().startOf('month'), dayjs().endOf('month')],
  'Last Month': [
    dayjs().subtract(1, 'month').startOf('month'),
    dayjs().subtract(1, 'month').endOf('month'),
  ],
};

const calculateCompareDates = (
  startDate: Dayjs,
  endDate: Dayjs,
  compareMode: CompareMode,
): { compareStartDate: Dayjs; compareEndDate: Dayjs } => {
  const dayDiff = endDate.diff(startDate, 'day');

  if (compareMode === 'by-date') {
    const compareStart = startDate.subtract(1, 'year');
    return {
      compareStartDate: compareStart,
      compareEndDate: compareStart.add(dayDiff, 'day'),
    };
  }

  const compareStart = dayjs()
    .year(startDate.year() - 1)
    .isoWeek(startDate.isoWeek())
    .isoWeekday(startDate.isoWeekday());
  return {
    compareStartDate: compareStart,
    compareEndDate: compareStart.add(dayDiff, 'day'),
  };
};

const ExecutiveSalesIntelligence: React.FC = () => {
  const { t } = useTranslation();
  const access = useAuthStore((state) => state.access);

  // 🔐 获取用户管辖的分店代码（Admin/WarehouseManager 返回 null，不过滤）
  const managedStoreCodes = access.managedStoreCodes?.() ?? undefined;
  // 使用 ref 保持稳定的引用，避免无限循环
  const managedStoreCodesRef = useRef(managedStoreCodes);
  useEffect(() => {
    managedStoreCodesRef.current = managedStoreCodes;
  }, [managedStoreCodes]);
  const [dateRange, setDateRange] = useState<DateRange>(() => {
    const today = dayjs();
    const compareDates = calculateCompareDates(
      today.startOf('day'),
      today.endOf('day'),
      'by-week',
    );
    return {
      startDate: today.startOf('day'),
      endDate: today.endOf('day'),
      compareStartDate: compareDates.compareStartDate,
      compareEndDate: compareDates.compareEndDate,
      compareMode: 'by-week',
      isManualCompareDate: false,
    };
  });
  const [branchData, setBranchData] = useState<ExecutiveBranchPerformance[]>(
    [],
  );
  const [hourlyData, setHourlyData] = useState<ExecutiveHourlyTraffic[]>([]);
  const [loading, setLoading] = useState(false);
  const [hourlyLoading, setHourlyLoading] = useState(false);
  const [selectedBranch, setSelectedBranch] = useState<{
    branchCode: string;
    branchName: string;
  } | null>(null);
  const [weeklyDateRange, setWeeklyDateRange] = useState<[Dayjs, Dayjs] | null>(
    () => [dayjs().startOf('month'), dayjs().endOf('month')],
  );
  const [weeklyData, setWeeklyData] = useState<WeeklyHierarchyData[]>([]);
  const [weeklyLoading, setWeeklyLoading] = useState(false);
  const [selectedQuickSelect, setSelectedQuickSelect] =
    useState<QuickSelectOption>('today');

  const dateRangeParams = useMemo(
    () => ({
      startDate: dateRange.startDate.format('YYYY-MM-DD'),
      endDate: dateRange.endDate.format('YYYY-MM-DD'),
      compareStartDate: dateRange.compareStartDate.format('YYYY-MM-DD'),
      compareEndDate: dateRange.compareEndDate.format('YYYY-MM-DD'),
    }),
    [dateRange],
  );

  const handleRangeChange = useCallback(
    (dates: [Dayjs | null, Dayjs | null] | null) => {
      if (dates && dates[0] && dates[1]) {
        if (dateRange.isManualCompareDate) {
          setDateRange({
            ...dateRange,
            startDate: dates[0],
            endDate: dates[1],
          });
        } else {
          const compareDates = calculateCompareDates(
            dates[0],
            dates[1],
            dateRange.compareMode,
          );
          setDateRange({
            startDate: dates[0],
            endDate: dates[1],
            compareStartDate: compareDates.compareStartDate,
            compareEndDate: compareDates.compareEndDate,
            compareMode: dateRange.compareMode,
            isManualCompareDate: false,
          });
        }
      }
    },
    [dateRange],
  );

  const handleCompareModeChange = useCallback(
    (mode: CompareMode) => {
      if (dateRange.isManualCompareDate) {
        setDateRange({
          ...dateRange,
          compareMode: mode,
        });
      } else {
        const compareDates = calculateCompareDates(
          dateRange.startDate,
          dateRange.endDate,
          mode,
        );
        setDateRange({
          ...dateRange,
          compareMode: mode,
          compareStartDate: compareDates.compareStartDate,
          compareEndDate: compareDates.compareEndDate,
        });
      }
    },
    [dateRange],
  );

  const handleManualCompareDateChange = useCallback(
    (dates: [Dayjs | null, Dayjs | null] | null) => {
      if (dates && dates[0] && dates[1]) {
        setDateRange({
          ...dateRange,
          compareStartDate: dates[0],
          compareEndDate: dates[1],
        });
      }
    },
    [dateRange],
  );

  const handleManualCompareModeChange = useCallback(
    (checked: boolean) => {
      if (checked) {
        setDateRange({
          ...dateRange,
          isManualCompareDate: true,
        });
      } else {
        const compareDates = calculateCompareDates(
          dateRange.startDate,
          dateRange.endDate,
          dateRange.compareMode,
        );
        setDateRange({
          ...dateRange,
          isManualCompareDate: false,
          compareStartDate: compareDates.compareStartDate,
          compareEndDate: compareDates.compareEndDate,
        });
      }
    },
    [dateRange],
  );

  const handleQuickSelectChange = useCallback((option: QuickSelectOption) => {
    setSelectedQuickSelect(option);
    const range = quickSelectDateRanges[option];
    const mode: CompareMode =
      option === 'thisMonth' || option === 'lastMonth' ? 'by-date' : 'by-week';
    const compareDates = calculateCompareDates(range.start, range.end, mode);
    setDateRange({
      startDate: range.start,
      endDate: range.end,
      compareStartDate: compareDates.compareStartDate,
      compareEndDate: compareDates.compareEndDate,
      compareMode: mode,
      isManualCompareDate: false,
    });
  }, []);

  const fetchBranchPerformance = useCallback(async () => {
    setLoading(true);
    try {
      const response = await getExecutiveBranchPerformance(
        dateRangeParams,
        100,
        managedStoreCodesRef.current,
      );

      if (response.success && response.data) {
        setBranchData(response.data);
      } else {
        message.error(
          response.message ||
            t('executiveSales.message.fetchBranchFailed'),
        );
      }
    } catch (error) {
      console.error('fetchBranchPerformance error:', error);
      message.error(
        t('executiveSales.message.fetchBranchFailed'),
      );
    } finally {
      setLoading(false);
    }
  }, [dateRangeParams, t]);

  const fetchHourlyTraffic = useCallback(async () => {
    setHourlyLoading(true);
    try {
      // 优先使用手动选择的分店，否则使用用户权限允许的分店
      const branchCodes = selectedBranch
        ? [selectedBranch.branchCode]
        : managedStoreCodesRef.current;
      const response = await getExecutiveHourlyTraffic(
        dateRangeParams,
        branchCodes,
      );

      if (response.success && response.data) {
        setHourlyData(response.data);
      } else {
        message.error(
          response.message ||
            t('executiveSales.message.fetchHourlyFailed'),
        );
      }
    } catch (error) {
      console.error('fetchHourlyTraffic error:', error);
      message.error(
        t('executiveSales.message.fetchHourlyFailed'),
      );
    } finally {
      setHourlyLoading(false);
    }
  }, [dateRangeParams, selectedBranch]);

  const fetchWeeklyHierarchy = useCallback(async () => {
    if (!weeklyDateRange || !weeklyDateRange[0] || !weeklyDateRange[1]) {
      return;
    }
    setWeeklyLoading(true);
    try {
      const response = await getWeeklyPerformanceHierarchy(
        {
          startDate: weeklyDateRange[0].format('YYYY-MM-DD'),
          endDate: weeklyDateRange[1].format('YYYY-MM-DD'),
        },
        managedStoreCodesRef.current,
      );

      if (response.success && response.data) {
        setWeeklyData(response.data);
      } else {
        message.error(
          response.message ||
            t('executiveSales.message.fetchWeeklyFailed'),
        );
      }
    } catch (error) {
      console.error('fetchWeeklyHierarchy error:', error);
      message.error(
        t('executiveSales.message.fetchWeeklyFailed'),
      );
    } finally {
      setWeeklyLoading(false);
    }
  }, [weeklyDateRange]);

  useEffect(() => {
    fetchBranchPerformance();
    fetchHourlyTraffic();
  }, [dateRangeParams]);

  useEffect(() => {
    fetchHourlyTraffic();
  }, [selectedBranch]);

  useEffect(() => {
    fetchWeeklyHierarchy();
  }, [weeklyDateRange]);

  const handleBranchSelect = (branchCode: string, branchName: string) => {
    setSelectedBranch({ branchCode, branchName });
  };

  const handleClearBranchSelection = () => {
    setSelectedBranch(null);
  };

  const transformedBranchData = branchData.map((item) => ({
    rank: item.rank,
    branchCode: item.branchCode,
    branchName: item.branchName,
    revenue: item.revenue,
    revenueLY: item.revenueLY,
    orderCount: item.orderCount,
    orderCountLY: item.orderCountLY,
    aov: item.aov,
    aovLY: item.aovLY,
  }));

  const transformedHourlyData = useMemo(() => {
    if (!hourlyData || hourlyData.length === 0) {
      return [];
    }

    if (selectedBranch) {
      const filteredData = hourlyData.filter(
        (item) =>
          item.branchCode?.trim().toLowerCase() ===
          selectedBranch.branchCode?.trim().toLowerCase(),
      );

      if (filteredData.length === 0 && hourlyData.length > 0) {
        console.warn('No data found for branch:', selectedBranch.branchCode);
        console.warn(
          'Available branchCodes:',
          hourlyData.map((h) => h.branchCode),
        );
      }

      return filteredData.map((item) => ({
        hour: item.hour,
        revenue: item.revenue,
        revenueLY: item.revenueLY,
        percentage: item.percentage,
        isPeak: item.isPeak,
      }));
    }

    const hourlyMap = new Map<
      string,
      {
        hour: string;
        revenue: number;
        revenueLY: number;
      }
    >();

    hourlyData.forEach((item) => {
      const existing = hourlyMap.get(item.hour);
      if (existing) {
        existing.revenue += item.revenue;
        existing.revenueLY += item.revenueLY;
      } else {
        hourlyMap.set(item.hour, {
          hour: item.hour,
          revenue: item.revenue,
          revenueLY: item.revenueLY,
        });
      }
    });

    const maxRevenue = Math.max(
      ...Array.from(hourlyMap.values()).map((h) => h.revenue),
    );

    return Array.from(hourlyMap.values())
      .sort((a, b) => a.hour.localeCompare(b.hour))
      .map((item) => ({
        hour: item.hour,
        revenue: item.revenue,
        revenueLY: item.revenueLY,
        percentage:
          maxRevenue > 0 ? Math.round((item.revenue / maxRevenue) * 100) : 0,
        isPeak: item.revenue >= maxRevenue * 0.8,
      }));
  }, [hourlyData, selectedBranch]);

  return (
    <div className={styles.pageContainer}>
      <header className={styles.headerSection}>
        <div className={styles.headerTop}>
          <div className={styles.headerLeft}>
            <div className={styles.headerTitleContainer}>
              <h1 className={styles.headerTitle}>
                {t('executiveSales.title')}
              </h1>
              <p className={styles.headerSubtitle}>
                {t('executiveSales.subtitle')}
              </p>
            </div>
          </div>

          <div className={styles.headerActions}>
            <RangePicker
              value={[dateRange.startDate, dateRange.endDate]}
              onChange={handleRangeChange}
              format="YYYY-MM-DD"
              allowClear={false}
              size="small"
              className={styles.dateRangePicker}
              presets={Object.entries(presetRanges).map(([label, range]) => ({
                label,
                value: range,
              }))}
            />
            {dateRange.isManualCompareDate ? (
              <RangePicker
                value={[dateRange.compareStartDate, dateRange.compareEndDate]}
                onChange={handleManualCompareDateChange}
                format="YYYY-MM-DD"
                allowClear={false}
                size="small"
                className={styles.dateRangePicker}
              />
            ) : (
              <div className={styles.compareDateTag}>
                {t('executiveSales.tag.compare')}{' '}
                {dateRange.compareStartDate.format('YYYY-MM-DD')} -{' '}
                {dateRange.compareEndDate.format('YYYY-MM-DD')}
              </div>
            )}
            <Radio.Group
              value={dateRange.compareMode}
              onChange={(e) => handleCompareModeChange(e.target.value)}
              buttonStyle="solid"
              size="small"
              optionType="button"
            >
              <Radio.Button value="by-week">
                {t('executiveSales.compareMode.byWeek')}
              </Radio.Button>
              <Radio.Button value="by-date">
                {t('executiveSales.compareMode.byDate')}
              </Radio.Button>
            </Radio.Group>
            <span className={styles.compareDateSwitch}>
              <Switch
                size="small"
                checked={dateRange.isManualCompareDate}
                onChange={handleManualCompareModeChange}
              />
              <span className={styles.compareDateSwitchLabel}>
                {dateRange.isManualCompareDate
                  ? t('executiveSales.compareMode.manual')
                  : t('executiveSales.compareMode.auto')}
              </span>
            </span>
          </div>
        </div>

        <div className={styles.quickSelectRow}>
          <span className={styles.quickSelectLabel}>
            {t('executiveSales.quickSelect.label')}
            :
          </span>
          {quickSelectOptions.map((option) => (
            <button
              type="button"
              key={option.key}
              className={`${styles.quickSelectBtn} ${
                selectedQuickSelect === option.key ? styles.active : ''
              }`}
              onClick={() => handleQuickSelectChange(option.key)}
            >
              {option.label}
            </button>
          ))}
        </div>

        {selectedBranch && (
          <div className={styles.filterTagsRow}>
            <Tag closable onClose={handleClearBranchSelection} color="blue">
              {selectedBranch.branchName}
            </Tag>
          </div>
        )}
      </header>

      <div className={styles.bentoGrid}>
        <div
          className={`${styles.cardBase} ${styles.colSpan8}`}
          style={{ minHeight: 600 }}
        >
          <div className={styles.cardHeader}>
            <h3 className={styles.cardTitle}>
              {t('executiveSales.dailyBranchPerformance')}
            </h3>
            <div className={styles.detailLink}>
              <span>
                {t('executiveSales.viewDetails')}
              </span>
              <RightOutlined className={styles.detailArrow} />
            </div>
          </div>
          <BranchPerformanceTable
            data={transformedBranchData}
            loading={loading}
            onBranchSelect={handleBranchSelect}
            selectedBranchCode={selectedBranch?.branchCode}
          />
        </div>

        <div
          className={`${styles.cardBase} ${styles.colSpan4}`}
          style={{ minHeight: 600 }}
        >
          <div className={styles.cardHeader}>
            <h3 className={styles.cardTitle}>
              {t('executiveSales.hourlyTrafficDensity')}
            </h3>
            {selectedBranch && (
              <Tag
                closable
                onClose={handleClearBranchSelection}
                className={`${styles.branchTag} ${styles.cardInTag}`}
              >
                {selectedBranch.branchName}
              </Tag>
            )}
          </div>
          <HourlyTrafficDensity
            data={transformedHourlyData}
            loading={hourlyLoading}
          />
        </div>

        <div className={`${styles.cardBase} ${styles.colSpan12}`}>
          <div className={styles.cardHeader}>
            <div>
              <h3 className={styles.cardTitle}>
                {t('executiveSales.weeklyPerformanceHierarchy')}
              </h3>
              <p
                style={{
                  fontSize: '0.75rem',
                  color: '#434750',
                  margin: '4px 0 0 0',
                }}
              >
                {t('executiveSales.hierarchy.subtitle')}
              </p>
            </div>
          </div>
          <WeeklyPerformanceCardHeader
            value={weeklyDateRange}
            onChange={setWeeklyDateRange}
          />
          <WeeklyPerformanceHierarchy
            data={weeklyData}
            loading={weeklyLoading}
          />
          <div className={styles.cardFooter}>
            <p className={styles.footerText}>
              {weeklyData.length > 0
                ? t('executiveSales.footer.showing', {
                      branches: weeklyData.reduce(
                        (acc, w) => acc + (w.children?.length || 0),
                        0,
                      ),
                      weeks: weeklyData.length,
                    },
                  )
                : t('common.noData')}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ExecutiveSalesIntelligence;
