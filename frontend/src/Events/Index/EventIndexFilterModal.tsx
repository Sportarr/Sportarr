import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import FilterModal, { FilterModalProps } from 'Components/Filter/FilterModal';
import Event from 'Events/Event';
import { setEventFilter } from 'Store/Actions/eventIndexActions';

function createEventSelector() {
  return createSelector(
    (state: AppState) => state.events.items,
    (event) => {
      return event;
    }
  );
}

function createFilterBuilderPropsSelector() {
  return createSelector(
    (state: AppState) => state.eventIndexIndex.filterBuilderProps,
    (filterBuilderProps) => {
      return filterBuilderProps;
    }
  );
}

type SeriesIndexFilterModalProps = FilterModalProps<Event>;

export default function SeriesIndexFilterModal(
  props: SeriesIndexFilterModalProps
) {
  const sectionItems = useSelector(createEventSelector());
  const filterBuilderProps = useSelector(createFilterBuilderPropsSelector());

  const dispatch = useDispatch();

  const dispatchSetFilter = useCallback(
    (payload: unknown) => {
      dispatch(setEventFilter(payload));
    },
    [dispatch]
  );

  return (
    <FilterModal
      {...props}
      sectionItems={sectionItems}
      filterBuilderProps={filterBuilderProps}
      customFilterType="event"
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}
