import { routerMiddleware } from 'connected-react-router';
import { applyMiddleware, compose } from 'redux';
import thunk from 'redux-thunk';
import createPersistState from './createPersistState';
import createSentryMiddleware from './createSentryMiddleware';

export default function(history) {
  const middlewares = [];
  const sentryMiddleware = createSentryMiddleware();

  if (sentryMiddleware) {
    middlewares.push(sentryMiddleware);
  }

  middlewares.push(routerMiddleware(history));

  // Add logging middleware before thunk to see what actions are being dispatched
  middlewares.push(store => next => action => {
    if (typeof action === 'function') {
      console.log('[MIDDLEWARE] Function action detected, passing to redux-thunk');
    }
    return next(action);
  });

  middlewares.push(thunk);

  // eslint-disable-next-line no-underscore-dangle
  const composeEnhancers = window.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__ || compose;

  return composeEnhancers(
    applyMiddleware(...middlewares),
    createPersistState()
  );
}
