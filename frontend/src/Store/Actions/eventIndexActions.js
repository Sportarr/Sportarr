import moment from 'moment';
import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';
import { filterBuilderProps, filterPredicates, filters, sortPredicates } from './eventDetailActions';

//
// Variables

export const section = 'eventIndex';

//
// State

export const defaultState = {
  sortKey: 'sortTitle',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'sortTitle',
  secondarySortDirection: sortDirections.ASCENDING,
  view: 'posters',

  posterOptions: {
    detailedProgressBar: false,
    size: 'large',
    showTitle: false,
    showMonitored: true,
    showQualityProfile: true,
    showTags: false,
    showSearchAction: false
  },

  overviewOptions: {
    detailedProgressBar: false,
    size: 'medium',
    showMonitored: true,
    showOrganization: true,
    showQualityProfile: true,
    showPreviousEvent: false,
    showAdded: false,
    showFightCardCount: true,
    showPath: false,
    showSizeOnDisk: false,
    showTags: false,
    showSearchAction: false
  },

  tableOptions: {
    showBanners: false,
    showSearchAction: false
  },

  columns: [
    {
      name: 'status',
      columnLabel: () => translate('Status'),
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'sortTitle',
      label: () => translate('SeriesTitle'),
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'seriesType',
      label: () => translate('Type'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'network',
      label: () => translate('Network'),
      isSortable: true,
      isVisible: true
    },
    {
      name: 'qualityProfileId',
      label: () => translate('QualityProfile'),
      isSortable: true,
      isVisible: true
    },
    {
      name: 'nextEvent',
      label: () => translate('NextAiring'),
      isSortable: true,
      isVisible: true
    },
    {
      name: 'previousEvent',
      label: () => translate('PreviousAiring'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'originalLanguage',
      label: () => translate('OriginalLanguage'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'added',
      label: () => translate('Added'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'fightCardCount',
      label: () => translate('FightCards'),
      isSortable: true,
      isVisible: true
    },
    {
      name: 'seasonFolder',
      label: () => translate('SeasonFolder'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'episodeProgress',
      label: () => translate('Episodes'),
      isSortable: true,
      isVisible: true
    },
    {
      name: 'episodeCount',
      label: () => translate('EpisodeCount'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'latestSeason',
      label: () => translate('LatestSeason'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'year',
      label: () => translate('Year'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'path',
      label: () => translate('Path'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'sizeOnDisk',
      label: () => translate('SizeOnDisk'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'genres',
      label: () => translate('Genres'),
      isSortable: false,
      isVisible: false
    },
    {
      name: 'ratings',
      label: () => translate('Rating'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'certification',
      label: () => translate('Certification'),
      isSortable: false,
      isVisible: false
    },
    {
      name: 'releaseGroups',
      label: () => translate('ReleaseGroups'),
      isSortable: false,
      isVisible: false
    },
    {
      name: 'tags',
      label: () => translate('Tags'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'useSceneNumbering',
      label: () => translate('SceneNumbering'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'monitorNewItems',
      label: () => translate('MonitorNewSeasons'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'actions',
      columnLabel: () => translate('Actions'),
      isVisible: true,
      isModifiable: false
    }
  ],

  sortPredicates: {
    ...sortPredicates,

    network: function(item) {
      const network = item.network;

      return network ? network.toLowerCase() : '';
    },

    nextEvent: function(item, direction) {
      const nextEvent = item.nextEvent;

      if (nextEvent) {
        return moment(nextEvent).unix();
      }

      if (direction === sortDirections.DESCENDING) {
        return 0;
      }

      return Number.MAX_VALUE;
    },

    previousEvent: function(item, direction) {
      const previousEvent = item.previousEvent;

      if (previousEvent) {
        return moment(previousEvent).unix();
      }

      if (direction === sortDirections.DESCENDING) {
        return -Number.MAX_VALUE;
      }

      return Number.MAX_VALUE;
    },

    episodeProgress: function(item) {
      const { statistics = {} } = item;

      const {
        episodeCount = 0,
        episodeFileCount
      } = statistics;

      const progress = episodeCount ? episodeFileCount / episodeCount * 100 : 100;

      return progress + episodeCount / 1000000;
    },

    episodeCount: function(item) {
      const { statistics = {} } = item;

      return statistics.totalEpisodeCount || 0;
    },

    fightCardCount: function(item) {
      const { statistics = {} } = item;

      return statistics.fightCardCount;
    },

    originalLanguage: function(item) {
      const { originalLanguage = {} } = item;

      return originalLanguage.name;
    },

    ratings: function(item) {
      const { ratings = {} } = item;

      return ratings.value;
    },

    monitorNewItems: function(item) {
      return item.monitorNewItems === 'all' ? 1 : 0;
    }
  },

  selectedFilterKey: 'all',

  filters,

  filterPredicates,

  filterBuilderProps
};

export const persistState = [
  'eventIndex.sortKey',
  'eventIndex.sortDirection',
  'eventIndex.selectedFilterKey',
  'eventIndex.customFilters',
  'eventIndex.view',
  'eventIndex.columns',
  'eventIndex.posterOptions',
  'eventIndex.overviewOptions',
  'eventIndex.tableOptions'
];

//
// Actions Types

export const SET_EVENT_SORT = 'eventIndex/setEventSort';
export const SET_EVENT_FILTER = 'eventIndex/setEventFilter';
export const SET_EVENT_VIEW = 'eventIndex/setEventView';
export const SET_EVENT_TABLE_OPTION = 'eventIndex/setEventTableOption';
export const SET_EVENT_POSTER_OPTION = 'eventIndex/setEventPosterOption';
export const SET_EVENT_OVERVIEW_OPTION = 'eventIndex/setEventOverviewOption';

//
// Action Creators

export const setEventSort = createAction(SET_EVENT_SORT);
export const setEventFilter = createAction(SET_EVENT_FILTER);
export const setEventView = createAction(SET_EVENT_VIEW);
export const setEventTableOption = createAction(SET_EVENT_TABLE_OPTION);
export const setEventPosterOption = createAction(SET_EVENT_POSTER_OPTION);
export const setEventOverviewOption = createAction(SET_EVENT_OVERVIEW_OPTION);

//
// Reducers

export const reducers = createHandleActions({

  [SET_EVENT_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_EVENT_FILTER]: createSetClientSideCollectionFilterReducer(section),

  [SET_EVENT_VIEW]: function(state, { payload }) {
    return Object.assign({}, state, { view: payload.view });
  },

  [SET_EVENT_TABLE_OPTION]: createSetTableOptionReducer(section),

  [SET_EVENT_POSTER_OPTION]: function(state, { payload }) {
    const posterOptions = state.posterOptions;

    return {
      ...state,
      posterOptions: {
        ...posterOptions,
        ...payload
      }
    };
  },

  [SET_EVENT_OVERVIEW_OPTION]: function(state, { payload }) {
    const overviewOptions = state.overviewOptions;

    return {
      ...state,
      overviewOptions: {
        ...overviewOptions,
        ...payload
      }
    };
  }

}, defaultState, section);
