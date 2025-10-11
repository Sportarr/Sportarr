import Event, {
  MonitorNewItems,
  SeriesMonitor,
  SeriesType,
} from 'Events/Event';

interface NewSeriesPayload {
  rootFolderPath: string;
  monitor: SeriesMonitor;
  monitorNewItems: MonitorNewItems;
  qualityProfileId: number;
  seriesType: SeriesType;
  seasonFolder: boolean;
  tags: number[];
  searchForMissingEpisodes?: boolean;
  searchForCutoffUnmetEpisodes?: boolean;
}

function getNewSeries(event: Event, payload: NewSeriesPayload) {
  const {
    rootFolderPath,
    monitor,
    monitorNewItems,
    qualityProfileId,
    seriesType,
    seasonFolder,
    tags,
    searchForMissingEpisodes = false,
    searchForCutoffUnmetEpisodes = false,
  } = payload;

  const addOptions = {
    monitor,
    searchForMissingEpisodes,
    searchForCutoffUnmetEpisodes,
  };

  event.addOptions = addOptions;
  event.monitored = true;
  event.monitorNewItems = monitorNewItems;
  event.qualityProfileId = qualityProfileId;
  event.rootFolderPath = rootFolderPath;
  event.seriesType = seriesType;
  event.seasonFolder = seasonFolder;
  event.tags = tags;

  return event;
}

export default getNewSeries;
