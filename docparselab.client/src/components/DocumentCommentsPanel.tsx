import { useEffect, useState } from 'react';
import { addDocumentComment, fetchDocumentComments } from '../api/office';
import type { DocumentComment } from '../types/office';

type Props = { token: string; documentId: number };

export function DocumentCommentsPanel({ token, documentId }: Props) {
  const [expanded, setExpanded] = useState(false);
  const [items, setItems] = useState<DocumentComment[]>([]);
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!expanded) return;
    void fetchDocumentComments(token, documentId).then(setItems).catch(() => setItems([]));
  }, [expanded, token, documentId]);

  const submit = async () => {
    if (!text.trim()) return;
    setBusy(true);
    try {
      const c = await addDocumentComment(token, documentId, text.trim());
      setItems((prev) => [...prev, c]);
      setText('');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="office-card-panel">
      <button type="button" className="office-card-panel-toggle" onClick={() => setExpanded((e) => !e)}>
        <h3>Комментарии</h3>
        <span className="office-card-panel-chevron">{expanded ? '▲' : '▼'}</span>
      </button>
      {expanded && (
        <div className="office-card-panel-body">
          <ul className="comments-list">
            {items.map((c) => (
              <li key={c.id} className="comments-list__item">
                <div className="comments-list__head">
                  <strong>{c.userDisplayName || c.userEmail}</strong>
                  <span>{new Date(c.createdAt).toLocaleString()}</span>
                </div>
                <p>{c.text}</p>
              </li>
            ))}
          </ul>
          <label className="parse-field">
            <span className="parse-field-label">Новый комментарий</span>
            <textarea value={text} onChange={(e) => setText(e.target.value)} rows={2} disabled={busy} />
          </label>
          <button type="button" className="office-sidebar-btn btn-primary" disabled={busy} onClick={() => void submit()}>
            Добавить
          </button>
        </div>
      )}
    </div>
  );
}
