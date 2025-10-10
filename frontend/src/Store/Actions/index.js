import * as app from './appActions';
import * as calendar from './calendarActions';
import * as captcha from './captchaActions';
import * as commands from './commandActions';
import * as customFilters from './customFilterActions';
import * as episodes from './episodeActions';
import * as episodeFiles from './episodeFileActions';
import * as episodeHistory from './episodeHistoryActions';
import * as episodeSelection from './episodeSelectionActions';
import * as events from './eventActions';
import * as fightCards from './fightCardActions';
import * as fights from './fightActions';
import * as history from './historyActions';
import * as importEvents from './importEventActions';
import * as interactiveImportActions from './interactiveImportActions';
import * as oAuth from './oAuthActions';
import * as organizePreview from './organizePreviewActions';
import * as parse from './parseActions';
import * as paths from './pathActions';
import * as providerOptions from './providerOptionActions';
import * as releases from './releaseActions';
import * as rootFolders from './rootFolderActions';
import * as eventDetails from './eventDetailActions';
import * as eventHistory from './eventHistoryActions';
import * as eventIndex from './eventIndexActions';
import * as settings from './settingsActions';
import * as system from './systemActions';
import * as tags from './tagActions';
import * as wanted from './wantedActions';

export default [
  app,
  calendar,
  captcha,
  commands,
  customFilters,
  episodes,
  episodeFiles,
  episodeHistory,
  episodeSelection,
  events,
  fightCards,
  fights,
  history,
  importEvents,
  interactiveImportActions,
  oAuth,
  organizePreview,
  parse,
  paths,
  providerOptions,
  releases,
  rootFolders,
  eventDetails,
  eventHistory,
  eventIndex,
  settings,
  system,
  tags,
  wanted
];
