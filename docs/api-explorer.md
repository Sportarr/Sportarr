# Metadata API Explorer

The interactive console for the Sportarr metadata service at sportarr.net. It is generated live from the running service's OpenAPI spec, so it is always current: browse every endpoint, see schemas, and try requests directly from this page.

This is the read-only metadata API used by partner applications and the Sportarr clients, versioned under `/v1` and addressed by canonical short IDs (e.g. `lg-000142`) and slugs (e.g. `nfl`). TheSportsDB-compatible shim endpoints appear here too under their own tags.

For the API the Sportarr application itself exposes to tools like Prowlarr and autobrr, see the [Application API](APPLICATION_API.md) instead.

<swagger-ui src="https://sportarr.net/openapi-partner.json"/>
