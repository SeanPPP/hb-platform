import { useTranslation } from 'react-i18next';
import { Spin } from 'antd';
import React from 'react';
import styles from './HourlyTrafficDensity.module.css';

interface HourlyData {
  hour: string;
  revenue: number;
  revenueLY: number;
  percentage: number;
  isPeak?: boolean;
  branchCode?: string;
  branchName?: string;
}

interface HourlyTrafficDensityProps {
  data: HourlyData[];
  loading?: boolean;
  maxHeight?: number;
}

const formatCurrency = (value: number): string => {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(value);
};

const HourlyTrafficDensity: React.FC<HourlyTrafficDensityProps> = ({
  data,
  loading = false,
  maxHeight = 500,
}) => {
  const { t } = useTranslation();
  return (
    <div className={styles.container} style={{ maxHeight }}>
      {loading ? (
        <div className={styles.loading}>
          <Spin />
        </div>
      ) : (
        <table className={styles.table}>
          <thead>
            <tr>
              <th className={styles.th}>
                {t('executiveSales.hourly.time')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.hourly.current')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.hourly.ly')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.hourly.yoy')}
              </th>
              <th className={`${styles.th} ${styles.thRight}`}>
                {t('executiveSales.hourly.progress')}
              </th>
            </tr>
          </thead>
          <tbody>
            {data.map((item, index) => {
              const yoyChange =
                item.revenueLY > 0
                  ? ((item.revenue - item.revenueLY) / item.revenueLY) * 100
                  : 0;
              const isPositive = yoyChange >= 0;
              const isPeak = item.isPeak || item.percentage >= 90;

              return (
                <tr
                  key={index}
                  className={`${styles.row} ${isPeak ? styles.peakRow : ''}`}
                >
                  <td className={styles.td}>
                    <span
                      className={`${styles.time} ${
                        isPeak ? styles.timePeak : ''
                      }`}
                    >
                      {item.hour}
                    </span>
                  </td>
                  <td className={`${styles.td} ${styles.tdRight}`}>
                    <span
                      className={`${styles.value} ${
                        isPeak ? styles.valuePeak : ''
                      }`}
                    >
                      {formatCurrency(item.revenue)}
                    </span>
                  </td>
                  <td className={`${styles.td} ${styles.tdRight}`}>
                    <span className={styles.lyValue}>
                      {formatCurrency(item.revenueLY)}
                    </span>
                  </td>
                  <td className={`${styles.td} ${styles.tdRight}`}>
                    <span
                      className={`${styles.yoyChange} ${
                        isPositive ? styles.yoyPositive : styles.yoyNegative
                      }`}
                    >
                      {isPositive ? '+' : ''}
                      {yoyChange.toFixed(1)}%
                    </span>
                  </td>
                  <td className={`${styles.td} ${styles.tdRight}`}>
                    <div className={styles.progressWrapper}>
                      <div className={styles.progressBar}>
                        <div
                          className={`${styles.progressFill} ${
                            isPeak ? styles.progressPeak : ''
                          }`}
                          style={{ width: `${item.percentage}%` }}
                        />
                      </div>
                      <span className={styles.percentage}>
                        {item.percentage}%
                      </span>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
};

export default HourlyTrafficDensity;
