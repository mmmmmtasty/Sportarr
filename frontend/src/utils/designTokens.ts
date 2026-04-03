/**
 * Design tokens for Sportarr's compact table and card layouts.
 *
 * Changing a value here propagates everywhere it is referenced, making it easy
 * to tune table density, card spacing, or badge styles globally.
 *
 * TAILWIND JIT NOTE: All values are full Tailwind class strings, not dynamic
 * fragments. Tailwind's content scanner picks them up from this file directly,
 * so generated CSS will always include them.
 */

// ─── Table — header cells ────────────────────────────────────────────────────

/** Default <th> padding used by SortableFilterableHeader. */
export const TABLE_HEADER_CLS = 'px-3 py-1.5';

// ─── Table — data cells ──────────────────────────────────────────────────────

/** Vertical padding for all compact table data rows. */
export const TABLE_CELL_PY = 'py-1.5';

/** Horizontal padding for title / name / label columns (left-aligned text). */
export const TABLE_CELL_PX_LABEL = 'px-3';

/** Horizontal padding for numeric / status / badge columns (data columns). */
export const TABLE_CELL_PX_DATA = 'px-2';

/** Combined class for a standard compact data cell (label column). */
export const TABLE_CELL_LABEL = `${TABLE_CELL_PX_LABEL} ${TABLE_CELL_PY}`;

/** Combined class for a standard compact data cell (data column). */
export const TABLE_CELL_DATA = `${TABLE_CELL_PX_DATA} ${TABLE_CELL_PY}`;

// ─── Table — rows ────────────────────────────────────────────────────────────

/** Hover + text style applied to every clickable compact table row. */
export const TABLE_ROW_HOVER = 'hover:bg-gray-800/50 transition-colors text-sm';

// ─── Cards ───────────────────────────────────────────────────────────────────

/** Inner padding for a content card. */
export const CARD_PADDING = 'p-4';

/** Gap between cards in a card grid. */
export const CARD_GAP = 'gap-4';

/** Standard responsive card grid (1 col → 2 cols at lg breakpoint). */
export const CARD_GRID = `grid grid-cols-1 lg:grid-cols-2 ${CARD_GAP} ${CARD_PADDING}`;

// ─── Layout ──────────────────────────────────────────────────────────────────

/** Standard page-level padding used by Activity, Wanted, Calendar, Leagues, etc. */
export const PAGE_PADDING = 'p-4 md:p-8';

// ─── Compact view breakpoint ─────────────────────────────────────────────────

/**
 * Viewport width (px) at which 'auto' mode switches from card grid to compact
 * table. Matches Tailwind's `xl:` breakpoint.
 * Also referenced in useCompactView and useIsWideScreen.
 */
export const COMPACT_VIEW_BREAKPOINT = 1280;

// ─── Scrollable containers ───────────────────────────────────────────────────

/** Constrained-height scrollable list (league picker, rename preview, etc.). */
export const SCROLLABLE_LIST = 'max-h-60 overflow-y-auto';

// ─── Badges ──────────────────────────────────────────────────────────────────

/** Base classes shared by all inline badges. */
export const BADGE_BASE = 'text-xs rounded whitespace-nowrap';

/** Neutral/gray badge — quality profiles, IDs, generic labels. */
export const BADGE_GRAY = `px-2 py-0.5 bg-gray-800 text-gray-300 ${BADGE_BASE}`;

/** Green badge — downloaded, following, success states. */
export const BADGE_GREEN = `px-1.5 py-0.5 bg-green-900/30 text-green-400 ${BADGE_BASE}`;

/** Red badge — missing, error, unmonitored states. */
export const BADGE_RED = `px-1.5 py-0.5 bg-red-900/30 text-red-400 ${BADGE_BASE}`;

/** Blue badge — info, protocol, indexer labels. */
export const BADGE_BLUE = `px-1.5 py-0.5 bg-blue-900/30 text-blue-400 ${BADGE_BASE}`;

/** Amber/yellow badge — warnings, cutoff-unmet, pending states. */
export const BADGE_AMBER = `px-1.5 py-0.5 bg-amber-900/30 text-amber-400 ${BADGE_BASE}`;

/** Purple badge — quality labels, video codec/encoding, import status. */
export const BADGE_PURPLE = `px-1.5 py-0.5 bg-purple-900/30 text-purple-400 ${BADGE_BASE}`;
