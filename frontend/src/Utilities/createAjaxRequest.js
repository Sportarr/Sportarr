import $ from 'jquery';

const absUrlRegex = /^(https?:)?\/\//i;

function isRelative(ajaxOptions) {
  return !absUrlRegex.test(ajaxOptions.url);
}

function addRootUrl(ajaxOptions) {
  ajaxOptions.url = window.Fightarr.apiRoot + ajaxOptions.url;
}

function addApiKey(ajaxOptions) {
  ajaxOptions.headers = ajaxOptions.headers || {};
  ajaxOptions.headers['X-Api-Key'] = window.Fightarr.apiKey;
}

function addContentType(ajaxOptions) {
  if (
    ajaxOptions.contentType == null &&
    ajaxOptions.dataType === 'json' &&
    (ajaxOptions.method === 'PUT' || ajaxOptions.method === 'POST' || ajaxOptions.method === 'DELETE')) {
    ajaxOptions.contentType = 'application/json';
  }
}

export default function createAjaxRequest(originalAjaxOptions) {
  const requestXHR = new window.XMLHttpRequest();
  let aborted = false;
  let complete = false;

  function abortRequest() {
    if (!complete) {
      aborted = true;
      requestXHR.abort();
    }
  }

  const ajaxOptions = { ...originalAjaxOptions };

  if (isRelative(ajaxOptions)) {
    addRootUrl(ajaxOptions);
    addApiKey(ajaxOptions);
    addContentType(ajaxOptions);
  }

  console.log('[createAjaxRequest] Making request to:', ajaxOptions.url);

  const request = $.ajax({
    xhr: () => requestXHR,
    ...ajaxOptions
  }).then((data) => {
    console.log('[createAjaxRequest] Success for:', ajaxOptions.url, 'Data:', data);
    return data;
  }, (xhr, textStatus, errorThrown) => {
    console.error('[createAjaxRequest] Failed for:', ajaxOptions.url, 'Status:', xhr.status, 'Error:', errorThrown);
    xhr.aborted = aborted;

    return $.Deferred().reject(xhr, textStatus, errorThrown).promise();
  }).always(() => {
    complete = true;
  });

  return {
    request,
    abortRequest
  };
}
