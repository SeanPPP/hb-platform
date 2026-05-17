import { useTranslation } from 'react-i18next';
import { Spin } from 'antd';
import React, { useState } from 'react';
import styles from './WeeklyPerformanceHierarchy.module.css';

interface WeeklyData {
  key: string;
  level: 'week' | 'branch' | 'date';
  hierarchy: string;
  revenue: number;
  revenueLY: number;
  orders: number;
  ordersLY: number;
  aov: number;
  aovLY: number;
  yoyChange?: number;
  children?: WeeklyData[];
}

interface WeeklyPerformanceHierarchyProps {
  data: WeeklyData[];
  loading?: boolean;
}

const formatCurrency = (value: number): string => {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 2,
  }).format(value);
};

const formatNumber = (value: number): string => {
  return new Intl.NumberFormat('en-US').format(value);
};

const branchColors = [
  '#3B82F6',
  '#10B981',
  '#F59E0B',
  '#EF4444',
  '#8B5CF6',
  '#EC4899',
  '#06B6D4',
  '#84CC16',
  '#F97316',
  '#6366F1',
  '#14B8A6',
  '#A855F7',
  '#F472B6',
  '#22D3EE',
  '#4ADE80',
  '#FB923C',
  '#818CF8',
  '#2DD4BF',
  '#FBBF24',
  '#E879F9',
];

const WeeklyPerformanceHierarchy: React.FC<WeeklyPerformanceHierarchyProps> = ({
  data,
  loading = false,
}) => {
  const { t } = useTranslation();
  const [expandedKeys, setExpandedKeys] = useState<React.Key[]>([]);

  const toggleExpand = (key: React.Key) => {
    setExpandedKeys((prev) =>
      prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key],
    );
  };

  const renderRow = (
    record: WeeklyData,
    level: number = 0,
    branchColorIndex?: number,
  ) => {
    const isExpanded = expandedKeys.includes(record.key);
    const hasChildren = record.children && record.children.length > 0;

    let currentBranchColorIndex = branchColorIndex;
    let currentColor = branchColors[0];

    if (level === 0 && record.level === 'week') {
      currentBranchColorIndex = 0;
      currentColor =
        branchColors[currentBranchColorIndex % branchColors.length];
    } else if (
      level === 1 &&
      record.level === 'branch' &&
      branchColorIndex !== undefined
    ) {
      currentBranchColorIndex = branchColorIndex;
      currentColor =
        branchColors[currentBranchColorIndex % branchColors.length];
    } else if (level >= 2 && branchColorIndex !== undefined) {
      currentColor = branchColors[branchColorIndex % branchColors.length];
    }

    const getLevelClass = () => {
      switch (record.level) {
        case 'week':
          return styles.rowWeek;
        case 'branch':
          return styles.rowBranch;
        case 'date':
          return styles.rowDate;
        default:
          return '';
      }
    };

    const getIndent = () => {
      switch (level) {
        case 1:
          return styles.indentBranch;
        case 2:
          return styles.indentDate;
        default:
          return '';
      }
    };

    const renderExpandIcon = () => {
      if (!hasChildren) {
        if (record.level === 'date') {
          return (
            <span className={styles.dot} style={{ background: currentColor }} />
          );
        }
        return null;
      }

      return (
        <span
          className={`${styles.expandIcon} ${
            isExpanded ? styles.expanded : ''
          }`}
          style={{ color: currentColor }}
          onClick={() => toggleExpand(record.key)}
        />
      );
    };

    const renderYoY = (current: number, ly: number) => {
      if (ly === 0) {
        return <span className={styles.changeRatio}>-</span>;
      }
      const change = ((current - ly) / ly) * 100;
      const isUp = change >= 0;
      return (
        <span
          className={`${styles.changeRatio} ${
            isUp ? styles.changeUp : styles.changeDown
          }`}
        >
          {isUp ? '+' : ''}
          {change.toFixed(1)}%
        </span>
      );
    };

    return (
      <React.Fragment key={record.key}>
        <tr
          className={`${styles.row} ${getLevelClass()}`}
          onClick={() => hasChildren && toggleExpand(record.key)}
        >
          <td className={styles.cell}>
            <div className={`${styles.hierarchyCell} ${getIndent()}`}>
              {renderExpandIcon()}
              <span
                className={styles.hierarchyLabel}
                style={{ color: currentColor }}
              >
                {record.hierarchy}
              </span>
            </div>
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            <span className={styles.value} style={{ color: currentColor }}>
              {formatCurrency(record.revenue)}
            </span>
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            <span className={styles.lyValue}>
              {formatCurrency(record.revenueLY)}
            </span>
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            {renderYoY(record.revenue, record.revenueLY)}
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            <span className={styles.subValue} style={{ color: currentColor }}>
              {formatNumber(record.orders)}
            </span>
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            <span className={styles.lyValue}>
              {formatNumber(record.ordersLY)}
            </span>
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            {renderYoY(record.orders, record.ordersLY)}
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            <span className={styles.subValue} style={{ color: currentColor }}>
              {formatCurrency(record.aov)}
            </span>
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            <span className={styles.lyValue}>
              {formatCurrency(record.aovLY)}
            </span>
          </td>
          <td className={`${styles.cell} ${styles.cellRight}`}>
            {renderYoY(record.aov, record.aovLY)}
          </td>
        </tr>
        {isExpanded &&
          record.children &&
          record.children.map((child, index) => {
            const nextColorIndex =
              record.level === 'week' ? index : currentBranchColorIndex;
            return renderRow(child, level + 1, nextColorIndex);
          })}
      </React.Fragment>
    );
  };

  return (
    <div className={styles.container}>
      {loading ? (
        <div className={styles.loadingContainer}>
          <Spin size="large" />
        </div>
      ) : data.length === 0 ? (
        <div className={styles.emptyContainer}>
          <p className={styles.emptyText}>
            {t('executiveSales.weekly.noData')}
          </p>
        </div>
      ) : (
        <table className={styles.table}>
          <thead className={styles.thead}>
            <tr>
              <th className={styles.th} rowSpan={2}>
                {t('executiveSales.weekly.hierarchy')}
              </th>
              <th className={`${styles.th} ${styles.thCenter}`} colSpan={3}>
                {t('executiveSales.weekly.revenue')}
              </th>
              <th className={`${styles.th} ${styles.thCenter}`} colSpan={3}>
                {t('executiveSales.weekly.orders')}
              </th>
              <th className={`${styles.th} ${styles.thCenter}`} colSpan={3}>
                {t('executiveSales.weekly.aov')}
              </th>
            </tr>
            <tr>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.current')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.ly')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.yoy')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.current')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.ly')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.yoy')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.current')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.ly')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.weekly.yoy')}
              </th>
            </tr>
          </thead>
          <tbody className={styles.tbody}>
            {data.map((record) => renderRow(record))}
          </tbody>
        </table>
      )}
    </div>
  );
};

export default WeeklyPerformanceHierarchy;
