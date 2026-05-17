import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Department, DocumentRegistryItem, MainView } from '../types/office';
import { DOCUMENT_TYPE_LABELS, WORKFLOW_STATUS_LABELS } from '../types/office';
import { fetchDepartments, fetchRegistry } from '../api/office';
import { AppSelect, optionsFromLabels } from './AppSelect';

type Props = {
  token: string;
  onOpenDocument: (id: number) => void;
  onSwitchView: (view: MainView) => void;
};

export function DocumentRegistryView({ token, onOpenDocument, onSwitchView }: Props) {
  const [items, setItems] = useState<DocumentRegistryItem[]>([]);
  const [total, setTotal] = useState(0);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [loading, setLoading] = useState(false);
  const [status, setStatus] = useState('');
  const [documentType, setDocumentType] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [search, setSearch] = useState('');

  const statusOptions = useMemo(
    () => optionsFromLabels(WORKFLOW_STATUS_LABELS, { value: '', label: 'Все статусы' }),
    [],
  );
  const typeOptions = useMemo(
    () => optionsFromLabels(DOCUMENT_TYPE_LABELS, { value: '', label: 'Все типы' }),
    [],
  );
  const departmentOptions = useMemo(
    () => [
      { value: '', label: 'Все подразделения' },
      ...departments.map((d) => ({ value: String(d.id), label: d.name })),
    ],
    [departments],
  );
  const [mineOnly, setMineOnly] = useState(false);
  const [page, setPage] = useState(0);
  const pageSize = 25;

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const pageData = await fetchRegistry(token, {
        status: status || undefined,
        documentType: documentType || undefined,
        departmentId: departmentId ? parseInt(departmentId, 10) : undefined,
        search: search.trim() || undefined,
        mineOnly,
        skip: page * pageSize,
        take: pageSize,
      });
      setItems(pageData.items);
      setTotal(pageData.total);
    } catch {
      setItems([]);
      setTotal(0);
    } finally {
      setLoading(false);
    }
  }, [token, status, documentType, departmentId, search, mineOnly, page, pageSize]);

  useEffect(() => {
    setPage(0);
  }, [status, documentType, departmentId, search, mineOnly]);

  useEffect(() => {
    void fetchDepartments(token).then(setDepartments).catch(() => setDepartments([]));
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <section className="office-registry">
      <div className="office-registry-header">
        <h2>Реестр документов</h2>
        <button type="button" onClick={() => onSwitchView('workspace')}>
          Загрузить документ
        </button>
      </div>
      <div className="registry-filters">
        <input
          type="search"
          className="registry-filters__search"
          placeholder="Поиск по названию, файлу, тегам…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void load()}
        />
        <div className="registry-filters__controls">
          <AppSelect
            className="app-select--compact registry-filters__status"
            value={status}
            onChange={setStatus}
            options={statusOptions}
            aria-label="Статус"
          />
          <AppSelect
            className="app-select--compact registry-filters__type"
            value={documentType}
            onChange={setDocumentType}
            options={typeOptions}
            aria-label="Тип документа"
          />
          <AppSelect
            className="app-select--compact registry-filters__dept"
            value={departmentId}
            onChange={setDepartmentId}
            options={departmentOptions}
            aria-label="Подразделение"
          />
          <label className="registry-mine">
            <input type="checkbox" checked={mineOnly} onChange={(e) => setMineOnly(e.target.checked)} />
            Только мои
          </label>
          <button
            type="button"
            className="btn-primary btn-sm registry-filters__submit"
            onClick={() => void load()}
            disabled={loading}
          >
            {loading ? '…' : 'Найти'}
          </button>
        </div>
      </div>
      <p className="registry-meta">
        Найдено: {total}
        {total > pageSize && (
          <span>
            {' '}
            · стр. {page + 1} из {Math.max(1, Math.ceil(total / pageSize))}
          </span>
        )}
      </p>
      {items.length === 0 && !loading ? (
        <p className="registry-empty">Документов нет. Загрузите файл или измените фильтры.</p>
      ) : (
        <div className="registry-table-wrap">
          <table className="registry-table">
            <thead>
              <tr>
                <th>Название</th>
                <th>Теги</th>
                <th>Тип</th>
                <th>Статус</th>
                <th>Подразделение</th>
                <th>Автор</th>
                <th>Дата</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {items.map((row) => (
                <tr key={row.id}>
                  <td>
                    <div className="registry-title-cell">
                      <span className="registry-title">{row.title}</span>
                      <span className="registry-file-name">{row.fileName}</span>
                    </div>
                  </td>
                  <td className="registry-tags">{row.tags?.trim() || '—'}</td>
                  <td>{DOCUMENT_TYPE_LABELS[row.documentType] ?? row.documentType}</td>
                  <td>
                    <span className={`status-badge status-${row.workflowStatus}`}>
                      {WORKFLOW_STATUS_LABELS[row.workflowStatus] ?? row.workflowStatus}
                    </span>
                  </td>
                  <td>{row.departmentName ?? '—'}</td>
                  <td>{row.ownerEmail ?? '—'}</td>
                  <td>{new Date(row.uploadedAt).toLocaleDateString()}</td>
                  <td>
                    <button type="button" onClick={() => onOpenDocument(row.id)}>
                      Открыть
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {total > pageSize && (
        <div className="registry-pagination">
          <button type="button" disabled={page === 0 || loading} onClick={() => setPage((p) => p - 1)}>
            Назад
          </button>
          <button
            type="button"
            disabled={loading || (page + 1) * pageSize >= total}
            onClick={() => setPage((p) => p + 1)}
          >
            Вперёд
          </button>
        </div>
      )}
    </section>
  );
}
