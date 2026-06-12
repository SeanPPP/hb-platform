import {
  getBranchSalesAggregate,
  getChinaSupplierSalesRank,
  getEnhancedSalesProductDetails,
  getSupplierSalesRank,
  type BranchSalesAggregate,
  type DateRange,
  type PagedSalesProductDetailWithDiscount,
} from '../../../services/salesDashboardService';
import { useTranslation } from 'react-i18next';
import { useAuthStore } from '../../../store/auth';
import { message } from 'antd';
import dayjs from 'dayjs';
import isoWeek from 'dayjs/plugin/isoWeek';
import weekday from 'dayjs/plugin/weekday';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import AIFloatingButton from './components/AIFloatingButton';
import BranchCard, {
  type BranchDistributionData,
} from './components/BranchCard';
import HeaderSection, {
  type QuickSelectOption,
} from './components/HeaderSection';
import SalesDetailTable, {
  type ProductSalesData,
} from './components/SalesDetailTable';
import SupplierCard, {
  type SupplierSalesData,
} from './components/SupplierCard';
import styles from './styles.module.css';
dayjs.extend(isoWeek);
dayjs.extend(weekday);

type CompareMode = 'by-week' | 'by-date';

const calculateCompareDates = (
  startDate: dayjs.Dayjs,
  endDate: dayjs.Dayjs,
  compareMode: CompareMode,
): [dayjs.Dayjs, dayjs.Dayjs] => {
  const dayDiff = endDate.diff(startDate, 'day');

  if (compareMode === 'by-date') {
    const compareStart = startDate.subtract(1, 'year');
    return [compareStart, compareStart.add(dayDiff, 'day')];
  }

  const compareStart = dayjs()
    .year(startDate.year() - 1)
    .isoWeek(startDate.isoWeek())
    .isoWeekday(startDate.isoWeekday());
  return [compareStart, compareStart.add(dayDiff, 'day')];
};

const quickSelectDateRanges: Record<
  QuickSelectOption,
  { start: dayjs.Dayjs; end: dayjs.Dayjs }
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

const SalesDetailAnalysisV2: React.FC = () => {
  const { t } = useTranslation();
  const access = useAuthStore((state) => state.access);

  // 🔐 获取用户管辖的分店代码（Admin/WarehouseManager 返回 null，不过滤）
  const managedStoreCodes = access.managedStoreCodes?.() ?? undefined;

  const [selectedQuickSelect, setSelectedQuickSelect] =
    useState<QuickSelectOption>('today');
  const [searchValue, setSearchValue] = useState('');

  const [dateRange, setDateRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([
    quickSelectDateRanges.today.start,
    quickSelectDateRanges.today.end,
  ]);

  const [localSupplierData, setLocalSupplierData] = useState<
    SupplierSalesData[]
  >([]);
  const [chinaSupplierData, setChinaSupplierData] = useState<
    SupplierSalesData[]
  >([]);
  const [branchData, setBranchData] = useState<BranchDistributionData[]>([]);
  const [productData, setProductData] = useState<ProductSalesData[]>([]);

  const [supplierLoading, setSupplierLoading] = useState(false);
  const [branchLoading, setBranchLoading] = useState(false);
  const [productLoading, setProductLoading] = useState(false);

  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [totalCount, setTotalCount] = useState(0);

  const [selectedBranch, setSelectedBranch] = useState<string | null>(null);
  const [selectedChinaSupplier, setSelectedChinaSupplier] = useState<
    string | null
  >(null);
  const [selectedLocalSupplier, setSelectedLocalSupplier] = useState<
    string | null
  >(null);
  const [compareMode, setCompareMode] = useState<CompareMode>('by-week');
  const [isManualCompareDate, setIsManualCompareDate] = useState(false);
  const [manualCompareDate, setManualCompareDate] = useState<
    [dayjs.Dayjs, dayjs.Dayjs]
  >(() =>
    calculateCompareDates(
      quickSelectDateRanges.today.start,
      quickSelectDateRanges.today.end,
      'by-week',
    ),
  );

  const dateRangeParams = useMemo((): DateRange => {
    const [compareStart, compareEnd] = isManualCompareDate
      ? manualCompareDate
      : calculateCompareDates(dateRange[0], dateRange[1], compareMode);
    return {
      startDate: dateRange[0].format('YYYY-MM-DD'),
      endDate: dateRange[1].format('YYYY-MM-DD'),
      compareStartDate: compareStart.format('YYYY-MM-DD'),
      compareEndDate: compareEnd.format('YYYY-MM-DD'),
    };
  }, [dateRange, compareMode, isManualCompareDate, manualCompareDate]);

  const compareDateRange = useMemo((): [dayjs.Dayjs, dayjs.Dayjs] => {
    if (isManualCompareDate) return manualCompareDate;
    return calculateCompareDates(dateRange[0], dateRange[1], compareMode);
  }, [dateRange, compareMode, isManualCompareDate, manualCompareDate]);

  const handleCompareModeChange = useCallback((mode: CompareMode) => {
    setCompareMode(mode);
  }, []);

  const handleBranchClick = useCallback(
    (branchCode: string) => {
      if (selectedBranch === branchCode) {
        setSelectedBranch(null);
      } else {
        setSelectedBranch(branchCode);
      }
    },
    [selectedBranch],
  );

  const handleChinaSupplierClick = useCallback(
    (supplierCode: string) => {
      if (selectedChinaSupplier === supplierCode) {
        setSelectedChinaSupplier(null);
      } else {
        setSelectedChinaSupplier(supplierCode);
        setSelectedLocalSupplier(null);
      }
    },
    [selectedChinaSupplier],
  );

  const handleLocalSupplierClick = useCallback(
    (supplierCode: string) => {
      if (selectedLocalSupplier === supplierCode) {
        setSelectedLocalSupplier(null);
      } else {
        setSelectedLocalSupplier(supplierCode);
        setSelectedChinaSupplier(null);
      }
    },
    [selectedLocalSupplier],
  );

  const handleQuickSelectChange = useCallback((option: QuickSelectOption) => {
    setSelectedQuickSelect(option);
    const range = quickSelectDateRanges[option];
    setDateRange([range.start, range.end]);
    setCompareMode(
      option === 'thisMonth' || option === 'lastMonth' ? 'by-date' : 'by-week',
    );
    setIsManualCompareDate(false);
  }, []);

  const handleManualCompareModeChange = useCallback((checked: boolean) => {
    setIsManualCompareDate(checked);
  }, []);

  const handleManualCompareDateChange = useCallback(
    (range: [dayjs.Dayjs, dayjs.Dayjs]) => {
      setManualCompareDate(range);
    },
    [],
  );

  const handleDateRangeChange = useCallback(
    (range: [dayjs.Dayjs, dayjs.Dayjs]) => {
      setSelectedQuickSelect('today');
      setDateRange(range);
    },
    [],
  );

  const handleClearAllFilters = useCallback(() => {
    setSelectedBranch(null);
    setSelectedChinaSupplier(null);
    setSelectedLocalSupplier(null);
    setSearchValue('');
    setCurrentPage(1);
  }, []);

  const handleClearBranch = useCallback(() => {
    setSelectedBranch(null);
  }, []);

  const handleClearChinaSupplier = useCallback(() => {
    setSelectedChinaSupplier(null);
  }, []);

  const handleClearLocalSupplier = useCallback(() => {
    setSelectedLocalSupplier(null);
  }, []);

  const hasActiveFilters =
    selectedBranch ||
    selectedChinaSupplier ||
    selectedLocalSupplier ||
    searchValue;

  const fetchLocalSuppliers = useCallback(async () => {
    setSupplierLoading(true);
    try {
      // 🔐 店长/经理用户：默认使用管辖分店过滤，允许手动切换
      const branchCodes = selectedBranch ? [selectedBranch] : managedStoreCodes;
      const response = await getSupplierSalesRank(
        dateRangeParams,
        200,
        branchCodes,
      );
      if (response.success && response.data) {
        const totalRevenue = response.data.reduce(
          (sum, item) => sum + item.totalAmount,
          0,
        );
        const totalRevenueLY = response.data.reduce(
          (sum, item) => sum + (item.compareTotalAmount || 0),
          0,
        );

        const sortedData = [...response.data].sort(
          (a, b) => b.totalAmount - a.totalAmount,
        );

        const data: SupplierSalesData[] = sortedData.map((item, index) => ({
          rank: index + 1,
          supplierCode: item.supplierCode,
          supplierName: item.supplierName,
          revenue: item.totalAmount,
          revenueLY: item.compareTotalAmount || 0,
          quantity: item.totalQuantity,
          quantityLY: 0,
          orderCount: item.storeCount,
          orderCountLY: 0,
          sharePercent:
            totalRevenue > 0 ? (item.totalAmount / totalRevenue) * 100 : 0,
          sharePercentLY:
            totalRevenueLY > 0
              ? ((item.compareTotalAmount || 0) / totalRevenueLY) * 100
              : 0,
        }));
        setLocalSupplierData(data);
      }
    } catch (error) {
      console.error('Failed to fetch local suppliers:', error);
      message.error(
        t('salesDetailAnalysisV2.error.localSuppliers'),
      );
    } finally {
      setSupplierLoading(false);
    }
  }, [dateRangeParams, t, selectedBranch, managedStoreCodes]);

  const fetchChinaSuppliers = useCallback(async () => {
    setSupplierLoading(true);
    try {
      // 🔐 店长/经理用户：默认使用管辖分店过滤，允许手动切换
      const branchCodes = selectedBranch ? [selectedBranch] : managedStoreCodes;
      const response = await getChinaSupplierSalesRank(
        dateRangeParams,
        200,
        branchCodes,
      );
      if (response.success && response.data) {
        const totalRevenue = response.data.reduce(
          (sum, item) => sum + item.totalAmount,
          0,
        );
        const totalRevenueLY = response.data.reduce(
          (sum, item) => sum + (item.compareTotalAmount || 0),
          0,
        );

        const data: SupplierSalesData[] = response.data.map((item, index) => ({
          rank: index + 1,
          supplierCode: item.supplierCode,
          supplierName: item.supplierName,
          revenue: item.totalAmount,
          revenueLY: item.compareTotalAmount || 0,
          quantity: item.totalQuantity,
          quantityLY: 0,
          orderCount: item.storeCount,
          orderCountLY: 0,
          sharePercent:
            totalRevenue > 0 ? (item.totalAmount / totalRevenue) * 100 : 0,
          sharePercentLY:
            totalRevenueLY > 0
              ? ((item.compareTotalAmount || 0) / totalRevenueLY) * 100
              : 0,
        }));
        setChinaSupplierData(data);
      }
    } catch (error) {
      console.error('Failed to fetch China suppliers:', error);
      message.error(
        t('salesDetailAnalysisV2.error.chinaSuppliers'),
      );
    } finally {
      setSupplierLoading(false);
    }
  }, [dateRangeParams, t, selectedBranch, managedStoreCodes]);

  const fetchBranches = useCallback(async () => {
    setBranchLoading(true);
    try {
      const supplierCodes =
        selectedChinaSupplier || selectedLocalSupplier
          ? ([selectedChinaSupplier || selectedLocalSupplier].filter(
              Boolean,
            ) as string[])
          : undefined;

      const compareDateRange =
        dateRangeParams.compareStartDate && dateRangeParams.compareEndDate
          ? {
              startDate: dateRangeParams.compareStartDate,
              endDate: dateRangeParams.compareEndDate,
            }
          : undefined;

      // 🔐 店长/经理用户：默认使用管辖分店过滤
      const response = await getBranchSalesAggregate(
        dateRangeParams,
        compareDateRange,
        managedStoreCodes,
        supplierCodes,
      );

      if (response.success && response.data) {
        const branchAggregateData = response.data as BranchSalesAggregate[];

        const data: BranchDistributionData[] = branchAggregateData.map(
          (branch, index) => ({
            rank: index + 1,
            branchCode: branch.branchCode,
            branchName: branch.branchName,
            revenue: branch.totalRevenue,
            revenueLY: branch.totalRevenueLY,
            quantity: branch.totalQuantity,
            quantityLY: branch.totalQuantityLY,
            orderCount: branch.orderCount,
            orderCountLY: branch.orderCountLY,
            sharePercent:
              branch.totalRevenue > 0
                ? (branch.hbRevenue / branch.totalRevenue) * 100
                : 0,
            sharePercentLY:
              branch.totalRevenueLY > 0
                ? (branch.hbRevenueLY / branch.totalRevenueLY) * 100
                : 0,
          }),
        );

        setBranchData(data);
      }
    } catch (error) {
      console.error('Failed to fetch branches:', error);
      message.error(
        t('salesDetailAnalysisV2.error.branches'),
      );
    } finally {
      setBranchLoading(false);
    }
  }, [
    dateRangeParams,
    selectedChinaSupplier,
    selectedLocalSupplier,
    managedStoreCodes,
  ]);

  const fetchProducts = useCallback(async () => {
    setProductLoading(true);
    try {
      // 🔐 店长/经理用户：默认使用管辖分店过滤，允许手动切换
      const branchCodes = selectedBranch ? [selectedBranch] : managedStoreCodes;
      const localSupplierCodes = selectedLocalSupplier
        ? [selectedLocalSupplier]
        : undefined;
      const chinaSupplierCodes = selectedChinaSupplier
        ? [selectedChinaSupplier]
        : undefined;

      
      const response = await getEnhancedSalesProductDetails(
        dateRangeParams,
        branchCodes,
        localSupplierCodes,
        chinaSupplierCodes,
        currentPage,
        pageSize,
      );

      
      if (response.success && response.data) {
        const result = response.data as PagedSalesProductDetailWithDiscount;
                const data: ProductSalesData[] = result.data.map((item, index) => ({
          rank: (currentPage - 1) * pageSize + index + 1,
          skuCode: item.itemNumber || item.productCode,
          productName: item.productName || 'Unknown Product',
          productImage: item.productImage,
          quantity: item.quantity,
          quantityLY: item.quantityLY,
          discountQuantity: item.discountedQuantity,
          discountQuantityLY: item.discountedQuantityLY,
          avgPrice: item.averageUnitPrice,
          avgPriceLY: item.averageUnitPriceLY,
          grossTotal: item.salesAmount,
          grossTotalLY: item.salesAmountLY,
        }));

        
        setProductData(data);
        setTotalCount(result.total);
      }
    } catch (error) {
      console.error('Failed to fetch products:', error);
      message.error(
        t('salesDetailAnalysisV2.error.products'),
      );
    } finally {
      setProductLoading(false);
    }
  }, [
    dateRangeParams,
    currentPage,
    pageSize,
    selectedBranch,
    selectedChinaSupplier,
    selectedLocalSupplier, t, managedStoreCodes,
  ]);

  useEffect(() => {
    fetchLocalSuppliers();
    fetchChinaSuppliers();
  }, [dateRangeParams, selectedBranch]);

  useEffect(() => {
    fetchBranches();
  }, [dateRangeParams]);

  useEffect(() => {
    fetchProducts();
  }, [
    currentPage,
    pageSize,
    dateRangeParams,
    selectedBranch,
    selectedChinaSupplier,
    selectedLocalSupplier,
  ]);

  const filteredLocalSuppliers = useMemo(() => {
    if (!searchValue) return localSupplierData;
    const search = searchValue.toLowerCase();
    return localSupplierData.filter(
      (item) =>
        item.supplierName.toLowerCase().includes(search) ||
        item.supplierCode?.toLowerCase().includes(search),
    );
  }, [localSupplierData, searchValue]);

  const filteredChinaSuppliers = useMemo(() => {
    if (!searchValue) return chinaSupplierData;
    const search = searchValue.toLowerCase();
    return chinaSupplierData.filter(
      (item) =>
        item.supplierName.toLowerCase().includes(search) ||
        item.supplierCode?.toLowerCase().includes(search),
    );
  }, [chinaSupplierData, searchValue]);

  const filteredBranches = useMemo(() => {
    if (!searchValue) return branchData;
    const search = searchValue.toLowerCase();
    return branchData.filter(
      (item) =>
        item.branchName.toLowerCase().includes(search) ||
        item.branchCode.toLowerCase().includes(search),
    );
  }, [branchData, searchValue]);

  const filterBranchName = useMemo(() => {
    if (!selectedBranch) return null;
    const branch = branchData.find((b) => b.branchCode === selectedBranch);
    return branch?.branchName || null;
  }, [selectedBranch, branchData]);

  const filterChinaSupplierName = useMemo(() => {
    if (!selectedChinaSupplier) return null;
    const supplier = chinaSupplierData.find(
      (s) => s.supplierCode === selectedChinaSupplier,
    );
    return supplier?.supplierName || null;
  }, [selectedChinaSupplier, chinaSupplierData]);

  const filterLocalSupplierName = useMemo(() => {
    if (!selectedLocalSupplier) return null;
    const supplier = localSupplierData.find(
      (s) => s.supplierCode === selectedLocalSupplier,
    );
    return supplier?.supplierName || null;
  }, [selectedLocalSupplier, localSupplierData]);

  const handlePageChange = (page: number, size: number) => {
    setCurrentPage(page);
    setPageSize(size);
  };

  return (
    <div className={styles.pageContainer}>
      <HeaderSection
        searchValue={searchValue}
        onSearchChange={setSearchValue}
        selectedQuickSelect={selectedQuickSelect}
        onQuickSelectChange={handleQuickSelectChange}
        dateRange={dateRange}
        onDateRangeChange={handleDateRangeChange}
        compareDateRange={compareDateRange}
        compareMode={compareMode}
        onCompareModeChange={handleCompareModeChange}
        isManualCompareDate={isManualCompareDate}
        onManualCompareModeChange={handleManualCompareModeChange}
        manualCompareDateRange={manualCompareDate}
        onManualCompareDateChange={handleManualCompareDateChange}
        filterTags={{
          branch: filterBranchName || undefined,
          chinaSupplier: filterChinaSupplierName || undefined,
          localSupplier: filterLocalSupplierName || undefined,
        }}
        onClearBranch={handleClearBranch}
        onClearChinaSupplier={handleClearChinaSupplier}
        onClearLocalSupplier={handleClearLocalSupplier}
        onClearFilters={handleClearAllFilters}
        hasActiveFilters={!!hasActiveFilters}
      />

      <div className={styles.bentoGrid}>
        <BranchCard
          title={t('salesDetailAnalysisV2.card.branchDistribution')}
          data={filteredBranches}
          loading={branchLoading}
          onRowClick={handleBranchClick}
          selectedRow={selectedBranch}
          filterSupplierName={
            filterChinaSupplierName || filterLocalSupplierName
          }
        />
        <SupplierCard
          title={t('salesDetailAnalysisV2.card.chinaSuppliers')}
          icon="china"
          data={filteredChinaSuppliers}
          loading={supplierLoading}
          onRowClick={handleChinaSupplierClick}
          selectedRow={selectedChinaSupplier}
          filterBranchName={filterBranchName}
        />
        <SupplierCard
          title={t('salesDetailAnalysisV2.card.localSuppliers')}
          icon="local"
          data={filteredLocalSuppliers}
          loading={supplierLoading}
          onRowClick={handleLocalSupplierClick}
          selectedRow={selectedLocalSupplier}
          filterBranchName={filterBranchName}
        />
      </div>

      <SalesDetailTable
        data={productData}
        loading={productLoading}
        totalCount={totalCount}
        currentPage={currentPage}
        pageSize={pageSize}
        onPageChange={handlePageChange}
        filterBranch={filterBranchName}
        filterChinaSupplier={filterChinaSupplierName}
        filterLocalSupplier={filterLocalSupplierName}
      />

      <AIFloatingButton />
    </div>
  );
};

export default SalesDetailAnalysisV2;
