import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import createAllEventsSelector from './createAllEventsSelector';

function createQualityProfileInUseSelector(id: number | undefined) {
  return createSelector(
    createAllEventsSelector(),
    (state: AppState) => state.settings.importLists.items,
    (series, lists) => {
      if (!id) {
        return false;
      }

      return (
        series.some((s) => s.qualityProfileId === id) ||
        lists.some((list) => list.qualityProfileId === id)
      );
    }
  );
}

export default createQualityProfileInUseSelector;
