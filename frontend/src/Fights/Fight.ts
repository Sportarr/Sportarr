interface Fight {
  id: number;
  fightarrFightId: number;
  fightCardId: number;
  fighter1Id: number;
  fighter1Name: string;
  fighter1Record: string;
  fighter2Id: number;
  fighter2Name: string;
  fighter2Record: string;
  weightClass: string;
  isTitleFight: boolean;
  isMainEvent: boolean;
  fightOrder: number;
  result?: string;
  method?: string;
  round?: number;
  time?: string;
  referee?: string;
  notes?: string;
}

export default Fight;
