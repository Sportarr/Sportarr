# -*- coding: utf-8 -*-

SPORTARR_API_URL = 'https://sportarr.net'


def Start():
    Log.Info("[Sportarr] Agent starting...")
    Log.Info("[Sportarr] API URL: %s" % SPORTARR_API_URL)
    HTTP.CacheTime = 3600


class SportarrAgent(Agent.TV_Shows):
    name = 'Sportarr'
    languages = ['en']
    primary_provider = True
    fallback_agent = False
    accepts_from = ['com.plexapp.agents.localmedia']

    def search(self, results, media, lang, manual):
        Log.Info("[Sportarr] Searching for: %s" % media.show)

        try:
            search_url = "%s/api/metadata/plex/search?title=%s" % (
                SPORTARR_API_URL,
                String.Quote(media.show, usePlus=True)
            )

            if media.year:
                search_url = search_url + "&year=%s" % media.year

            Log.Debug("[Sportarr] Search URL: %s" % search_url)
            response = JSON.ObjectFromURL(search_url)

            if 'results' in response:
                for idx, series in enumerate(response['results'][:10]):
                    score = 100 - (idx * 5)

                    if series.get('title', '').lower() == media.show.lower():
                        score = 100

                    results.Append(MetadataSearchResult(
                        id=str(series.get('id')),
                        name=series.get('title'),
                        year=series.get('year'),
                        score=score,
                        lang=lang
                    ))

                    Log.Info("[Sportarr] Found: %s (ID: %s, Score: %d)" % (
                        series.get('title'), series.get('id'), score
                    ))

        except Exception as e:
            Log.Error("[Sportarr] Search error: %s" % str(e))

    def update(self, metadata, media, lang, force):
        Log.Info("[Sportarr] Updating metadata for ID: %s" % metadata.id)

        try:
            series_url = "%s/api/metadata/plex/series/%s" % (SPORTARR_API_URL, metadata.id)
            Log.Debug("[Sportarr] Series URL: %s" % series_url)
            series = JSON.ObjectFromURL(series_url)

            if series:
                metadata.title = series.get('title')
                metadata.summary = series.get('summary')
                metadata.originally_available_at = None

                if series.get('year'):
                    try:
                        metadata.originally_available_at = Datetime.ParseDate("%s-01-01" % series.get('year'))
                    except:
                        pass

                metadata.studio = series.get('studio')
                metadata.content_rating = series.get('content_rating')

                metadata.genres.clear()
                for genre in series.get('genres', []):
                    metadata.genres.add(genre)

                if series.get('poster_url'):
                    try:
                        metadata.posters[series['poster_url']] = Proxy.Media(
                            HTTP.Request(series['poster_url']).content
                        )
                    except Exception as e:
                        Log.Warn("[Sportarr] Failed to fetch poster: %s" % e)

                if series.get('banner_url'):
                    try:
                        metadata.banners[series['banner_url']] = Proxy.Media(
                            HTTP.Request(series['banner_url']).content
                        )
                    except Exception as e:
                        Log.Warn("[Sportarr] Failed to fetch banner: %s" % e)

                if series.get('fanart_url'):
                    try:
                        metadata.art[series['fanart_url']] = Proxy.Media(
                            HTTP.Request(series['fanart_url']).content
                        )
                    except Exception as e:
                        Log.Warn("[Sportarr] Failed to fetch fanart: %s" % e)

            seasons_url = "%s/api/metadata/plex/series/%s/seasons" % (SPORTARR_API_URL, metadata.id)
            Log.Debug("[Sportarr] Seasons URL: %s" % seasons_url)
            seasons_response = JSON.ObjectFromURL(seasons_url)

            if 'seasons' in seasons_response:
                for season_data in seasons_response['seasons']:
                    season_num = season_data.get('season_number')
                    if season_num in media.seasons:
                        season = metadata.seasons[season_num]
                        season.title = season_data.get('title', "Season %s" % season_num)
                        season.summary = season_data.get('summary', '')

                        if season_data.get('poster_url'):
                            try:
                                season.posters[season_data['poster_url']] = Proxy.Media(
                                    HTTP.Request(season_data['poster_url']).content
                                )
                            except Exception as e:
                                Log.Warn("[Sportarr] Failed to fetch season poster: %s" % e)

                        self.update_episodes(metadata, media, season_num)

        except Exception as e:
            Log.Error("[Sportarr] Update error: %s" % str(e))

    def update_episodes(self, metadata, media, season_num):
        Log.Debug("[Sportarr] Updating episodes for season %s" % season_num)

        try:
            episodes_url = "%s/api/metadata/plex/series/%s/season/%s/episodes" % (
                SPORTARR_API_URL, metadata.id, season_num
            )
            Log.Debug("[Sportarr] Episodes URL: %s" % episodes_url)
            episodes_response = JSON.ObjectFromURL(episodes_url)

            if 'episodes' in episodes_response:
                for ep_data in episodes_response['episodes']:
                    ep_num = ep_data.get('episode_number')

                    if ep_num in media.seasons[season_num].episodes:
                        episode = metadata.seasons[season_num].episodes[ep_num]

                        title = ep_data.get('title', "Episode %s" % ep_num)
                        if ep_data.get('part_name'):
                            title = "%s - %s" % (title, ep_data['part_name'])

                        episode.title = title
                        episode.summary = ep_data.get('summary', '')

                        if ep_data.get('air_date'):
                            try:
                                episode.originally_available_at = Datetime.ParseDate(ep_data['air_date'])
                            except:
                                pass

                        if ep_data.get('duration_minutes'):
                            episode.duration = ep_data['duration_minutes'] * 60 * 1000

                        if ep_data.get('thumb_url'):
                            try:
                                episode.thumbs[ep_data['thumb_url']] = Proxy.Media(
                                    HTTP.Request(ep_data['thumb_url']).content
                                )
                            except Exception as e:
                                Log.Warn("[Sportarr] Failed to fetch episode thumb: %s" % e)

                        Log.Debug("[Sportarr] Updated S%sE%s: %s" % (season_num, ep_num, title))

        except Exception as e:
            Log.Error("[Sportarr] Episodes update error: %s" % str(e))
