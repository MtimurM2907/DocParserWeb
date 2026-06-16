import { useCallback, useEffect, useMemo, useState } from 'react';
import { fetchAuditLog } from '../api/backend';
import { AppSelect } from './AppSelect';
import {
  AUDIT_ACTION_LABELS,
  AUDIT_SORT_LABELS,
  auditActionLabel,
  type AuditSortField,
} from '../lib/auditLabels';
import type { AuditLogEntry } from '../types/api';

type Props = {
  token: string;
};

const PAGE_SIZE = 25;

function SortHeader({
  field,
  current,
  sortDesc,
  onSort,
}: {
  field: AuditSortField;
  current: AuditSortField;
  sortDesc: boolean;
  onSort: (field: AuditSortField) => void;
}) {
  const active = current === field;
  const arrow = active ? (sortDesc ? ' ↓' : ' ↑') : '';
  return (
    <th>
      <button
        type="button"
        className={`audit-sort-btn${active ? ' audit-sort-btn--active' : ''}`}
        onClick={() => onSort(field)}
      >
        {AUDIT_SORT_LABELS[field]}
        {arrow}
      </button>
    </th>
  );
}

function formatDetails(details: string | null | undefined): string {
  if (!details) return '—';
  if (details.startsWith('hash=') && details.length > 40) {
    return `${details.slice(0, 40)}…`;
  }
  return details;
}

export function AdminAuditView({ token }: Props) {
  const [entries, setEntries] = useState<AuditLogEntry[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [searchDebounced, setSearchDebounced] = useState('');
  const [actionFilter, setActionFilter] = useState('');
  const [sortBy, setSortBy] = useState<AuditSortField>('createdAt');
  const [sortDesc, setSortDesc] = useState(true);

  useEffect(() => {
    const t = window.setTimeout(() => setSearchDebounced(search.trim()), 300);
    return () => window.clearTimeout(t);
  }, [search]);

  useEffect(() => {
    setPage(0);
  }, [searchDebounced, actionFilter, sortBy, sortDesc]);

  const actionOptions = useMemo(
    () => [
      { value: '', label: 'Все действия' },
      ...Object.entries(AUDIT_ACTION_LABELS).map(([value, label]) => ({ value, label })),
    ],
    [],
  );

  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await fetchAuditLog(token, {
        all: true,
        take: PAGE_SIZE,
        skip: page * PAGE_SIZE,
        search: searchDebounced || undefined,
        action: actionFilter || undefined,
        sortBy,
        sortDesc,
      });
      setEntries(resp.items);
      setTotalCount(resp.totalCount);
    } catch (e) {
      setEntries([]);
      setTotalCount(0);
      setError(e instanceof Error ? e.message : 'Не удалось загрузить журнал');
    } finally {
      setLoading(false);
    }
  }, [token, page, searchDebounced, actionFilter, sortBy, sortDesc]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleSort = (field: AuditSortField) => {
    if (sortBy === field) {
      setSortDesc((d) => !d);
    } else {
      setSortBy(field);
      setSortDesc(field === 'createdAt');
    }
  };

  const resetFilters = () => {
    setSearch('');
    setActionFilter('');
    setSortBy('createdAt');
    setSortDesc(true);
    setPage(0);
  };

  return (
    <section className="office-admin office-audit">
      <div className="office-admin-header">
        <h2>Журнал действий</h2>
        <button type="button" className="btn-secondary btn-sm" onClick={() => void load()} disabled={loading}>
          {loading ? '…' : 'Обновить'}
        </button>
      </div>

      <div className="audit-filters">
        <label className="audit-filters__search parse-field">
          <span className="parse-field-label">Поиск</span>
          <input
            type="search"
            placeholder="Email, действие, ресурс, детали…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
        <AppSelect
          className="app-select--compact audit-filters__action"
          value={actionFilter}
          onChange={setActionFilter}
          options={actionOptions}
          aria-label="Тип действия"
        />
        {(search || actionFilter || sortBy !== 'createdAt' || !sortDesc) && (
          <button type="button" className="btn-ghost btn-sm audit-filters__reset" onClick={resetFilters}>
            Сбросить
          </button>
        )}
      </div>

      {!loading && totalCount > 0 && (
        <p className="registry-meta audit-summary">
          Всего записей: {totalCount}
          {totalCount > PAGE_SIZE ? ` · стр. ${page + 1} из ${pageCount}` : ''}
          {searchDebounced ? ` · поиск «${searchDebounced}»` : ''}
          {actionFilter ? ` · ${auditActionLabel(actionFilter)}` : ''}
        </p>
      )}

      {error && <div className="admin-alert admin-alert--error">{error}</div>}
      {entries.length === 0 && !loading ? (
        <p className="registry-meta">Записей не найдено.</p>
      ) : (
        <>
          <div className="audit-table-wrap">
            <table className="audit-table">
              <thead>
                <tr>
                  <SortHeader field="createdAt" current={sortBy} sortDesc={sortDesc} onSort={handleSort} />
                  <SortHeader field="user" current={sortBy} sortDesc={sortDesc} onSort={handleSort} />
                  <SortHeader field="action" current={sortBy} sortDesc={sortDesc} onSort={handleSort} />
                  <SortHeader field="resource" current={sortBy} sortDesc={sortDesc} onSort={handleSort} />
                  <th>Детали</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((row) => (
                  <tr key={row.id}>
                    <td className="audit-cell-time">{new Date(row.createdAt).toLocaleString()}</td>
                    <td>{row.userEmailSnapshot ?? (row.userId != null ? `#${row.userId}` : '—')}</td>
                    <td>
                      <span className="audit-action-code" title={row.action}>
                        {auditActionLabel(row.action)}
                      </span>
                    </td>
                    <td className="audit-cell-resource">{row.resource ?? '—'}</td>
                    <td className="audit-cell-details" title={row.details ?? undefined}>
                      {formatDetails(row.details)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {totalCount > PAGE_SIZE && (
            <div className="registry-pagination">
              <button type="button" disabled={page === 0 || loading} onClick={() => setPage((p) => p - 1)}>
                ← Назад
              </button>
              <span className="registry-pagination__info">
                {page + 1} / {pageCount}
              </span>
              <button
                type="button"
                disabled={loading || (page + 1) * PAGE_SIZE >= totalCount}
                onClick={() => setPage((p) => p + 1)}
              >
                Вперёд →
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}
