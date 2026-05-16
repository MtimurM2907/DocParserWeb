import { useEffect, useState } from 'react';
import type { DocumentVersionBrief, DocumentVersionDetail } from '../types/office';
import { fetchDocumentVersionDetail, fetchDocumentVersions } from '../api/office';

type Props = {
  token: string;
  documentId: number;
  onApplyVersion?: (text: string) => void;
  onRestoreVersion?: (text: string) => Promise<void>;
  applyDisabled?: boolean;
};

const CHANGE_LABELS: Record<string, string> = {
  edit: 'Правка текста',
  parse: 'Первичный разбор',
};

export function DocumentVersionsPanel({
  token,
  documentId,
  onApplyVersion,
  onRestoreVersion,
  applyDisabled = false,
}: Props) {
  const [versions, setVersions] = useState<DocumentVersionBrief[]>([]);
  const [loading, setLoading] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [selected, setSelected] = useState<DocumentVersionDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [restoring, setRestoring] = useState(false);

  useEffect(() => {
    if (!expanded) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    void fetchDocumentVersions(token, documentId)
      .then((list) => {
        if (!cancelled) setVersions(list);
      })
      .catch((e) => {
        if (!cancelled) {
          setVersions([]);
          setError(e instanceof Error ? e.message : 'Не удалось загрузить версии');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [token, documentId, expanded]);

  const openVersion = async (versionId: number) => {
    setDetailLoading(true);
    setError(null);
    try {
      setSelected(await fetchDocumentVersionDetail(token, documentId, versionId));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось открыть версию');
      setSelected(null);
    } finally {
      setDetailLoading(false);
    }
  };

  return (
    <div className="office-card-panel">
      <button
        type="button"
        className="office-card-panel-toggle"
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
      >
        <h3>История версий</h3>
        <span className="office-card-panel-chevron">{expanded ? '▲' : '▼'}</span>
      </button>
      {expanded && (
        <div className="office-card-panel-body">
          {error && <p className="office-card-error">{error}</p>}
          {loading && <p className="registry-meta">Загрузка…</p>}
          {!loading && versions.length === 0 && (
            <p className="registry-meta">Версий пока нет. Сохраните текст, чтобы создать версию.</p>
          )}
          {!loading && versions.length > 0 && (
            <ul className="versions-list">
              {versions.map((v) => (
                <li key={v.id} className={selected?.id === v.id ? 'active' : ''}>
                  <button type="button" className="version-row-btn" onClick={() => void openVersion(v.id)}>
                    <span className="version-num">v{v.versionNumber}</span>
                    <span className="version-row-main">
                      <span className="version-meta">
                        {CHANGE_LABELS[v.changeType] ?? v.changeType} · {v.textLength.toLocaleString()} симв.
                      </span>
                      <span className="version-date">{new Date(v.createdAt).toLocaleString()}</span>
                      {v.createdByEmail && <span className="version-author">{v.createdByEmail}</span>}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {detailLoading && <p className="registry-meta">Открытие версии…</p>}
          {selected && !detailLoading && (
            <div className="version-preview">
              <div className="version-preview-header">
                <strong>Версия {selected.versionNumber}</strong>
                <div className="version-preview-actions">
                  {onApplyVersion && (
                    <button
                      type="button"
                      className="btn-secondary"
                      disabled={applyDisabled}
                      title={applyDisabled ? 'Редактирование недоступно для текущего статуса' : undefined}
                      onClick={() => {
                        onApplyVersion(selected.text);
                        setSelected(null);
                      }}
                    >
                      В редактор
                    </button>
                  )}
                  {onRestoreVersion && (
                    <button
                      type="button"
                      className="btn-primary"
                      disabled={applyDisabled || restoring}
                      title={applyDisabled ? 'Редактирование недоступно для текущего статуса' : undefined}
                      onClick={() => {
                        void (async () => {
                          setRestoring(true);
                          setError(null);
                          try {
                            await onRestoreVersion(selected.text);
                            setSelected(null);
                            setExpanded(true);
                          } catch (e) {
                            setError(e instanceof Error ? e.message : 'Не удалось восстановить версию');
                          } finally {
                            setRestoring(false);
                          }
                        })();
                      }}
                    >
                      {restoring ? 'Сохранение…' : 'Сделать текущей'}
                    </button>
                  )}
                  <button type="button" className="btn-secondary" onClick={() => setSelected(null)}>
                    Закрыть
                  </button>
                </div>
              </div>
              <pre className="text-output version-preview-text">{selected.text}</pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
