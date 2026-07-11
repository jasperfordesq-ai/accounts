"use client";

import { ArrowDown, ArrowLeftRight, ArrowUp, ArrowUpDown } from "lucide-react";
import { useId, useMemo, useState, type ReactNode } from "react";

type Tone = "default" | "good" | "warn" | "bad" | "info";
type DataTableRowTone = Tone;
type DataTableSortDirection = "asc" | "desc";
type DataTableSortValue = string | number | null | undefined;

export interface DataTableSortState {
  columnIndex: number;
  direction: DataTableSortDirection;
}

export interface DataTableRichRow {
  id?: string | number;
  cells: ReactNode[];
  searchText?: string;
  tone?: DataTableRowTone;
  sortValues?: DataTableSortValue[];
}

export type DataTableRow = ReactNode[] | DataTableRichRow;

export interface DataGridProps {
  columns: string[];
  rows: DataTableRow[];
  caption?: string;
  filterPlaceholder?: string;
  emptyState?: ReactNode;
  totals?: ReactNode[];
  defaultSort?: DataTableSortState | null;
  sortableColumns?: boolean[];
  mobilePresentation?: "cards" | "scroll";
}

export interface HorizontalScrollRegionProps {
  label: string;
  children: ReactNode;
  className?: string;
  stickyFirstColumn?: boolean;
}

export function DataTable({
  ...props
}: DataGridProps) {
  return (
    <DataGridBase
      {...props}
      mobilePresentation={props.mobilePresentation ?? "cards"}
      surfaceClassName="workbench-data-table"
    />
  );
}

export function DataGrid({
  ...props
}: DataGridProps) {
  return (
    <DataGridBase
      {...props}
      mobilePresentation={props.mobilePresentation ?? "scroll"}
      surfaceClassName="workbench-data-grid"
    />
  );
}

export function HorizontalScrollRegion({
  label,
  children,
  className = "",
  stickyFirstColumn = true,
}: HorizontalScrollRegionProps) {
  const regionId = useId();
  const cueId = `${regionId}-scroll-cue`;

  return (
    <div className={`min-w-0 space-y-2 ${className}`}>
      <p
        id={cueId}
        className="flex items-center gap-2 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-xs font-medium text-sky-900 dark:border-sky-800 dark:bg-sky-950/40 dark:text-sky-100"
        data-scroll-instruction="true"
      >
        <ArrowLeftRight aria-hidden="true" className="h-3.5 w-3.5 shrink-0" />
        <span>Swipe horizontally, or focus this table and use the arrow keys. The first column stays visible.</span>
      </p>
      <div
        role="region"
        aria-label={label}
        aria-describedby={cueId}
        tabIndex={0}
        className="max-w-full overflow-x-auto overscroll-x-contain rounded-md focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
        data-horizontal-scroll-region="true"
        data-sticky-first-column={stickyFirstColumn ? "true" : "false"}
      >
        {children}
      </div>
    </div>
  );
}

function DataGridBase({
  columns,
  rows,
  caption,
  filterPlaceholder,
  emptyState = "No rows to show",
  totals,
  defaultSort = null,
  sortableColumns,
  mobilePresentation = "scroll",
  surfaceClassName,
}: DataGridProps & { surfaceClassName: string }) {
  const tableInstanceId = useId();
  const scrollCueId = `${tableInstanceId}-scroll-cue`;
  const filterInputId = `${tableInstanceId}-filter`;
  const usesMobileCards = mobilePresentation === "cards";
  const [filter, setFilter] = useState("");
  const normalizedRows = useMemo(() => rows.map(normalizeDataTableRow), [rows]);
  const sortableColumnState = useMemo(() => columns.map((_, columnIndex) => {
    if (sortableColumns?.[columnIndex] !== true) return false;
    const values = normalizedRows.map((row) => row.sortValues[columnIndex]);
    return values.some((value) => value !== null && value !== undefined && value !== "")
      && values.every((value) => value === null
        || value === undefined
        || typeof value === "string"
        || typeof value === "number");
  }), [columns, normalizedRows, sortableColumns]);
  const isColumnSortable = (columnIndex: number) =>
    sortableColumnState[columnIndex] === true;
  const [sortState, setSortState] = useState<DataTableSortState | null>(
    defaultSort && sortableColumns?.[defaultSort.columnIndex] === true ? defaultSort : null,
  );
  const normalizedFilter = filter.trim().toLowerCase();
  const visibleRows = useMemo(() => {
    const filteredRows = normalizedFilter
      ? normalizedRows.filter((row) => row.searchText.toLowerCase().includes(normalizedFilter))
      : normalizedRows;

    if (!sortState || sortableColumnState[sortState.columnIndex] !== true) return filteredRows;

    return [...filteredRows].sort((left, right) => {
      const comparison = compareDataTableSortValues(
        left.sortValues[sortState.columnIndex],
        right.sortValues[sortState.columnIndex],
      );
      return sortState.direction === "asc" ? comparison : -comparison;
    });
  }, [normalizedFilter, normalizedRows, sortState, sortableColumnState]);
  const tableLabel = caption ?? "Workbench data table";
  const showFilter = Boolean(filterPlaceholder);
  const toggleSort = (columnIndex: number) => {
    if (!isColumnSortable(columnIndex)) return;

    setSortState((current) => {
      if (current?.columnIndex !== columnIndex) {
        return { columnIndex, direction: "asc" };
      }

      return {
        columnIndex,
        direction: current.direction === "asc" ? "desc" : "asc",
      };
    });
  };

  return (
    <div className="min-w-0 space-y-3">
      {showFilter && (
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <label className="sr-only" htmlFor={filterInputId}>
            Filter {tableLabel}
          </label>
          <input
            id={filterInputId}
            type="search"
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            placeholder={filterPlaceholder}
            aria-label={`Filter ${tableLabel}`}
            className="min-h-10 w-full rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 text-sm text-[var(--foreground)] outline-none transition focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/20 sm:max-w-xs"
          />
          <p className="text-xs font-medium text-[var(--muted-foreground)]">
            {visibleRows.length} of {normalizedRows.length} rows
          </p>
        </div>
      )}
      <p
        id={scrollCueId}
        className={`${usesMobileCards ? "hidden md:flex" : "flex"} items-center gap-2 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-xs font-medium text-sky-900 dark:border-sky-800 dark:bg-sky-950/40 dark:text-sky-100`}
        data-scroll-instruction="true"
      >
        <ArrowLeftRight aria-hidden="true" className="h-3.5 w-3.5 shrink-0" />
        <span>Scroll horizontally to review all evidence columns.</span>
        <span className="hidden sm:inline">Focus the table region and use the arrow keys.</span>
      </p>
      <div
        className={`${surfaceClassName} min-w-0 max-w-full overflow-x-auto overscroll-x-contain rounded-md border border-[var(--border)] bg-[var(--surface)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500`}
        data-responsive={usesMobileCards ? "card" : "scroll"}
        data-scroll-affordance="true"
        data-sticky-first-column="true"
        data-workbench-table-shell="true"
        role="region"
        aria-label={`${tableLabel} scrollable table`}
        aria-describedby={scrollCueId}
        tabIndex={0}
      >
      <table className="min-w-full border-collapse text-left text-sm" aria-label={tableLabel}>
        {caption && <caption className="sr-only">{caption}</caption>}
        <thead className="bg-[var(--surface-subtle)] text-xs font-semibold uppercase text-[var(--muted-foreground)]">
          <tr>
            {columns.map((column, columnIndex) => {
              const isSorted = sortState?.columnIndex === columnIndex;
              const isSortable = isColumnSortable(columnIndex);
              return (
                <th
                  key={column}
                  aria-sort={isSortable ? (isSorted ? (sortState.direction === "asc" ? "ascending" : "descending") : "none") : undefined}
                  data-sticky-column={columnIndex === 0 ? "true" : undefined}
                  className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3"
                >
                  {isSortable ? (
                    <button
                      type="button"
                      aria-label={`Sort by ${column}`}
                      onClick={() => toggleSort(columnIndex)}
                      className="inline-flex min-h-7 items-center gap-1.5 rounded-sm text-left font-semibold uppercase text-[var(--muted-foreground)] transition hover:text-[var(--foreground)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
                    >
                      <span>{column}</span>
                      <SortIcon direction={isSorted ? sortState.direction : null} />
                    </button>
                  ) : (
                    <span className="inline-flex min-h-7 items-center text-left font-semibold uppercase text-[var(--muted-foreground)]">
                      {column}
                    </span>
                  )}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--border)]">
          {visibleRows.map((row, rowIndex) => (
            <tr
              key={row.id ?? rowIndex}
              data-tone={row.tone}
              className={`hover:bg-[var(--surface-subtle)] ${dataTableRowToneClass(row.tone)}`}
            >
              {row.cells.map((cell, cellIndex) => (
                <td
                  key={cellIndex}
                  data-label={columns[cellIndex] ?? ""}
                  data-sticky-column={cellIndex === 0 ? "true" : undefined}
                  className="px-4 py-3 align-top text-[var(--foreground)]"
                >
                  {cell}
                </td>
              ))}
            </tr>
          ))}
          {visibleRows.length === 0 && (
            <tr>
              <td colSpan={columns.length} className="px-4 py-6 text-center text-sm text-[var(--muted-foreground)]">
                {emptyState}
              </td>
            </tr>
          )}
        </tbody>
        {totals && (
          <tfoot className="border-t border-[var(--border)] bg-[var(--surface-subtle)] text-sm font-semibold text-[var(--foreground)]">
            <tr>
              {totals.map((cell, cellIndex) => (
                <td
                  key={cellIndex}
                  data-label={columns[cellIndex] ?? ""}
                  data-sticky-column={cellIndex === 0 ? "true" : undefined}
                  className="px-4 py-3 align-top"
                >
                  {cell}
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
      </div>
    </div>
  );
}

function SortIcon({ direction }: { direction: DataTableSortDirection | null }) {
  if (direction === "asc") {
    return <ArrowUp aria-hidden="true" className="h-3.5 w-3.5" />;
  }

  if (direction === "desc") {
    return <ArrowDown aria-hidden="true" className="h-3.5 w-3.5" />;
  }

  return <ArrowUpDown aria-hidden="true" className="h-3.5 w-3.5 opacity-60" />;
}

function normalizeDataTableRow(row: DataTableRow, rowIndex: number): Required<DataTableRichRow> {
  if (Array.isArray(row)) {
    const fallbackId = row.map(cellText).join("|");
    return {
      id: `row-${rowIndex}-${fallbackId || "legacy"}`,
      cells: row,
      searchText: row.map(cellText).join(" "),
      tone: "default",
      sortValues: row.map(cellText),
    };
  }

  const fallbackId = row.cells.map(cellText).join("|");
  return {
    id: row.id ?? `row-${rowIndex}-${fallbackId || "rich"}`,
    cells: row.cells,
    searchText: row.searchText ?? row.cells.map(cellText).join(" "),
    tone: row.tone ?? "default",
    sortValues: row.sortValues ?? row.cells.map(cellText),
  };
}

function compareDataTableSortValues(left: DataTableSortValue, right: DataTableSortValue) {
  if (left === right) return 0;
  if (left === null || left === undefined || left === "") return 1;
  if (right === null || right === undefined || right === "") return -1;
  if (typeof left === "number" && typeof right === "number") return left - right;

  return String(left).localeCompare(String(right), "en-IE", {
    numeric: true,
    sensitivity: "base",
  });
}

function cellText(cell: ReactNode): string {
  if (cell === null || cell === undefined || typeof cell === "boolean") return "";
  if (typeof cell === "string" || typeof cell === "number" || typeof cell === "bigint") {
    return String(cell);
  }
  return "";
}

function dataTableRowToneClass(tone: DataTableRowTone) {
  switch (tone) {
    case "good":
      return "border-l-4 border-l-emerald-400";
    case "warn":
      return "border-l-4 border-l-amber-400 bg-amber-50/35 dark:bg-amber-950/20";
    case "bad":
      return "border-l-4 border-l-red-400 bg-red-50/35 dark:bg-red-950/20";
    case "info":
      return "border-l-4 border-l-sky-400";
    default:
      return "";
  }
}
