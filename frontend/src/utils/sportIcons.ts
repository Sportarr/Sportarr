// Shared sport icon helpers for screens that benefit from quick visual scanning.

const SPORT_ICONS: Record<string, string> = {
  'American Football': '🏈',
  'Athletics': '🏃',
  'Australian Football': '🏉',
  'Badminton': '🏸',
  'Baseball': '⚾',
  'Basketball': '🏀',
  'Climbing': '🧗',
  'Cricket': '🏏',
  'Cycling': '🚴',
  'Darts': '🎯',
  'Esports': '🎮',
  'Equestrian': '🏇',
  'Extreme Sports': '🪂',
  'Field Hockey': '🏑',
  'Fighting': '🥊',
  'Gaelic': '🏐',
  'Gambling': '🎰',
  'Golf': '⛳',
  'Gymnastics': '🤸',
  'Handball': '🤾',
  'Ice Hockey': '🏒',
  'Lacrosse': '🥍',
  'Motorsport': '🏎️',
  'Multi Sports': '🏅',
  'Netball': '🏀',
  'Rugby': '🏉',
  'Shooting': '🎯',
  'Skating': '⛸️',
  'Skiing': '⛷️',
  'Snooker': '🎱',
  'Soccer': '⚽',
  'Table Tennis': '🏓',
  'Tennis': '🎾',
  'Volleyball': '🏐',
  'Watersports': '🏄',
  'Weightlifting': '🏋️',
  'Wintersports': '🎿',
};

const SPORT_FUZZY_ICONS: Array<[string, string]> = [
  ['boxing', '🥊'],
  ['mma', '🥊'],
  ['mixed martial arts', '🥊'],
  ['ufc', '🥊'],
  ['kickboxing', '🥊'],
  ['muay thai', '🥊'],
  ['wrestling', '🤼'],
  ['sumo', '🤼'],
  ['football', '⚽'],
  ['futsal', '⚽'],
  ['arena football', '🏈'],
  ['canadian football', '🏈'],
  ['gridiron', '🏈'],
  ['racing', '🏎️'],
  ['formula 1', '🏎️'],
  ['f1', '🏎️'],
  ['nascar', '🏎️'],
  ['indycar', '🏎️'],
  ['motogp', '🏍️'],
  ['superbike', '🏍️'],
  ['rally', '🏎️'],
  ['wrc', '🏎️'],
  ['endurance', '🏎️'],
  ['le mans', '🏎️'],
  ['pool', '🎱'],
  ['billiards', '🎱'],
  ['surfing', '🏄'],
  ['swimming', '🏊'],
  ['diving', '🤿'],
  ['rowing', '🚣'],
  ['sailing', '⛵'],
  ['triathlon', '🏊'],
  ['marathon', '🏃'],
  ['track and field', '🏃'],
  ['figure skating', '⛸️'],
  ['speed skating', '⛸️'],
  ['curling', '🥌'],
  ['bobsled', '🛷'],
  ['snowboard', '🏂'],
  ['cross country', '⛷️'],
  ['biathlon', '⛷️'],
  ['fencing', '🤺'],
  ['archery', '🏹'],
  ['powerlifting', '🏋️'],
  ['bodybuilding', '🏋️'],
  ['crossfit', '🏋️'],
  ['cheerleading', '📣'],
  ['chess', '♟️'],
  ['poker', '🃏'],
];

export function getSportIcon(sport: string): string {
  const sportLower = sport.toLowerCase();

  const exactMatch = Object.entries(SPORT_ICONS).find(([key]) => {
    const keyLower = key.toLowerCase();
    return keyLower === sportLower || sportLower.includes(keyLower);
  });

  if (exactMatch) return exactMatch[1];

  for (const [needle, icon] of SPORT_FUZZY_ICONS) {
    if (sportLower.includes(needle)) {
      return icon;
    }
  }

  return '🏆';
}
