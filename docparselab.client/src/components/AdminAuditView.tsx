import { useCallback, useEffect, useState } from 'react';
import type { AuditLogEntry } from '../types/api';
import { fetchAuditLog } from '../api/backend';

type Props = {
  token: string;
};

export function AdminAuditView({ token }: Props) {
  const [entries, setEntries] = useState<AuditLogEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setEntries(await fetchAuditLog(token, { all: true, take: 200 }));
    } catch (e) {
      setEntries([]);
      setError(e instanceof Error ? e.message : 'Не удалось загрузить журнал');
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <section className="office-admin office-audit">
      <div className="office-admin-header">
        <h2>Журнал действий (все пользователи)</h2>
        <button type="button" className="btn-secondary btn-sm" onClick={() => void load()} disabled={loading}>
          {loading ? '…' : 'Обновить'}
        </button>
      </div>
      {error && <div className="admin-alert admin-alert--error">{error}</div>}
      {entries.length === 0 && !loading ? (
        <p className="registry-meta">Записей нет.</p>
      ) : (
        <div className="audit-table-wrap">
          <table className="audit-table">
            <thead>
              <tr>
                <th>Время</th>
                <th>Пользователь</th>
                <th>Действие</th>
                <th>Ресурс</th>
                <th>Детали</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((row) => (
                <tr key={row.id}>
                  <td>{new Date(row.createdAt).toLocaleString()}</td>
                  <td>{row.userEmailSnapshot ?? row.userId ?? '—'}</td>
                  <td>{row.action}</td>
                  <td>{row.resource ?? '—'}</td>
                  <td>{row.details ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
