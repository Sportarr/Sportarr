import Column from 'Components/Table/Column';
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { SortDirection } from 'Helpers/Props/sortDirections';
import translate from 'Utilities/String/translate';

export interface EventOptions {
  pageSize: number;
  selectedFilterKey: string | number;
  sortKey: string;
  sortDirection: SortDirection;
  columns: Column[];
}

const { useOptions, setOptions, setOption } = createOptionsStore<EventOptions>(
  'event_options',
  () => {
    return {
      pageSize: 50,
      selectedFilterKey: 'all',
      sortKey: 'time',
      sortDirection: 'descending',
      columns: [
        {
          name: 'level',
          label: '',
          columnLabel: () => translate('Level'),
          isSortable: false,
          isVisible: true,
          isModifiable: false,
        },
        {
          name: 'time',
          label: () => translate('Time'),
          isSortable: true,
          isVisible: true,
          isModifiable: false,
        },
        {
          name: 'logger',
          label: () => translate('Component'),
          isSortable: false,
          isVisible: true,
          isModifiable: false,
        },
        {
          name: 'message',
          label: () => translate('Message'),
          isVisible: true,
          isModifiable: false,
        },
        {
          name: 'actions',
          label: '',
          columnLabel: () => translate('Actions'),
          isVisible: true,
          isModifiable: false,
        },
      ],
    };
  }
);

export const useEventOptions = useOptions;
export const setEventOptions = setOptions;
export const setEventOption = setOption;
