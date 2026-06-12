import { LoadingOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Modal, Spin, Table } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import React, { useMemo, useState } from 'react';
import styles from '../styles.module.css';

export interface ProductSalesData {
  rank: number;
  skuCode: string;
  productName: string;
  productImage?: string;
  quantity: number;
  quantityLY: number;
  discountQuantity: number;
  discountQuantityLY: number;
  avgPrice: number;
  avgPriceLY: number;
  grossTotal: number;
  grossTotalLY: number;
}

export interface SalesDetailTableProps {
  data: ProductSalesData[];
  loading?: boolean;
  totalCount: number;
  currentPage: number;
  pageSize: number;
  onPageChange: (page: number, pageSize: number) => void;
  filterBranch?: string | null;
  filterChinaSupplier?: string | null;
  filterLocalSupplier?: string | null;
}

const formatNumber = (value: number): string => {
  return new Intl.NumberFormat('en-US').format(value);
};

const formatCurrency = (value: number): string => {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
};

const calculateGrowth = (
  current: number,
  previous: number,
): { value: string; isPositive: boolean } => {
  if (previous === 0) {
    return { value: '+100%', isPositive: true };
  }
  const growth = ((current - previous) / previous) * 100;
  const isPositive = growth >= 0;
  return {
    value: `${isPositive ? '+' : ''}${growth.toFixed(1)}%`,
    isPositive,
  };
};

const getPageNumbers = (
  current: number,
  total: number,
): (number | 'ellipsis')[] => {
  const delta = 2;
  const range: number[] = [];
  const rangeWithDots: (number | 'ellipsis')[] = [];

  for (let i = 1; i <= total; i++) {
    if (
      i === 1 ||
      i === total ||
      (i >= current - delta && i <= current + delta)
    ) {
      range.push(i);
    }
  }

  let prev = 0;
  for (const i of range) {
    if (prev !== 0) {
      if (i - prev === 2) {
        rangeWithDots.push(prev + 1);
      } else if (i - prev !== 1) {
        rangeWithDots.push('ellipsis');
      }
    }
    rangeWithDots.push(i);
    prev = i;
  }

  return rangeWithDots;
};

const SalesDetailTable: React.FC<SalesDetailTableProps> = ({
  data,
  loading = false,
  totalCount,
  currentPage,
  pageSize,
  onPageChange,
  filterBranch,
  filterChinaSupplier,
  filterLocalSupplier,
}) => {
  const { t } = useTranslation();
  const [previewVisible, setPreviewVisible] = useState(false);
  const [previewImage, setPreviewImage] = useState('');

  const totalPages = Math.ceil(totalCount / pageSize);
  const startItem = (currentPage - 1) * pageSize + 1;
  const endItem = Math.min(currentPage * pageSize, totalCount);

  const pageNumbers = useMemo(
    () => getPageNumbers(currentPage, totalPages),
    [currentPage, totalPages],
  );

  const handlePageSizeChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    onPageChange(1, parseInt(e.target.value, 10));
  };

  const hasFilter = filterBranch || filterChinaSupplier || filterLocalSupplier;
  const filterLabels: string[] = [];
  if (filterBranch) {
    filterLabels.push(`Branch: ${filterBranch}`);
  }
  if (filterChinaSupplier) {
    filterLabels.push(`China Supplier: ${filterChinaSupplier}`);
  }
  if (filterLocalSupplier) {
    filterLabels.push(`Local Supplier: ${filterLocalSupplier}`);
  }
  const filterLabel = filterLabels.join(' | ');

  const columns: ColumnsType<ProductSalesData> = useMemo(
    () => [
      {
        title: t('salesDetailAnalysisV2.table.column.rank'),
        dataIndex: 'rank',
        key: 'rank',
        width: 40,
        align: 'center',
        render: (rank: number) => (
          <span className={styles.rankNumber}>{rank}</span>
        ),
      },
      {
        title: t('salesDetailAnalysisV2.table.column.media'),
        dataIndex: 'productImage',
        key: 'productImage',
        width: 50,
        align: 'center',
        render: (img: string | undefined, record: ProductSalesData) => (
          <div
            style={{
              width: 44,
              height: 44,
              borderRadius: 4,
              overflow: 'hidden',
              background: '#e2e2e8',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            {img ? (
              <img
                src={img}
                alt={record.productName}
                style={{
                  width: 44,
                  height: 44,
                  objectFit: 'cover',
                  cursor: 'pointer',
                }}
                onClick={(e) => {
                  e.stopPropagation();
                  setPreviewImage(img);
                  setPreviewVisible(true);
                }}
              />
            ) : (
              <span style={{ fontSize: 14 }}>📷</span>
            )}
          </div>
        ),
      },
      {
        title: t('salesDetailAnalysisV2.table.column.sku'),
        dataIndex: 'skuCode',
        key: 'skuCode',
        width: 80,
        align: 'center',
        render: (sku: string) => <span className={styles.skuCode}>{sku}</span>,
      },
      {
        title: t('salesDetailAnalysisV2.table.column.product'),
        dataIndex: 'productName',
        key: 'productName',
        width: 160,
        align: 'left',
        render: (name: string) => (
          <div className={styles.productCell}>
            <span className={styles.productName}>{name}</span>
          </div>
        ),
      },
      {
        title: t('salesDetailAnalysisV2.table.column.soldQty'),
        key: 'quantity',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'quantity',
            key: 'quantityCurrent',
            width: 70,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.mainValue}>{formatNumber(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'quantityLY',
            key: 'quantityLY',
            width: 65,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.lyValue}>{formatNumber(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'quantityYoY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: ProductSalesData) => {
              const qtyGrowth = calculateGrowth(
                record.quantity,
                record.quantityLY,
              );
              return (
                <span
                  className={`${styles.trendBadge} ${
                    qtyGrowth.isPositive
                      ? styles.trendPositive
                      : styles.trendNegative
                  }`}
                >
                  {qtyGrowth.value}
                </span>
              );
            },
          },
        ],
      },
      {
        title: t('salesDetailAnalysisV2.table.column.discQty'),
        key: 'discountQuantity',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'discountQuantity',
            key: 'discountQuantityCurrent',
            width: 70,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.mainValue} style={{ color: '#ba1a1a' }}>
                {formatNumber(value)}
              </span>
            ),
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'discountQuantityLY',
            key: 'discountQuantityLY',
            width: 65,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.lyValue}>{formatNumber(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'discountQuantityYoY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: ProductSalesData) => {
              const discQtyGrowth = calculateGrowth(
                record.discountQuantity,
                record.discountQuantityLY,
              );
              return (
                <span
                  className={`${styles.trendBadge} ${
                    discQtyGrowth.isPositive
                      ? styles.trendPositive
                      : styles.trendNegative
                  }`}
                >
                  {discQtyGrowth.value}
                </span>
              );
            },
          },
        ],
      },
      {
        title: t('salesDetailAnalysisV2.table.column.avgPrice'),
        key: 'avgPrice',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'avgPrice',
            key: 'avgPriceCurrent',
            width: 85,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.mainValue}>{formatCurrency(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'avgPriceLY',
            key: 'avgPriceLY',
            width: 80,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.lyValue}>{formatCurrency(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'avgPriceYoY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: ProductSalesData) => {
              const priceGrowth = calculateGrowth(
                record.avgPrice,
                record.avgPriceLY,
              );
              return (
                <span
                  className={`${styles.trendBadge} ${
                    priceGrowth.isPositive
                      ? styles.trendPositive
                      : styles.trendNegative
                  }`}
                >
                  {priceGrowth.value}
                </span>
              );
            },
          },
        ],
      },
      {
        title: t('salesDetailAnalysisV2.table.column.grossTotal'),
        key: 'grossTotal',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'grossTotal',
            key: 'grossTotalCurrent',
            width: 100,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.grossTotal}>{formatCurrency(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'grossTotalLY',
            key: 'grossTotalLY',
            width: 95,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.lyValue}>{formatCurrency(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'grossTotalYoY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: ProductSalesData) => {
              const grossGrowth = calculateGrowth(
                record.grossTotal,
                record.grossTotalLY,
              );
              return (
                <span
                  className={`${styles.trendBadge} ${
                    grossGrowth.isPositive
                      ? styles.trendPositive
                      : styles.trendNegative
                  }`}
                >
                  {grossGrowth.value}
                </span>
              );
            },
          },
        ],
      },
    ],
    [t],
  );

  return (
    <div className={styles.detailTable}>
      <div className={styles.detailTableHeader}>
        <div className={styles.detailTableTitle}>
          <h2>
            {t('salesDetailAnalysisV2.table.title')}
          </h2>
          <div className={styles.tableTags}>
            <span className={`${styles.tag} ${styles.liveSync}`}>
              {t('salesDetailAnalysisV2.table.liveSync')}
            </span>
            <span className={`${styles.tag} ${styles.recordCount}`}>
              {formatNumber(totalCount)}{' '}
              {t('salesDetailAnalysisV2.table.records')}
            </span>
            {hasFilter && (
              <span
                className={`${styles.tag}`}
                style={{ background: '#003670', color: '#fff' }}
              >
                {filterLabel}
              </span>
            )}
          </div>
        </div>

        <div className={styles.tableControls}>
          <div className={styles.pageSizeControl}>
            <span>
              {t('salesDetailAnalysisV2.table.show')}:
            </span>
            <select value={pageSize} onChange={handlePageSizeChange}>
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={200}>200</option>
              <option value={500}>500</option>
            </select>
          </div>
        </div>
      </div>

      <div className={styles.tableWrapper}>
        {loading ? (
          <div className={styles.loadingOverlay}>
            <Spin indicator={<LoadingOutlined spin />} size="large" />
          </div>
        ) : data.length === 0 ? (
          <div className={styles.emptyState}>
            <span className={styles.emptyIcon}>📊</span>
            <span className={styles.emptyText}>
              {t('salesDetailAnalysisV2.table.noData')}
            </span>
          </div>
        ) : (
          <Table
            columns={columns}
            dataSource={data}
            rowKey="rank"
            size="small"
            pagination={false}
            scroll={{ y: 520 }}
            virtual
            className={styles.virtualTable}
          />
        )}
      </div>

      <div className={styles.tableFooter}>
        <div className={styles.tableInfo}>
          {t('salesDetailAnalysisV2.table.paginationInfo', {
            start: startItem,
            end: endItem,
            total: formatNumber(totalCount),
          })}
        </div>

        <div className={styles.pagination}>
          <button
            type="button"
            className={styles.paginationBtn}
            onClick={() => onPageChange(currentPage - 1, pageSize)}
            disabled={currentPage === 1}
          >
            {t('salesDetailAnalysisV2.table.previous')}
          </button>

          <div className={styles.pageNumbers}>
            {pageNumbers.map((page, index) =>
              page === 'ellipsis' ? (
                <button
                  type="button"
                  key={`ellipsis-${index}`}
                  className={`${styles.pageNumber} ${styles.ellipsis}`}
                >
                  ...
                </button>
              ) : (
                <button
                  type="button"
                  key={page}
                  className={`${styles.pageNumber} ${
                    currentPage === page ? styles.active : ''
                  }`}
                  onClick={() => onPageChange(page, pageSize)}
                >
                  {page}
                </button>
              ),
            )}
          </div>

          <button
            type="button"
            className={styles.paginationBtn}
            onClick={() => onPageChange(currentPage + 1, pageSize)}
            disabled={currentPage === totalPages}
          >
            {t('salesDetailAnalysisV2.table.next')}
          </button>
        </div>
      </div>

      <Modal
        open={previewVisible}
        footer={null}
        onCancel={() => setPreviewVisible(false)}
        width={600}
        centered
        bodyStyle={{ padding: 0, background: 'transparent' }}
      >
        <img
          alt="preview"
          style={{ width: '100%', display: 'block' }}
          src={previewImage}
        />
      </Modal>
    </div>
  );
};

export default SalesDetailTable;
