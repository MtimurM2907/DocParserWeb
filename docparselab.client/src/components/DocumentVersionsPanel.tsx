import { useEffect, useMemo, useState } from 'react';
import type { DocumentVersionBrief, DocumentVersionDetail } from '../types/office';
import { fetchDocumentVersionDetail, fetchDocumentVersions, fetchVersionDiff } from '../api/office';
import type { VersionDiff } from '../types/office';
import { AppSelect } from './AppSelect';
import { DocumentVersionDiffModal, DocumentVersionPreviewModal } from './DocumentVersionPreviewModal';

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
  import: 'Импорт',
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
  const [previewOpen, setPreviewOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [restoring, setRestoring] = useState(false);
  const [diffFromId, setDiffFromId] = useState('');
  const [diffToId, setDiffToId] = useState('');
  const [diff, setDiff] = useState<VersionDiff | null>(null);
  const [diffLoading, setDiffLoading] = useState(false);
  const [diffModalOpen, setDiffModalOpen] = useState(false);

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

  const versionOptions = useMemo(
    () => versions.map((v) => ({ value: String(v.id), label: `v${v.versionNumber}` })),
    [versions],
  );

  const versionLabel = (id: string) => versionOptions.find((o) => o.value === id)?.label ?? id;

  const closePreview = () => {
    setPreviewOpen(false);
    setSelected(null);
    setDetailLoading(false);
  };

  const openVersion = async (versionId: number) => {
    setPreviewOpen(true);
    setDetailLoading(true);
    setError(null);
    setSelected(null);
    try {
      setSelected(await fetchDocumentVersionDetail(token, documentId, versionId));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось открыть версию');
    } finally {
      setDetailLoading(false);
    }
  };

  const handleRestore = async (text: string) => {
    if (!onRestoreVersion) return;
    setRestoring(true);
    setError(null);
    try {
      await onRestoreVersion(text);
      closePreview();
      setExpanded(true);
      const list = await fetchDocumentVersions(token, documentId);
      setVersions(list);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось восстановить версию');
      throw e;
    } finally {
      setRestoring(false);
    }
  };

  const runDiff = async () => {
    setDiffLoading(true);
    setError(null);
    try {
      const result = await fetchVersionDiff(
        token,
        documentId,
        parseInt(diffFromId, 10),
        parseInt(diffToId, 10),
      );
      setDiff(result);
      setDiffModalOpen(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сравнить');
      setDiff(null);
    } finally {
      setDiffLoading(false);
    }
  };

  return (
    <>
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
            {!loading && versions.length > 1 && (
              <div className="version-diff-controls">
                <h4>Сравнение версий</h4>
                <div className="version-diff-pickers">
                  <AppSelect
                    value={diffFromId}
                    onChange={setDiffFromId}
                    options={[{ value: '', label: 'С версии…' }, ...versionOptions]}
                    placeholder="С версии…"
                  />
                  <AppSelect
                    value={diffToId}
                    onChange={setDiffToId}
                    options={[{ value: '', label: 'К версии…' }, ...versionOptions]}
                    placeholder="К версии…"
                  />
                  <button
                    type="button"
                    className="btn-secondary version-diff-run"
                    disabled={diffLoading || !diffFromId || !diffToId}
                    onClick={() => void runDiff()}
                  >
                    {diffLoading ? '…' : 'Сравнить'}
                  </button>
                </div>
              </div>
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
                      <span className="version-row-open" aria-hidden>
                        Просмотр
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>

      <DocumentVersionPreviewModal
        open={previewOpen}
        version={selected}
        loading={detailLoading}
        error={previewOpen && !detailLoading && !selected ? error : null}
        onClose={closePreview}
        onApplyVersion={onApplyVersion}
        onRestoreVersion={onRestoreVersion ? handleRestore : undefined}
        applyDisabled={applyDisabled}
        restoring={restoring}
      />

      <DocumentVersionDiffModal
        open={diffModalOpen}
        diff={diff}
        fromLabel={versionLabel(diffFromId)}
        toLabel={versionLabel(diffToId)}
        onClose={() => setDiffModalOpen(false)}
      />
    </>
  );
}
