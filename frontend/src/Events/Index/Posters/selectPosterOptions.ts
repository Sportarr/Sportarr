import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';

const selectPosterOptions = createSelector(
  (state: AppState) => state.eventIndex.posterOptions,
  (posterOptions) => posterOptions
);

export default selectPosterOptions;
