import _ from 'lodash';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { filterBuilderTypes, filterBuilderValueTypes, filterTypes, sortDirections } from 'Helpers/Props';
import getFilterTypePredicate from 'Helpers/Props/getFilterTypePredicate';
import { createThunk, handleThunks } from 'Store/thunks';
import sortByProp from 'Utilities/Array/sortByProp';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import dateFilterPredicate from 'Utilities/Date/dateFilterPredicate';
import translate from 'Utilities/String/translate';
import { set, updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import createSaveProviderHandler from './Creators/createSaveProviderHandler';
import createSetSettingValueReducer from './Creators/Reducers/createSetSettingValueReducer';
import { fetchFightCards } from './fightCardActions';

//
// Variables

export const section = 'events';

export const filters = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: []
  },
  {
    key: 'monitored',
    label: () => translate('MonitoredOnly'),
    filters: [
      {
        key: 'monitored',
        value: true,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'unmonitored',
    label: () => translate('UnmonitoredOnly'),
    filters: [
      {
        key: 'monitored',
        value: false,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'upcoming',
    label: () => translate('UpcomingOnly'),
    filters: [
      {
        key: 'status',
        value: 'upcoming',
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'completed',
    label: () => translate('CompletedOnly'),
    filters: [
      {
        key: 'status',
        value: 'completed',
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'missing',
    label: () => translate('MissingFights'),
    filters: [
      {
        key: 'missing',
        value: true,
        type: filterTypes.EQUAL
      }
    ]
  }
];

export const filterPredicates = {
  fightProgress: function(item, filterValue, type) {
    const { statistics = {} } = item;

    const {
      fightCount = 0,
      fightFileCount
    } = statistics;

    const progress = fightCount ?
      fightFileCount / fightCount * 100 :
      100;

    const predicate = getFilterTypePredicate(type);

    return predicate(progress, filterValue);
  },

  missing: function(item) {
    const { statistics = {} } = item;

    return statistics.fightCount - statistics.fightFileCount > 0;
  },

  eventDate: function(item, filterValue, type) {
    return dateFilterPredicate(item.eventDate, filterValue, type);
  },

  added: function(item, filterValue, type) {
    return dateFilterPredicate(item.added, filterValue, type);
  },

  organization: function(item, filterValue, type) {
    const predicate = getFilterTypePredicate(type);
    const { organizationName } = item;

    return predicate(organizationName || '', filterValue);
  },

  fightCardCount: function(item, filterValue, type) {
    const predicate = getFilterTypePredicate(type);
    const fightCardCount = item.statistics ? item.statistics.fightCardCount : 0;

    return predicate(fightCardCount, filterValue);
  },

  sizeOnDisk: function(item, filterValue, type) {
    const predicate = getFilterTypePredicate(type);
    const sizeOnDisk = item.statistics && item.statistics.sizeOnDisk ?
      item.statistics.sizeOnDisk :
      0;

    return predicate(sizeOnDisk, filterValue);
  },

  location: function(item, filterValue, type) {
    const predicate = getFilterTypePredicate(type);
    const { location } = item;

    return predicate(location || '', filterValue);
  }
};

export const sortPredicates = {
  status: function(item) {
    let result = 0;

    if (item.monitored) {
      result += 2;
    }

    if (item.status === 'upcoming') {
      result++;
    }

    return result;
  },

  sizeOnDisk: function(item) {
    const { statistics = {} } = item;

    return statistics.sizeOnDisk || 0;
  },

  eventDate: function(item) {
    return item.eventDate ? new Date(item.eventDate).getTime() : 0;
  }
};

//
// Filter Builder Properties

export const filterBuilderProps = [
  {
    name: 'monitored',
    label: () => translate('Monitored'),
    type: filterBuilderTypes.EXACT,
    valueType: filterBuilderValueTypes.BOOL
  },
  {
    name: 'status',
    label: () => translate('Status'),
    type: filterBuilderTypes.EXACT,
    valueType: filterBuilderValueTypes.EVENT_STATUS
  },
  {
    name: 'organizationName',
    label: () => translate('Organization'),
    type: filterBuilderTypes.EXACT,
    valueType: filterBuilderValueTypes.ORGANIZATION
  },
  {
    name: 'eventType',
    label: () => translate('EventType'),
    type: filterBuilderTypes.EXACT,
    valueType: filterBuilderValueTypes.EVENT_TYPE
  },
  {
    name: 'location',
    label: () => translate('Location'),
    type: filterBuilderTypes.STRING
  },
  {
    name: 'venue',
    label: () => translate('Venue'),
    type: filterBuilderTypes.STRING
  },
  {
    name: 'fightProgress',
    label: () => translate('FightProgress'),
    type: filterBuilderTypes.NUMBER
  },
  {
    name: 'sizeOnDisk',
    label: () => translate('SizeOnDisk'),
    type: filterBuilderTypes.NUMBER,
    valueType: filterBuilderValueTypes.BYTES
  },
  {
    name: 'fightCardCount',
    label: () => translate('FightCardCount'),
    type: filterBuilderTypes.NUMBER
  },
  {
    name: 'eventDate',
    label: () => translate('EventDate'),
    type: filterBuilderTypes.DATE,
    valueType: filterBuilderValueTypes.DATE
  },
  {
    name: 'added',
    label: () => translate('Added'),
    type: filterBuilderTypes.DATE,
    valueType: filterBuilderValueTypes.DATE
  },
  {
    name: 'tags',
    label: () => translate('Tags'),
    type: filterBuilderTypes.ARRAY,
    valueType: filterBuilderValueTypes.TAG
  },
  {
    name: 'eventNumber',
    label: () => translate('EventNumber'),
    type: filterBuilderTypes.STRING
  }
];

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isSaving: false,
  saveError: null,
  isDeleting: false,
  deleteError: null,
  items: [],
  sortKey: 'eventDate',
  sortDirection: sortDirections.DESCENDING,
  pendingChanges: {},
  deleteOptions: {
    addImportListExclusion: false
  }
};

export const persistState = [
  'events.deleteOptions'
];

//
// Actions Types

export const FETCH_EVENTS = 'events/fetchEvents';
export const SET_EVENT_VALUE = 'events/setEventValue';
export const SAVE_EVENT = 'events/saveEvent';
export const DELETE_EVENT = 'events/deleteEvent';

export const TOGGLE_EVENT_MONITORED = 'events/toggleEventMonitored';
export const TOGGLE_FIGHT_CARD_MONITORED = 'events/toggleFightCardMonitored';
export const UPDATE_EVENT_MONITOR = 'events/updateEventMonitor';
export const SAVE_EVENT_EDITOR = 'events/saveEventEditor';
export const BULK_DELETE_EVENTS = 'events/bulkDeleteEvents';

export const SET_DELETE_OPTION = 'events/setDeleteOption';

//
// Action Creators

export const fetchEvents = createThunk(FETCH_EVENTS);
export const saveEvent = createThunk(SAVE_EVENT, (payload) => {
  const newPayload = {
    ...payload
  };

  if (payload.moveFiles) {
    newPayload.queryParams = {
      moveFiles: true
    };
  }

  delete newPayload.moveFiles;

  return newPayload;
});

export const deleteEvent = createThunk(DELETE_EVENT, (payload) => {
  return {
    ...payload,
    queryParams: {
      deleteFiles: payload.deleteFiles,
      addImportListExclusion: payload.addImportListExclusion
    }
  };
});

export const toggleEventMonitored = createThunk(TOGGLE_EVENT_MONITORED);
export const toggleFightCardMonitored = createThunk(TOGGLE_FIGHT_CARD_MONITORED);
export const updateEventMonitor = createThunk(UPDATE_EVENT_MONITOR);
export const saveEventEditor = createThunk(SAVE_EVENT_EDITOR);
export const bulkDeleteEvents = createThunk(BULK_DELETE_EVENTS);

export const setEventValue = createAction(SET_EVENT_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

export const setDeleteOption = createAction(SET_DELETE_OPTION);

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_EVENTS]: createFetchHandler(section, '/events'),
  [SAVE_EVENT]: createSaveProviderHandler(section, '/events'),
  [DELETE_EVENT]: createRemoveItemHandler(section, '/events'),

  [TOGGLE_EVENT_MONITORED]: function(getState, payload, dispatch) {
    const {
      eventId: id,
      monitored
    } = payload;

    const event = _.find(getState().events.items, { id });

    dispatch(updateItem({
      id,
      section,
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: `/events/${id}`,
      method: 'PUT',
      data: JSON.stringify({
        ...event,
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
  },

  [TOGGLE_FIGHT_CARD_MONITORED]: function(getState, payload, dispatch) {
    const {
      eventId,
      cardNumber,
      monitored
    } = payload;

    const event = _.find(getState().events.items, { id: eventId });

    dispatch(updateItem({
      id: eventId,
      section,
      isSaving: true
    }));

    const fightCards = _.cloneDeep(event.fightCards);
    const card = _.find(fightCards, { cardNumber });

    if (card) {
      card.monitored = monitored;
    }

    const promise = createAjaxRequest({
      url: `/events/${eventId}`,
      method: 'PUT',
      data: JSON.stringify({
        ...event,
        fightCards
      }),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(updateItem({
        id: eventId,
        section,
        isSaving: false,
        fightCards
      }));
    });

    promise.fail((xhr) => {
      dispatch(updateItem({
        id: eventId,
        section,
        isSaving: false
      }));
    });
  },

  [SAVE_EVENT_EDITOR]: function(getState, payload, dispatch) {
    dispatch(set({
      section,
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: '/events/editor',
      method: 'PUT',
      data: JSON.stringify(payload),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        ...data.map((event) => {

          const {
            images,
            rootFolderPath,
            statistics,
            ...propsToUpdate
          } = event;

          return updateItem({
            id: event.id,
            section: 'events',
            ...propsToUpdate
          });
        }),

        set({
          section,
          isSaving: false,
          saveError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  },

  [BULK_DELETE_EVENTS]: function(getState, payload, dispatch) {
    dispatch(set({
      section,
      isDeleting: true
    }));

    const promise = createAjaxRequest({
      url: '/events/editor',
      method: 'DELETE',
      data: JSON.stringify(payload),
      dataType: 'json'
    }).request;

    promise.done(() => {
      // SignalR will take care of removing the events from the collection

      dispatch(set({
        section,
        isDeleting: false,
        deleteError: null
      }));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isDeleting: false,
        deleteError: xhr
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_EVENT_VALUE]: createSetSettingValueReducer(section),

  [SET_DELETE_OPTION]: (state, { payload }) => {
    return {
      ...state,
      deleteOptions: {
        ...payload
      }
    };
  }

}, defaultState, section);
