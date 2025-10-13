import { Dispatch } from 'redux';
import AppState from 'App/State/AppState';

type GetState = () => AppState;
type Thunk = (
  getState: GetState,
  identityFn: never,
  dispatch: Dispatch
) => unknown;

const thunks: Record<string, Thunk> = {};

// Expose for debugging
(window as any).__THUNKS_REGISTRY__ = thunks;

function identity<T, TResult>(payload: T): TResult {
  return payload as unknown as TResult;
}

export function createThunk(type: string, identityFunction = identity) {
  return function <T>(payload?: T) {
    return function (dispatch: Dispatch, getState: GetState) {
      console.log(`[createThunk EXECUTING] Looking up thunk for type: "${type}"`);

      // Access the global registry to handle module duplication issues
      const globalThunks = (window as any).__THUNKS_REGISTRY__;
      const thunk = globalThunks?.[type] || thunks[type];

      console.log(`[createThunk EXECUTING] Found thunk:`, !!thunk);

      if (thunk) {
        const finalPayload = payload ?? {};
        console.log(`[createThunk EXECUTING] Calling thunk handler for "${type}" with payload:`, finalPayload);

        return thunk(getState, identityFunction(finalPayload), dispatch);
      }

      console.error(`[createThunk] Thunk handler NOT FOUND for type: "${type}"`);
      console.error(`[createThunk] Local thunk types:`, Object.keys(thunks));
      console.error(`[createThunk] Global thunk types:`, globalThunks ? Object.keys(globalThunks) : 'none');
      throw Error(`Thunk handler has not been registered for ${type}`);
    };
  };
}

export function handleThunks(handlers: Record<string, Thunk>) {
  const types = Object.keys(handlers);

  console.log('[handleThunks] Registering handlers for types:', types);

  types.forEach((type) => {
    thunks[type] = handlers[type];
  });

  console.log('[handleThunks] Total registered thunks:', Object.keys(thunks).length);
}
