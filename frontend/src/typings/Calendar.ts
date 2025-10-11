import FightCard from 'FightCard/FightCard';

export interface CalendarItem extends Omit<FightCard, 'airDateUtc'> {
  airDateUtc: string;
}

export interface CalendarEvent extends CalendarItem {
  isGroup: false;
}

export interface CalendarEventGroup {
  isGroup: true;
  seriesId: number;
  seasonNumber: number;
  episodeIds: number[];
  events: CalendarItem[];
}

export type CalendarStatus =
  | 'downloaded'
  | 'downloading'
  | 'unmonitored'
  | 'onAir'
  | 'missing'
  | 'unaired';
