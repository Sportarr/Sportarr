import React from 'react';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';
import Fight from './Fight';
import styles from './FightRow.css';

interface FightRowProps {
  fight: Fight;
}

function FightRow({ fight }: FightRowProps) {
  const {
    fighter1Name,
    fighter1Record,
    fighter2Name,
    fighter2Record,
    weightClass,
    isTitleFight,
    isMainEvent,
    fightOrder,
    result,
    method
  } = fight;

  return (
    <div className={styles.fightRow}>
      <div className={styles.fightOrder}>#{fightOrder}</div>

      <div className={styles.fighters}>
        <div className={styles.fighter}>
          <span className={styles.fighterName}>{fighter1Name}</span>
          <span className={styles.record}>({fighter1Record})</span>
        </div>

        <div className={styles.vs}>vs</div>

        <div className={styles.fighter}>
          <span className={styles.fighterName}>{fighter2Name}</span>
          <span className={styles.record}>({fighter2Record})</span>
        </div>
      </div>

      <div className={styles.details}>
        <Label kind={kinds.INFO}>{weightClass}</Label>

        {isTitleFight && (
          <Label kind={kinds.WARNING}>Title Fight</Label>
        )}

        {isMainEvent && (
          <Label kind={kinds.DANGER}>Main Event</Label>
        )}
      </div>

      {result && (
        <div className={styles.result}>
          <strong>Result:</strong> {result}
          {method && ` via ${method}`}
        </div>
      )}
    </div>
  );
}

export default FightRow;
