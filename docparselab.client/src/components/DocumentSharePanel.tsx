import { useState } from 'react';
import type { ParsedDocument } from '../types/api';
import { shareDocument } from '../api/backend';

type Props = {
  token: string;
  document: ParsedDocument;
  currentUserId: number;
  onShared: () => void;
  onError: (msg: string) => void;
};

export function DocumentSharePanel({ token, document, currentUserId, onShared, onError }: Props) {
  const [expanded, setExpanded] = useState(false);
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [success, setSuccess] = useState<string | null>(null);

  const isOwner = document.ownerId === currentUserId;
  if (!isOwner) return null;

  const handleShare = async () => {
    const target = email.trim();
    if (!target) {
      onError('Укажите email получателя.');
      return;
    }
    setBusy(true);
    setSuccess(null);
    onError('');
    try {
      await shareDocument(token, document.id, target);
      setEmail('');
      setSuccess(`Доступ предоставлен: ${target}`);
      onShared();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Не удалось предоставить доступ');
    } finally {
      setBusy(false);
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
        <h3>Доступ коллегам</h3>
        <span className="office-card-panel-chevron">{expanded ? '▲' : '▼'}</span>
      </button>
      {expanded && (
        <div className="office-card-panel-body share-panel-body">
          <p className="registry-meta">
            Пользователь с указанным email сможет открыть и редактировать документ (если статус позволяет).
            {(document.shareCount ?? 0) > 0 && (
              <span> Уже расшарено: {document.shareCount}.</span>
            )}
          </p>
          <label className="parse-field">
            <span className="parse-field-label">Email коллеги</span>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="colleague@company.ru"
              disabled={busy}
              onKeyDown={(e) => e.key === 'Enter' && void handleShare()}
            />
          </label>
          <button type="button" className="office-sidebar-btn" disabled={busy} onClick={() => void handleShare()}>
            {busy ? 'Отправка…' : 'Предоставить доступ'}
          </button>
          {success && <p className="share-success">{success}</p>}
        </div>
      )}
    </div>
  );
}
