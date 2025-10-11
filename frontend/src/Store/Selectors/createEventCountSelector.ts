import { createSelector } from 'reselect';
import createAllEventsSelector from './createAllEventsSelector';

function createEventCountSelector() {
  return createSelector(createAllEventsSelector(), (series) => {
    return series.length;
  });
}

export default createEventCountSelector;
