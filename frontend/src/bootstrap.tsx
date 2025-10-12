import { createBrowserHistory } from 'history';
import React from 'react';
import { createRoot } from 'react-dom/client';
import createAppStore from 'Store/createAppStore';
import App from './App/App';

import 'Diag/ConsoleApi';

export async function bootstrap() {
  const history = createBrowserHistory();
  const store = createAppStore(history);

  // Expose store for debugging
  (window as any).store = store;

  // Log all dispatched actions and state changes
  const originalDispatch = store.dispatch;
  store.dispatch = function(action: any) {
    console.log('[REDUX DISPATCH]', action);
    const result = originalDispatch.call(this, action);

    // Log populated state after each action
    const state = store.getState();
    const populated = {
      events: state.events?.isPopulated,
      customFilters: state.customFilters?.isPopulated,
      tags: state.tags?.isPopulated,
      ui: state.settings?.ui?.isPopulated,
      qualityProfiles: state.settings?.qualityProfiles?.isPopulated,
      languages: state.settings?.languages?.isPopulated,
      importLists: state.settings?.importLists?.isPopulated,
      indexerFlags: state.settings?.indexerFlags?.isPopulated,
      systemStatus: state.system?.status?.isPopulated,
      translations: state.app?.translations?.isPopulated
    };

    if (action.type && (action.type.includes('FETCH') || action.type.includes('SET'))) {
      console.log('[REDUX POPULATED STATE]', populated);
    }

    return result;
  };

  // Log initial state
  console.log('[REDUX INITIAL STATE]', store.getState());

  const container = document.getElementById('root');

  const root = createRoot(container!); // createRoot(container!) if you use TypeScript
  root.render(<App store={store} history={history} />);
}
