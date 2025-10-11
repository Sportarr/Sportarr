import React from 'react';
import FightCard from 'FightCard/FightCard';
import SeasonEpisodeNumber from 'FightCard/CardNumber';
import Event from 'Events/Event';
import translate from 'Utilities/String/translate';

interface EpisodeCellContentProps {
  episodes: FightCard[];
  isFullSeason: boolean;
  seasonNumber?: number;
  series?: Event;
}

export default function EpisodeCellContent({
  episodes,
  isFullSeason,
  seasonNumber,
  series,
}: EpisodeCellContentProps) {
  if (episodes.length === 0) {
    return '-';
  }

  if (isFullSeason && seasonNumber != null) {
    return translate('SeasonNumberToken', { seasonNumber });
  }

  if (episodes.length === 1) {
    const episode = episodes[0];

    return (
      <SeasonEpisodeNumber
        seasonNumber={episode.seasonNumber}
        episodeNumber={episode.episodeNumber}
        absoluteEpisodeNumber={episode.absoluteEpisodeNumber}
        seriesType={series?.seriesType}
        alternateTitles={series?.alternateTitles}
        sceneSeasonNumber={episode.sceneSeasonNumber}
        sceneEpisodeNumber={episode.sceneEpisodeNumber}
        sceneAbsoluteEpisodeNumber={episode.sceneAbsoluteEpisodeNumber}
        unverifiedSceneNumbering={episode.unverifiedSceneNumbering}
      />
    );
  }

  const firstEpisode = episodes[0];
  const lastEpisode = episodes[episodes.length - 1];

  return (
    <>
      <SeasonEpisodeNumber
        seasonNumber={firstEpisode.seasonNumber}
        episodeNumber={firstEpisode.episodeNumber}
        absoluteEpisodeNumber={firstEpisode.absoluteEpisodeNumber}
        seriesType={series?.seriesType}
        alternateTitles={series?.alternateTitles}
        sceneSeasonNumber={firstEpisode.sceneSeasonNumber}
        sceneEpisodeNumber={firstEpisode.sceneEpisodeNumber}
        sceneAbsoluteEpisodeNumber={firstEpisode.sceneAbsoluteEpisodeNumber}
        unverifiedSceneNumbering={firstEpisode.unverifiedSceneNumbering}
      />
      {' - '}
      <SeasonEpisodeNumber
        seasonNumber={lastEpisode.seasonNumber}
        episodeNumber={lastEpisode.episodeNumber}
        absoluteEpisodeNumber={lastEpisode.absoluteEpisodeNumber}
        seriesType={series?.seriesType}
        alternateTitles={series?.alternateTitles}
        sceneSeasonNumber={lastEpisode.sceneSeasonNumber}
        sceneEpisodeNumber={lastEpisode.sceneEpisodeNumber}
        sceneAbsoluteEpisodeNumber={lastEpisode.sceneAbsoluteEpisodeNumber}
        unverifiedSceneNumbering={lastEpisode.unverifiedSceneNumbering}
      />
    </>
  );
}
