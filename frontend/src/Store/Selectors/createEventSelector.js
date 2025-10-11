import { createSelector } from 'reselect';

export function createSeriesSelectorForHook(seriesId) {
  return createSelector(
    (state) => state.events.itemMap,
    (state) => state.events.items,
    (itemMap, allSeries) => {
      return seriesId ? allSeries[itemMap[seriesId]] : undefined;
    }
  );
}

function createEventSelector() {
  return createSelector(
    (state, { seriesId }) => seriesId,
    (state) => state.events.itemMap,
    (state) => state.events.items,
    (seriesId, itemMap, allSeries) => {
      return allSeries[itemMap[seriesId]];
    }
  );
}

export default createEventSelector;
