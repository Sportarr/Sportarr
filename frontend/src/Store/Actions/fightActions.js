import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import translate from 'Utilities/String/translate';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';

//
// Variables

export const section = 'fights';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  sortKey: 'fightOrder',
  sortDirection: sortDirections.ASCENDING,
  items: []
};

export const persistState = [
  'fights.sortKey',
  'fights.sortDirection'
];

//
// Actions Types

export const FETCH_FIGHTS = 'fights/fetchFights';
export const SET_FIGHTS_SORT = 'fights/setFightsSort';
export const CLEAR_FIGHTS = 'fights/clearFights';

//
// Action Creators

export const fetchFights = createThunk(FETCH_FIGHTS);
export const setFightsSort = createAction(SET_FIGHTS_SORT);
export const clearFights = createAction(CLEAR_FIGHTS);

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_FIGHTS]: function(getState, payload, dispatch) {
    const { eventId, cardNumber } = payload;
    let url = '/fights';

    if (eventId && cardNumber) {
      url = `/fights/card/${eventId}/${cardNumber}`;
    } else if (eventId) {
      url = `/fights/event/${eventId}`;
    }

    dispatch(createFetchHandler(section, url)(getState, payload, dispatch));
  }

});

//
// Reducers

export const reducers = createHandleActions({

  [SET_FIGHTS_SORT]: createSetClientSideCollectionSortReducer(section),

  [CLEAR_FIGHTS]: (state) => {
    return Object.assign({}, state, defaultState);
  }

}, defaultState, section);
