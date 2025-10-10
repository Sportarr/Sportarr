import { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import AddEvent from 'AddEvent/AddEvent';
import { AddEventOptions } from 'AddEvent/addEventOptionsStore';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Event from 'Events/Event';
import { updateItem } from 'Store/Actions/baseActions';

type AddEventPayload = AddEvent & AddEventOptions;

export const useLookupSeries = (query: string) => {
  return useApiQuery<AddEvent[]>({
    path: '/event/lookup',
    queryParams: {
      term: query,
    },
    queryOptions: {
      enabled: !!query,
      // Disable refetch on window focus to prevent refetching when the user switch tabs
      refetchOnWindowFocus: false,
    },
  });
};

export const useAddEvent = () => {
  const dispatch = useDispatch();

  const onAddSuccess = useCallback(
    (data: Event) => {
      dispatch(updateItem({ section: 'event', ...data }));
    },
    [dispatch]
  );

  const { isPending, error, mutate } = useApiMutation<Event, AddEventPayload>(
    {
      path: '/event',
      method: 'POST',
      mutationOptions: {
        onSuccess: onAddSuccess,
      },
    }
  );

  return {
    isAdding: isPending,
    addError: error,
    addSeries: mutate,
  };
};
