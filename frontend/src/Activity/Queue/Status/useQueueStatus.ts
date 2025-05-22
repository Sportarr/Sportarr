import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { useQueueOption } from '../queueOptionsStore';

export interface QueueStatus {
  totalCount: number;
  count: number;
  unknownCount: number;
  errors: boolean;
  warnings: boolean;
  unknownErrors: boolean;
  unknownWarnings: boolean;
}

export default function useQueueStatus() {
  const includeUnknownSeriesItems = useQueueOption('includeUnknownSeriesItems');

  const { data } = useApiQuery<QueueStatus>({
    path: '/queue/status',
    queryParams: {
      includeUnknownSeriesItems,
    },
  });

  if (!data) {
    return {
      count: 0,
      errors: false,
      warnings: false,
    };
  }

  const {
    errors,
    warnings,
    unknownErrors,
    unknownWarnings,
    count,
    totalCount,
  } = data;

  if (includeUnknownSeriesItems) {
    return {
      count: totalCount,
      errors: errors || unknownErrors,
      warnings: warnings || unknownWarnings,
    };
  }

  return {
    count,
    errors,
    warnings,
  };
}
