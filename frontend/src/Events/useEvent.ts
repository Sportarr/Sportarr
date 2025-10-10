import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';

export function createEventSelector(seriesId?: number) {
  return createSelector(
    (state: AppState) => state.events.itemMap,
    (state: AppState) => state.events.items,
    (itemMap, allSeries) => {
      return seriesId ? allSeries[itemMap[seriesId]] : undefined;
    }
  );
}

function useSeries(seriesId?: number) {
  return useSelector(createEventSelector(seriesId));
}

export default useSeries;
