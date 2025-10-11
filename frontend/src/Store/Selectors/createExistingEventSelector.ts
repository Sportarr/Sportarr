import { createSelector } from 'reselect';
import createAllEventsSelector from './createAllEventsSelector';

function createExistingEventSelector(tvdbId: number | undefined) {
  return createSelector(createAllEventsSelector(), (series) => {
    if (tvdbId == null) {
      return false;
    }

    return series.some((s) => s.tvdbId === tvdbId);
  });
}

export default createExistingEventSelector;
