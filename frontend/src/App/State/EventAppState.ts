import AppSectionState, {
  AppSectionDeleteState,
  AppSectionSaveState,
} from 'App/State/AppSectionState';
import Column from 'Components/Table/Column';
import { SortDirection } from 'Helpers/Props/sortDirections';
import Event from 'Events/Event';
import { Filter, FilterBuilderProp } from './AppState';

export interface EventIndexAppState {
  sortKey: string;
  sortDirection: SortDirection;
  secondarySortKey: string;
  secondarySortDirection: SortDirection;
  view: string;

  posterOptions: {
    detailedProgressBar: boolean;
    size: string;
    showTitle: boolean;
    showMonitored: boolean;
    showQualityProfile: boolean;
    showTags: boolean;
    showSearchAction: boolean;
  };

  overviewOptions: {
    detailedProgressBar: boolean;
    size: string;
    showMonitored: boolean;
    showOrganization: boolean;
    showQualityProfile: boolean;
    showPreviousEvent: boolean;
    showAdded: boolean;
    showFightCardCount: boolean;
    showPath: boolean;
    showSizeOnDisk: boolean;
    showTags: boolean;
    showSearchAction: boolean;
  };

  tableOptions: {
    showBanners: boolean;
    showSearchAction: boolean;
  };

  selectedFilterKey: string;
  filterBuilderProps: FilterBuilderProp<Series>[];
  filters: Filter[];
  columns: Column[];
}

interface EventAppState
  extends AppSectionState<Series>,
    AppSectionDeleteState,
    AppSectionSaveState {
  itemMap: Record<number, number>;

  deleteOptions: {
    addImportListExclusion: boolean;
  };

  pendingChanges: Partial<Series>;
}

export default EventAppState;
