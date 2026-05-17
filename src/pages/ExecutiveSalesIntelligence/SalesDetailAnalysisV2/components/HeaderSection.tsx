import { ClearOutlined, SearchOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { DatePicker, Input, Radio, Switch, Tag } from 'antd';
import type { Dayjs } from 'dayjs';
import React from 'react';
import styles from '../styles.module.css';

type CompareMode = 'by-week' | 'by-date';

const { RangePicker } = DatePicker;

export type QuickSelectOption =
  | 'today'
  | 'yesterday'
  | 'thisWeek'
  | 'lastWeek'
  | 'thisMonth'
  | 'lastMonth';

export interface HeaderSectionProps {
  searchValue: string;
  onSearchChange: (value: string) => void;
  selectedQuickSelect: QuickSelectOption;
  onQuickSelectChange: (option: QuickSelectOption) => void;
  dateRange: [Dayjs, Dayjs];
  onDateRangeChange: (range: [Dayjs, Dayjs]) => void;
  compareDateRange?: [Dayjs, Dayjs];
  compareMode?: CompareMode;
  onCompareModeChange?: (mode: CompareMode) => void;
  isManualCompareDate?: boolean;
  onManualCompareModeChange?: (checked: boolean) => void;
  manualCompareDateRange?: [Dayjs, Dayjs];
  onManualCompareDateChange?: (range: [Dayjs, Dayjs]) => void;
  filterTags?: {
    branch?: string;
    chinaSupplier?: string;
    localSupplier?: string;
  };
  onClearBranch?: () => void;
  onClearChinaSupplier?: () => void;
  onClearLocalSupplier?: () => void;
  onClearFilters?: () => void;
  hasActiveFilters?: boolean;
}

const quickSelectOptions: { key: QuickSelectOption; label: string }[] = [
  { key: 'today', label: 'Today' },
  { key: 'yesterday', label: 'Yesterday' },
  { key: 'thisWeek', label: 'This Week' },
  { key: 'lastWeek', label: 'Last Week' },
  { key: 'thisMonth', label: 'This Month' },
  { key: 'lastMonth', label: 'Last Month' },
];

const HeaderSection: React.FC<HeaderSectionProps> = ({
  searchValue,
  onSearchChange,
  selectedQuickSelect,
  onQuickSelectChange,
  dateRange,
  onDateRangeChange,
  compareDateRange,
  compareMode = 'by-week',
  onCompareModeChange,
  isManualCompareDate = false,
  onManualCompareModeChange,
  manualCompareDateRange,
  onManualCompareDateChange,
  filterTags,
  onClearBranch,
  onClearChinaSupplier,
  onClearLocalSupplier,
  onClearFilters,
  hasActiveFilters,
}) => {
  const { t } = useTranslation();

  const hasAnyFilter =
    filterTags?.branch ||
    filterTags?.chinaSupplier ||
    filterTags?.localSupplier;

  return (
    <header className={styles.headerSection}>
      <div className={styles.headerTop}>
        <div className={styles.headerLeft}>
          <span className={styles.headerIcon}>📊</span>
          <h1 className={styles.headerTitle}>
            {t('salesDetailAnalysisV2.title')}
          </h1>
        </div>

        <div className={styles.searchContainer}>
          <SearchOutlined className={styles.searchIcon} />
          <Input
            className={styles.searchInput}
            placeholder={t('salesDetailAnalysisV2.search.placeholder')}
            value={searchValue}
            onChange={(e) => onSearchChange(e.target.value)}
          />
        </div>

        <div className={styles.headerActions}>
          <RangePicker
            className={styles.dateRangePicker}
            value={dateRange}
            onChange={(dates) => {
              if (dates && dates[0] && dates[1]) {
                onDateRangeChange([dates[0], dates[1]]);
              }
            }}
            format="YYYY-MM-DD"
            allowClear={false}
          />
          {isManualCompareDate && manualCompareDateRange ? (
            <RangePicker
              value={manualCompareDateRange}
              onChange={(dates) => {
                if (dates && dates[0] && dates[1]) {
                  onManualCompareDateChange?.([dates[0], dates[1]]);
                }
              }}
              format="YYYY-MM-DD"
              allowClear={false}
              size="small"
            />
          ) : (
            compareDateRange && (
              <div className={styles.compareDateTag}>
                {t('salesDetailAnalysisV2.compareDate')}
                : {compareDateRange[0].format('YYYY-MM-DD')} -{' '}
                {compareDateRange[1].format('YYYY-MM-DD')}
              </div>
            )
          )}
          <Radio.Group
            value={compareMode}
            onChange={(e) => onCompareModeChange?.(e.target.value)}
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
              checked={isManualCompareDate}
              onChange={onManualCompareModeChange}
            />
            <span className={styles.compareDateSwitchLabel}>
              {isManualCompareDate
                ? t('executiveSales.compareMode.manual')
                : t('executiveSales.compareMode.auto')}
            </span>
          </span>

          {hasActiveFilters && (
            <button
              type="button"
              className={`${styles.actionBtn} ${styles.clearBtn}`}
              onClick={onClearFilters}
              title="Clear Filters"
            >
              <ClearOutlined style={{ fontSize: 18 }} />
            </button>
          )}
        </div>
      </div>

      <div className={styles.quickSelectRow}>
        <span className={styles.quickSelectLabel}>
          {t('salesDetailAnalysisV2.quickSelect.label')}
          :
        </span>
        {quickSelectOptions.map((option) => (
          <button
            type="button"
            key={option.key}
            className={`${styles.quickSelectBtn} ${
              selectedQuickSelect === option.key ? styles.active : ''
            }`}
            onClick={() => onQuickSelectChange(option.key)}
          >
            {option.label}
          </button>
        ))}
      </div>

      {hasAnyFilter && (
        <div className={styles.filterTagsRow}>
          {filterTags?.branch && (
            <Tag closable onClose={onClearBranch} color="blue">
              {filterTags.branch}
            </Tag>
          )}
          {filterTags?.chinaSupplier && (
            <Tag closable onClose={onClearChinaSupplier} color="green">
              {filterTags.chinaSupplier}
            </Tag>
          )}
          {filterTags?.localSupplier && (
            <Tag closable onClose={onClearLocalSupplier} color="orange">
              {filterTags.localSupplier}
            </Tag>
          )}
          {hasActiveFilters && (
            <button
              type="button"
              className={`${styles.actionBtn} ${styles.clearBtn}`}
              onClick={onClearFilters}
              title="Clear All Filters"
            >
              <ClearOutlined style={{ fontSize: 16 }} />
            </button>
          )}
        </div>
      )}
    </header>
  );
};

export default HeaderSection;
