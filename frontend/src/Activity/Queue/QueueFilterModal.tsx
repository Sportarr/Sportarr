import React, { useCallback } from 'react';
import FilterModal, { FilterModalProps } from 'Components/Filter/FilterModal';
import { setQueueOption } from './queueOptionsStore';
import useQueue, { FILTER_BUILDER } from './useQueue';

type QueueFilterModalProps = FilterModalProps<History>;

export default function QueueFilterModal(props: QueueFilterModalProps) {
  const { data } = useQueue();
  const customFilterType = 'queue';

  const dispatchSetFilter = useCallback(
    ({ selectedFilterKey }: { selectedFilterKey: string | number }) => {
      setQueueOption('selectedFilterKey', selectedFilterKey);
    },
    []
  );

  return (
    <FilterModal
      {...props}
      sectionItems={data?.records ?? []}
      filterBuilderProps={FILTER_BUILDER}
      customFilterType={customFilterType}
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}
