import { describe, expect, it } from 'vitest';
import {
  cellKey,
  cellsToChips,
  cellsToRules,
  chipsToRules,
  describeFacetSelection,
  facetRulesMatch,
  gridAxes,
  parseFacetRules,
  rulesToCells,
  rulesToChips,
  serializeFacetRules,
  type FacetSchema,
} from '../eventFacets';

const SCHEMA: FacetSchema = {
  id: 'fis-alpine',
  dimensions: [
    { key: 'gender', label: 'Gender', values: ['Men', 'Women', 'Mixed'] },
    { key: 'discipline', label: 'Discipline', values: ['Downhill', 'Super G', 'Giant Slalom', 'Slalom', 'Combined', 'Parallel'] },
  ],
};
const DISCIPLINES = SCHEMA.dimensions[1].values;
// Men's speed events plus women's slalom, the example from issue #380.
const MEN_SPEED_WOMEN_SLALOM = [
  { gender: ['Men'], discipline: ['Downhill', 'Super G'] },
  { gender: ['Women'], discipline: ['Slalom'] },
];

describe('stored rules', () => {
  it('round trips the backend shape', () => {
    const json = '[{"gender":["Men"],"discipline":"*"}]';
    expect(serializeFacetRules(parseFacetRules(json))).toBe(json);
    expect(serializeFacetRules(null)).toBeNull();
    expect(serializeFacetRules([])).toBe('[]');
  });

  it('reads null, blank and invalid values as all events', () => {
    for (const json of [null, undefined, '', '  ', 'not json', '{"gender":"Men"}', '[1]']) {
      expect(parseFacetRules(json)).toBeNull();
    }
    expect(parseFacetRules('[]')).toEqual([]);
  });

  it('turns a single value into a list and a list with * into a wildcard', () => {
    expect(parseFacetRules('[{"gender":"Men","discipline":["*"]}]')).toEqual([{ gender: ['Men'], discipline: '*' }]);
  });
});

describe('facetRulesMatch', () => {
  it('treats null as all and [] as none', () => {
    expect(facetRulesMatch(null, { gender: 'Men' })).toBe(true);
    expect(facetRulesMatch([], { gender: 'Men' })).toBe(false);
  });

  it('matches wildcards, lists and missing dimensions', () => {
    expect(facetRulesMatch([{ gender: '*' }], { gender: 'Mixed' })).toBe(true);
    expect(facetRulesMatch([{ gender: ['Women'] }], { gender: 'Women', discipline: 'Slalom' })).toBe(true);
    expect(facetRulesMatch([{ gender: ['Women'] }], { gender: 'Men' })).toBe(false);
    expect(facetRulesMatch([{ gender: ['women'] }], { gender: 'Women' })).toBe(true);
  });

  it('lets an unparsed dimension pass but rejects a parsed excluded one', () => {
    expect(facetRulesMatch([{ gender: ['Men'], discipline: ['Downhill'] }], {})).toBe(true);
    expect(facetRulesMatch([{ gender: ['Men'] }], { gender: 'Women' })).toBe(false);
  });

  it('matches exact combinations', () => {
    expect(facetRulesMatch(MEN_SPEED_WOMEN_SLALOM, { gender: 'Men', discipline: 'Super G' })).toBe(true);
    expect(facetRulesMatch(MEN_SPEED_WOMEN_SLALOM, { gender: 'Men', discipline: 'Slalom' })).toBe(false);
    expect(facetRulesMatch(MEN_SPEED_WOMEN_SLALOM, { gender: 'Women', discipline: 'Slalom' })).toBe(true);
    expect(facetRulesMatch(MEN_SPEED_WOMEN_SLALOM, { gender: 'Women', discipline: 'Downhill' })).toBe(false);
  });
});

describe('chips', () => {
  it('shows null as everything on and [] as everything off', () => {
    expect([...rulesToChips(SCHEMA, null)!.gender]).toEqual(['Men', 'Women', 'Mixed']);
    expect(rulesToChips(SCHEMA, [])!.discipline.size).toBe(0);
  });

  it('shows one rule and drops unknown values', () => {
    const chips = rulesToChips(SCHEMA, [{ gender: ['women', 'Boys'], discipline: '*' }])!;
    expect([...chips.gender]).toEqual(['Women']);
    expect([...chips.discipline]).toEqual(DISCIPLINES);
  });

  it('cannot show more than one rule', () => {
    expect(rulesToChips(SCHEMA, MEN_SPEED_WOMEN_SLALOM)).toBeNull();
  });

  it('saves everything as null, an empty row as [] and a full row as *', () => {
    const all = { gender: new Set(['Men', 'Women', 'Mixed']), discipline: new Set(DISCIPLINES) };
    expect(chipsToRules(SCHEMA, all)).toBeNull();
    expect(chipsToRules(SCHEMA, { ...all, gender: new Set() })).toEqual([]);
    expect(chipsToRules(SCHEMA, { ...all, discipline: new Set(['Slalom', 'Downhill']) }))
      .toEqual([{ gender: '*', discipline: ['Downhill', 'Slalom'] }]);
  });
});

describe('grid', () => {
  it('saves the exact combinations and reads them back', () => {
    const cells = new Set([cellKey(['Men', 'Downhill']), cellKey(['Men', 'Super G']), cellKey(['Women', 'Slalom'])]);
    const rules = cellsToRules(SCHEMA, cells);
    expect(rules).toEqual(MEN_SPEED_WOMEN_SLALOM);
    expect(rulesToCells(SCHEMA, rules)).toEqual(cells);
  });

  it('saves a full row as * so a new discipline is monitored too', () => {
    const cells = new Set(DISCIPLINES.map(d => cellKey(['Women', d])));
    expect(cellsToRules(SCHEMA, cells)).toEqual([{ gender: ['Women'], discipline: '*' }]);
  });

  it('saves every cell as null and no cell as []', () => {
    const every = new Set(['Men', 'Women', 'Mixed'].flatMap(g => DISCIPLINES.map(d => cellKey([g, d]))));
    expect(cellsToRules(SCHEMA, every)).toBeNull();
    expect(cellsToRules(SCHEMA, new Set())).toEqual([]);
  });

  it('merges cells into chips when going back to the simple choice', () => {
    const chips = cellsToChips(SCHEMA, rulesToCells(SCHEMA, MEN_SPEED_WOMEN_SLALOM));
    expect([...chips.gender]).toEqual(['Men', 'Women']);
    expect([...chips.discipline]).toEqual(['Downhill', 'Super G', 'Slalom']);
  });

  it('describes the choice', () => {
    expect(describeFacetSelection(SCHEMA, null)).toBe('All events');
    expect(describeFacetSelection(SCHEMA, [])).toBe('No events');
    expect(describeFacetSelection(SCHEMA, MEN_SPEED_WOMEN_SLALOM)).toBe('3 of 18 combinations');
  });
});

describe('grid layout', () => {
  it('puts the dimension with fewer values across so the grid fits a phone', () => {
    const axes = gridAxes(SCHEMA)!;
    expect(axes.columns.key).toBe('gender');
    expect(axes.rows.key).toBe('discipline');
    // Keys and names stay in schema order whichever way the grid is drawn.
    expect(axes.key('Slalom', 'Women')).toBe(cellKey(['Women', 'Slalom']));
    expect(axes.label('Slalom', 'Women')).toBe('Women Slalom');
  });

  it('keeps the schema order when the first dimension is the longer one', () => {
    const flipped: FacetSchema = { id: 'x', dimensions: [SCHEMA.dimensions[1], SCHEMA.dimensions[0]] };
    const axes = gridAxes(flipped)!;
    expect(axes.rows.key).toBe('discipline');
    expect(axes.key('Slalom', 'Women')).toBe(cellKey(['Slalom', 'Women']));
  });
});

