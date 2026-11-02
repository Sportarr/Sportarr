// Event facets, for example gender and discipline in alpine skiing.
//
// The backend (EventFacets) stores the choice as a JSON list of rules. Each
// rule maps a dimension key to a list of values or '*'. A dimension that a
// rule does not name matches any value. null monitors every event and []
// monitors none. Keep the matching here the same as on the backend.

export interface FacetDimension {
  key: string;
  label: string;
  values: string[];
}

export interface FacetSchema {
  id: string;
  dimensions: FacetDimension[];
}

export type FacetRuleValue = string[] | '*';
export type FacetRule = Record<string, FacetRuleValue>;

const sameValue = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();

function toRuleValue(value: unknown): FacetRuleValue | null {
  if (value === '*') return '*';
  if (typeof value === 'string') return [value];
  if (Array.isArray(value) && value.every(item => typeof item === 'string')) {
    return value.includes('*') ? '*' : (value as string[]);
  }
  return null;
}

function toRule(value: unknown): FacetRule | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null;
  const rule: FacetRule = {};
  for (const [key, raw] of Object.entries(value)) {
    const ruleValue = toRuleValue(raw);
    if (ruleValue === null) return null;
    rule[key] = ruleValue;
  }
  return rule;
}

/** Reads the stored value. Returns null (all events) for null, blank or invalid JSON. */
export function parseFacetRules(json: string | null | undefined): FacetRule[] | null {
  if (!json || !json.trim()) return null;
  try {
    const parsed: unknown = JSON.parse(json);
    if (!Array.isArray(parsed)) return null;
    const rules = parsed.map(toRule);
    return rules.every((rule): rule is FacetRule => rule !== null) ? rules : null;
  } catch {
    return null;
  }
}

export function serializeFacetRules(rules: FacetRule[] | null): string | null {
  return rules === null ? null : JSON.stringify(rules);
}

/**
 * True when any rule matches. A dimension that the event does not show
 * passes, so a title the parser cannot read stays monitored.
 */
export function facetRulesMatch(rules: FacetRule[] | null, facets: Record<string, string | undefined>): boolean {
  if (rules === null) return true;
  return rules.some(rule => Object.entries(rule).every(([key, allowed]) => {
    const value = facets[key];
    if (value === undefined || allowed === '*') return true;
    return allowed.some(candidate => sameValue(candidate, value));
  }));
}

/**
 * The choice as one set of values per dimension. Returns null when the rules
 * cannot be shown that way, which is when there is more than one rule.
 */
export function rulesToChips(schema: FacetSchema, rules: FacetRule[] | null): Record<string, Set<string>> | null {
  if (rules !== null && rules.length > 1) return null;
  const rule = rules?.[0];
  const chips: Record<string, Set<string>> = {};
  for (const dimension of schema.dimensions) {
    if (rules !== null && rules.length === 0) {
      chips[dimension.key] = new Set();
      continue;
    }
    const allowed = rule?.[dimension.key];
    chips[dimension.key] = new Set(allowed === undefined || allowed === '*'
      ? dimension.values
      : dimension.values.filter(value => allowed.some(candidate => sameValue(candidate, value))));
  }
  return chips;
}

/**
 * One rule with a list per dimension. A dimension with every value becomes
 * '*', so a value that appears later in the season is monitored too.
 */
export function chipsToRules(schema: FacetSchema, chips: Record<string, Set<string>>): FacetRule[] | null {
  const rule: FacetRule = {};
  let everything = true;
  for (const dimension of schema.dimensions) {
    const selected = dimension.values.filter(value => chips[dimension.key]?.has(value));
    if (selected.length === 0) return [];
    if (selected.length === dimension.values.length) {
      rule[dimension.key] = '*';
    } else {
      rule[dimension.key] = selected;
      everything = false;
    }
  }
  return everything ? null : [rule];
}

export function cellKey(values: string[]): string {
  return values.join('\u001f');
}

export interface GridAxes {
  rows: FacetDimension;
  columns: FacetDimension;
  /** The cell key, always in schema order, whichever way the grid is drawn. */
  key: (row: string, column: string) => string;
  /** The cell name in schema order, for example "Women Slalom". */
  label: (row: string, column: string) => string;
}

/**
 * How to draw the grid of the first two dimensions. The dimension with
 * fewer values goes across, so the grid stays narrow enough for a phone.
 */
export function gridAxes(schema: FacetSchema): GridAxes | null {
  const [first, second] = schema.dimensions;
  if (!first || !second) return null;
  const firstAcross = first.values.length < second.values.length;
  const order = (row: string, column: string) => (firstAcross ? [column, row] : [row, column]);
  return {
    rows: firstAcross ? second : first,
    columns: firstAcross ? first : second,
    key: (row, column) => cellKey(order(row, column)),
    label: (row, column) => order(row, column).join(' '),
  };
}

/** The selected cells of the grid of the first two dimensions. */
export function rulesToCells(schema: FacetSchema, rules: FacetRule[] | null): Set<string> {
  const cells = new Set<string>();
  const [rows, columns] = schema.dimensions;
  if (!rows || !columns) return cells;
  for (const row of rows.values) {
    for (const column of columns.values) {
      if (facetRulesMatch(rules, { [rows.key]: row, [columns.key]: column })) {
        cells.add(cellKey([row, column]));
      }
    }
  }
  return cells;
}

/**
 * Exact combinations, one rule per row. A full row becomes '*' for the
 * columns, so a discipline added later is monitored for that row too.
 */
export function cellsToRules(schema: FacetSchema, cells: Set<string>): FacetRule[] | null {
  const [rows, columns] = schema.dimensions;
  if (!rows || !columns) return cells.size === 0 ? [] : null;
  const rules: FacetRule[] = [];
  let fullRows = 0;
  for (const row of rows.values) {
    const selected = columns.values.filter(column => cells.has(cellKey([row, column])));
    if (selected.length === 0) continue;
    if (selected.length === columns.values.length) {
      fullRows++;
      rules.push({ [rows.key]: [row], [columns.key]: '*' });
    } else {
      rules.push({ [rows.key]: [row], [columns.key]: selected });
    }
  }
  if (fullRows === rows.values.length) return null;
  return rules;
}

/** Merges grid cells into one set of values per dimension. This can widen the choice. */
export function cellsToChips(schema: FacetSchema, cells: Set<string>): Record<string, Set<string>> {
  const [rows, columns] = schema.dimensions;
  const chips: Record<string, Set<string>> = {};
  for (const dimension of schema.dimensions) chips[dimension.key] = new Set(dimension.values);
  if (!rows || !columns) return chips;
  chips[rows.key] = new Set(rows.values.filter(row => columns.values.some(column => cells.has(cellKey([row, column])))));
  chips[columns.key] = new Set(columns.values.filter(column => rows.values.some(row => cells.has(cellKey([row, column])))));
  return chips;
}

/** Short text for the dialog footer. */
export function describeFacetSelection(schema: FacetSchema, rules: FacetRule[] | null): string {
  if (rules === null) return 'All events';
  if (rules.length === 0) return 'No events';
  const [rows, columns] = schema.dimensions;
  if (!rows || !columns) return 'Some events';
  return `${rulesToCells(schema, rules).size} of ${rows.values.length * columns.values.length} combinations`;
}
