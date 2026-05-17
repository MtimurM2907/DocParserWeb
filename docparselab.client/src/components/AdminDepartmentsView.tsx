import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Department } from '../types/office';
import { createDepartment, deleteDepartment, fetchDepartments } from '../api/office';

type Props = { token: string };

export function AdminDepartmentsView({ token }: Props) {
  const [departments, setDepartments] = useState<Department[]>([]);
  const [name, setName] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setDepartments(await fetchDepartments(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка загрузки');
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  const filteredDepartments = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    const list = [...departments];
    list.sort((a, b) => a.name.localeCompare(b.name, 'ru'));
    if (!q) return list;
    return list.filter((d) => d.name.toLowerCase().includes(q) || String(d.id).includes(q));
  }, [departments, searchQuery]);

  const create = async () => {
    if (!name.trim()) return;
    setBusy(true);
    setError(null);
    try {
      await createDepartment(token, name.trim());
      setName('');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось создать');
    } finally {
      setBusy(false);
    }
  };

  const remove = async (dep: Department) => {
    if (!window.confirm(`Удалить подразделение «${dep.name}»?`)) return;
    setDeletingId(dep.id);
    setError(null);
    try {
      await deleteDepartment(token, dep.id);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось удалить');
    } finally {
      setDeletingId(null);
    }
  };

  return (
    <section className="office-admin">
      <div className="office-admin-header">
        <div>
          <h2>Подразделения</h2>
          <p className="office-admin-subtitle">{departments.length} в справочнике</p>
        </div>
        <button type="button" className="btn-secondary btn-sm" onClick={() => void load()} disabled={loading}>
          {loading ? '…' : 'Обновить'}
        </button>
      </div>

      {error && <div className="admin-alert admin-alert--error">{error}</div>}

      <div className="admin-panel-block">
        <h3 className="admin-panel-block__title">Добавить подразделение</h3>
        <div className="admin-dept-add">
          <input
            type="text"
            className="admin-dept-add__input"
            value={name}
            placeholder="Название подразделения"
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') void create();
            }}
            disabled={busy}
          />
          <button type="button" className="btn-primary" disabled={busy || !name.trim()} onClick={() => void create()}>
            {busy ? '…' : 'Добавить'}
          </button>
        </div>
      </div>

      <div className="admin-panel-block admin-panel-block--list">
        <div className="admin-list-toolbar">
          <h3 className="admin-panel-block__title">Справочник</h3>
          <input
            type="search"
            className="admin-list-toolbar__search"
            placeholder="Поиск по названию или №…"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        {departments.length === 0 && !loading ? (
          <p className="registry-meta">Подразделений пока нет.</p>
        ) : filteredDepartments.length === 0 ? (
          <p className="registry-meta">Ничего не найдено.</p>
        ) : (
          <ul className="admin-dept-grid">
            {filteredDepartments.map((d) => (
              <li key={d.id} className="admin-dept-chip">
                <span className="admin-dept-chip__name">{d.name}</span>
                <span className="admin-dept-chip__id">#{d.id}</span>
                <button
                  type="button"
                  className="btn-danger btn-sm admin-dept-chip__delete"
                  disabled={deletingId === d.id}
                  onClick={() => void remove(d)}
                  title="Удалить подразделение"
                >
                  {deletingId === d.id ? '…' : 'Удалить'}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
