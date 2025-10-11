import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import Event from 'Events/Event';

function createMultiEventSelector(seriesIds: number[]) {
  return createSelector(
    (state: AppState) => state.events.itemMap,
    (state: AppState) => state.events.items,
    (itemMap, allSeries) => {
      return seriesIds.reduce((acc: Series[], seriesId) => {
        const series = allSeries[itemMap[seriesId]];

        if (series) {
          acc.push(series);
        }

        return acc;
      }, []);
    }
  );
}

export default createMultiEventSelector;
