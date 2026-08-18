# autobrr

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/autobrr.svg" alt="" width="72" height="72" />
</p>

[autobrr](https://autobrr.com) watches IRC announce channels and RSS feeds and pushes matching releases the moment they appear, well before RSS-based searching would find them. It has native Sportarr support.

!!! info "Availability"
    Sportarr support is merged in autobrr and ships with their next release after v1.83.0. It requires Sportarr 4.0.1024 or later.

## Download client

1. In autobrr, go to **Settings > Clients** and add **Sportarr**
2. Set the host to your Sportarr URL (e.g. `http://sportarr:1867`) and paste your API key (**Settings > General** in Sportarr)
3. Test and save. The test checks your Sportarr version and tells you if it's too old

## Filter action

In any autobrr filter, add an action of type **Sportarr** and pick your client. Matched releases get pushed to Sportarr, which runs them through the same match, evaluate, and grab decision engine as its own RSS sync and responds with the exact rejection reasons if it passes.

## List source

autobrr can also build filter title lists from your Sportarr library: create a list of type **Sportarr**, pick your client, and your monitored league names feed the filter automatically, with include/exclude by Sportarr tags supported.
