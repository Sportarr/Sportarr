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

  // Log all dispatched actions
  const originalDispatch = store.dispatch;
  store.dispatch = function(action: any) {
    console.log('[REDUX DISPATCH]', action);
    return originalDispatch.call(this, action);
  };

  // Log initial state
  console.log('[REDUX INITIAL STATE]', store.getState());

  const container = document.getElementById('root');

  const root = createRoot(container!); // createRoot(container!) if you use TypeScript
  root.render(<App store={store} history={history} />);
}
