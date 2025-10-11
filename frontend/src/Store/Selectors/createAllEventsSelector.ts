import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';

function createAllEventsSelector() {
  return createSelector(
    (state: AppState) => state.events,
    (series) => {
      return series.items;
    }
  );
}

export default createAllEventsSelector;
