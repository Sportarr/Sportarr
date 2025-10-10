import React from 'react';
import { Route } from 'react-router-dom';
import Switch from 'Components/Router/Switch';
import ImportSeries from './Import/ImportEvent';
import ImportSeriesSelectFolder from './SelectFolder/ImportEventSelectFolder';

function ImportSeriesPage() {
  return (
    <Switch>
      <Route
        exact={true}
        path="/add/import"
        component={ImportSeriesSelectFolder}
      />

      <Route path="/add/import/:rootFolderId" component={ImportSeries} />
    </Switch>
  );
}

export default ImportSeriesPage;
