import { useTranslation } from 'react-i18next';
import { Table } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import React from 'react';
import styles from './BranchPerformanceTable.module.css';

interface BranchPerformance {
  rank: number;
  branchCode?: string;
  branchName: string;
  revenue: number;
  revenueLY: number;
  orderCount: number;
  orderCountLY: number;
  aov: number;
  aovLY: number;
}

interface BranchPerformanceTableProps {
  data: BranchPerformance[];
  loading?: boolean;
  onViewDetails?: () => void;
  showBranchCode?: boolean;
  onBranchSelect?: (branchCode: string, branchName: string) => void;
  selectedBranchCode?: string | null;
}

const formatCurrency = (value: number): string => {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
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

const BranchPerformanceTable: React.FC<BranchPerformanceTableProps> = ({
  data,
  loading = false,
  onBranchSelect,
  selectedBranchCode,
}) => {
  const { t } = useTranslation();
  const columns: ColumnsType<BranchPerformance> = [
    {
      title: t('executiveSales.table.rank'),
      dataIndex: 'rank',
      key: 'rank',
      width: 50,
      fixed: 'left',
      render: (rank: number) => (
        <span className={styles.rankNumber}>
          {String(rank).padStart(2, '0')}
        </span>
      ),
    },
    {
      title: t('executiveSales.table.branchName'),
      dataIndex: 'branchName',
      key: 'branchName',
      width: 100,
      fixed: 'left',
      ellipsis: true,
      render: (branchName: string, record: BranchPerformance) => (
        <span
          className={styles.branchName}
          style={{
            color: branchColors[(record.rank - 1) % branchColors.length],
          }}
        >
          {branchName}
        </span>
      ),
    },
    {
      title: t('executiveSales.table.revenue'),
      key: 'revenue',
      align: 'center',
      className: styles.revenueHeader,
      children: [
        {
          title: t('executiveSales.table.current'),
          dataIndex: 'revenue',
          key: 'revenueCurrent',
          align: 'right',
          width: 110,
          render: (_: unknown, record: BranchPerformance) => (
            <span
              className={styles.mainValue}
              style={{
                color: branchColors[(record.rank - 1) % branchColors.length],
              }}
            >
              {formatCurrency(record.revenue)}
            </span>
          ),
        },
        {
          title: t('executiveSales.table.ly'),
          dataIndex: 'revenueLY',
          key: 'revenueLY',
          align: 'right',
          width: 100,
          render: (value: number) => (
            <span className={styles.lyValue}>{formatCurrency(value)}</span>
          ),
        },
        {
          title: t('executiveSales.table.yoy'),
          key: 'revenueYoY',
          align: 'right',
          width: 80,
          render: (_: unknown, record: BranchPerformance) => {
            const yoyChange =
              ((record.revenue - record.revenueLY) / record.revenueLY) * 100;
            const isPositive = yoyChange >= 0;
            return (
              <span
                className={`${styles.trendBadge} ${
                  isPositive ? styles.trendPositive : styles.trendNegative
                }`}
              >
                {isPositive ? '+' : ''}
                {yoyChange.toFixed(1)}%
              </span>
            );
          },
        },
      ],
    },
    {
      title: t('executiveSales.table.orderCount'),
      key: 'orderCount',
      align: 'center',
      className: styles.orderCountHeader,
      children: [
        {
          title: t('executiveSales.table.current'),
          dataIndex: 'orderCount',
          key: 'orderCountCurrent',
          align: 'right',
          width: 80,
          render: (_: unknown, record: BranchPerformance) => (
            <span
              className={styles.subValue}
              style={{
                color: branchColors[(record.rank - 1) % branchColors.length],
              }}
            >
              {formatNumber(record.orderCount)}
            </span>
          ),
        },
        {
          title: t('executiveSales.table.ly'),
          dataIndex: 'orderCountLY',
          key: 'orderCountLY',
          align: 'right',
          width: 80,
          render: (value: number) => (
            <span className={styles.lyValue}>{formatNumber(value)}</span>
          ),
        },
        {
          title: t('executiveSales.table.yoy'),
          key: 'orderCountYoY',
          align: 'right',
          width: 70,
          render: (_: unknown, record: BranchPerformance) => {
            const yoyChange =
              record.orderCountLY > 0
                ? ((record.orderCount - record.orderCountLY) /
                    record.orderCountLY) *
                  100
                : record.orderCount > 0
                ? 100
                : 0;
            const isPositive = yoyChange >= 0;
            return (
              <span
                className={`${styles.trendBadge} ${
                  isPositive ? styles.trendPositive : styles.trendNegative
                }`}
              >
                {isPositive ? '+' : ''}
                {yoyChange.toFixed(1)}%
              </span>
            );
          },
        },
      ],
    },
    {
      title: t('executiveSales.table.aov'),
      key: 'aov',
      align: 'center',
      className: styles.aovHeader,
      children: [
        {
          title: t('executiveSales.table.current'),
          dataIndex: 'aov',
          key: 'aovCurrent',
          align: 'right',
          width: 90,
          render: (_: unknown, record: BranchPerformance) => (
            <span
              className={styles.subValue}
              style={{
                color: branchColors[(record.rank - 1) % branchColors.length],
              }}
            >
              {formatCurrency(record.aov)}
            </span>
          ),
        },
        {
          title: t('executiveSales.table.ly'),
          dataIndex: 'aovLY',
          key: 'aovLY',
          align: 'right',
          width: 90,
          render: (value: number) => (
            <span className={styles.lyValue}>{formatCurrency(value)}</span>
          ),
        },
        {
          title: t('executiveSales.table.yoy'),
          key: 'aovYoY',
          align: 'right',
          width: 70,
          render: (_: unknown, record: BranchPerformance) => {
            const yoyChange =
              ((record.aov - record.aovLY) / record.aovLY) * 100;
            const isPositive = yoyChange >= 0;
            return (
              <span
                className={`${styles.trendBadge} ${
                  isPositive ? styles.trendPositive : styles.trendNegative
                }`}
              >
                {isPositive ? '+' : ''}
                {yoyChange.toFixed(1)}%
              </span>
            );
          },
        },
      ],
    },
  ];

  return (
    <div className={styles.tableWrapper}>
      <Table
        columns={columns}
        dataSource={data}
        loading={loading}
        rowKey="rank"
        pagination={false}
        scroll={{ x: 900, y: 540 }}
        className={styles.table}
        onRow={(record) => ({
          className: `${styles.tableRow} ${
            selectedBranchCode === record.branchCode ? styles.selectedRow : ''
          } ${styles.tableRowClickable}`,
          onClick: () =>
            record.branchCode &&
            onBranchSelect?.(record.branchCode, record.branchName),
        })}
      />
    </div>
  );
};

export default BranchPerformanceTable;
