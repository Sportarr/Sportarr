import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { SeriesMonitor, SeriesType } from 'Event/Event';

export interface AddEventOptions {
  rootFolderPath: string;
  monitor: SeriesMonitor;
  qualityProfileId: number;
  seriesType: SeriesType;
  seasonFolder: boolean;
  searchForMissingEpisodes: boolean;
  searchForCutoffUnmetEpisodes: boolean;
  tags: number[];
}

const { useOptions, useOption, setOption } =
  createOptionsStore<AddEventOptions>('add_series_options', () => {
    return {
      rootFolderPath: '',
      monitor: 'all',
      qualityProfileId: 0,
      seriesType: 'standard',
      seasonFolder: true,
      searchForMissingEpisodes: false,
      searchForCutoffUnmetEpisodes: false,
      tags: [],
    };
  });

export const useAddEventOptions = useOptions;
export const useAddEventOption = useOption;
export const setAddEventOption = setOption;
