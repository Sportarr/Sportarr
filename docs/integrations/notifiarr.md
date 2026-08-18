# Notifiarr

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/notifiarr.svg" alt="" width="72" height="72" />
</p>

Notifiarr turns app events into rich Discord notifications. Sportarr has a native connection that sends events straight to notifiarr.com, so you do not need to run the Notifiarr client or relay anything through a generic webhook.

## Setup

1. Log in at [notifiarr.com](https://notifiarr.com) and enable the **Sportarr** integration on your profile
2. In your Notifiarr profile, create an integration API key for Sportarr. Use that key, not your account key
3. In Notifiarr's integration settings, pick the Discord channel that should receive Sportarr notifications
4. In Sportarr, go to **Settings > Notifications**, add a **Notifiarr** connection, and paste the integration API key
5. Pick the triggers you want (grabs, imports, upgrades, deletes, health, DVR recordings, and the rest) and save
6. Use **Test** on the connection. A test event should arrive in your Discord channel within a few seconds

## What gets sent

Every enabled trigger sends the full event payload: the league, the event with its TheSportsDB id, quality, release details, and file paths where they apply. Notifiarr uses this to build the Discord embed, so richer triggers (grabs, imports) produce richer notifications.

## Troubleshooting

- **Test passes but nothing arrives in Discord**: the integration has no channel selected on notifiarr.com. Pick one in the Sportarr integration settings there
- **Unauthorized errors**: you pasted your Notifiarr account API key. The connection needs the Sportarr integration key from your profile
