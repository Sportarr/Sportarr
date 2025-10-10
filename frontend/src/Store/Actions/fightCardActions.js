import _ from 'lodash';
import React from 'react';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import Icon from 'Components/Icon';
import { icons, sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import { updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'fightCards';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  sortKey: 'cardNumber',
  sortDirection: sortDirections.ASCENDING,
  items: [],

  columns: [
    {
      name: 'monitored',
      columnLabel: () => translate('Monitored'),
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'cardNumber',
      label: '#',
      isVisible: true,
      isSortable: true
    },
    {
      name: 'cardSection',
      label: () => translate('CardSection'),
      isVisible: true,
      isSortable: true
    },
    {
      name: 'title',
      label: () => translate('Title'),
      isVisible: true,
      isSortable: true
    },
    {
      name: 'airDateUtc',
      label: () => translate('AirDate'),
      isVisible: true,
      isSortable: true
    },
    {
      name: 'fightCount',
      label: () => translate('Fights'),
      isVisible: true,
      isSortable: true
    },
    {
      name: 'size',
      label: () => translate('Size'),
      isVisible: false,
      isSortable: true
    },
    {
      name: 'releaseGroup',
      label: () => translate('ReleaseGroup'),
      isVisible: false
    },
    {
      name: 'customFormats',
      label: () => translate('Formats'),
      isVisible: false
    },
    {
      name: 'customFormatScore',
      columnLabel: () => translate('CustomFormatScore'),
      label: React.createElement(Icon, {
        name: icons.SCORE,
        title: () => translate('CustomFormatScore')
      }),
      isVisible: false,
      isSortable: true
    },
    {
      name: 'status',
      label: () => translate('Status'),
      isVisible: true
    },
    {
      name: 'actions',
      columnLabel: () => translate('Actions'),
      isVisible: true,
      isModifiable: false
    }
  ]
};

export const persistState = [
  'fightCards.sortKey',
  'fightCards.sortDirection',
  'fightCards.columns'
];

//
// Actions Types

export const FETCH_FIGHT_CARDS = 'fightCards/fetchFightCards';
export const SET_FIGHT_CARDS_SORT = 'fightCards/setFightCardsSort';
export const SET_FIGHT_CARDS_TABLE_OPTION = 'fightCards/setFightCardsTableOption';
export const CLEAR_FIGHT_CARDS = 'fightCards/clearFightCards';
export const TOGGLE_FIGHT_CARD_MONITORED = 'fightCards/toggleFightCardMonitored';

//
// Action Creators

export const fetchFightCards = createThunk(FETCH_FIGHT_CARDS);
export const setFightCardsSort = createAction(SET_FIGHT_CARDS_SORT);
export const setFightCardsTableOption = createAction(SET_FIGHT_CARDS_TABLE_OPTION);
export const clearFightCards = createAction(CLEAR_FIGHT_CARDS);
export const toggleFightCardMonitored = createThunk(TOGGLE_FIGHT_CARD_MONITORED);

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_FIGHT_CARDS]: function(getState, payload, dispatch) {
    const eventId = payload.eventId;
    let url = '/fights';

    if (eventId) {
      url = `/fights/event/${eventId}`;
    }

    dispatch(createFetchHandler(section, url)(getState, payload, dispatch));
  },

  [TOGGLE_FIGHT_CARD_MONITORED]: function(getState, payload, dispatch) {
    const {
      fightCardId: id,
      eventId,
      monitored
    } = payload;

    const fightCard = _.find(getState().fightCards.items, { id });

    dispatch(updateItem({
      id,
      section,
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: `/fights/card/${eventId}/${fightCard.cardNumber}`,
      method: 'PUT',
      data: JSON.stringify({
        ...fightCard,
        monitored
      }),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(updateItem({
        id,
        section,
        isSaving: false,
        monitored
      }));
    });

    promise.fail((xhr) => {
      dispatch(updateItem({
        id,
        section,
        isSaving: false
      }));
    });
  }

});

//
// Reducers

export const reducers = createHandleActions({

  [SET_FIGHT_CARDS_SORT]: createSetClientSideCollectionSortReducer(section),

  [SET_FIGHT_CARDS_TABLE_OPTION]: createSetTableOptionReducer(section),

  [CLEAR_FIGHT_CARDS]: (state) => {
    return Object.assign({}, state, defaultState);
  }

}, defaultState, section);
