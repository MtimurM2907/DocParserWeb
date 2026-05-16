import { useState } from 'react';
import { rewriteText } from '../api/backend';

const REWRITE_MODES = [
  'Более формально',
  'Кратко',
  'Подробнее',
  'Проще',
] as const;

const TONES = ['нейтральный', 'деловой', 'дружелюбный'] as const;

const LENGTHS = ['сопоставимая с оригиналом', 'короче', 'длиннее'] as const;

type Props = {
  open: boolean;
  sourceText: string;
  documentId?: number;
  token: string;
  confidential: boolean;
  onClose: () => void;
  onApply: (text: string) => void;
};

export function AiRewriteModal({
  open,
  sourceText,
  documentId,
  token,
  confidential,
  onClose,
  onApply,
}: Props) {
  const [mode, setMode] = useState<string>(REWRITE_MODES[0]);
  const [tone, setTone] = useState<string>(TONES[0]);
  const [length, setLength] = useState<string>(LENGTHS[0]);
  const [result, setResult] = useState<string | null>(null);
  const [comment, setComment] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!open) return null;

  const runRewrite = async () => {
    const text = sourceText.trim();
    if (!text) {
      setError('Нет текста для переписывания.');
      return;
    }
    setBusy(true);
    setError(null);
    setResult(null);
    setComment(null);
    try {
      const resp = await rewriteText({
        text,
        mode,
        tone,
        length,
        documentId,
        token,
      });
      setResult(resp.rewrittenText);
      setComment(resp.modelComment || null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось переписать текст');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-card modal-card-wide"
        role="dialog"
        aria-modal="true"
        aria-label="AI-переписывание текста"
        onClick={(e) => e.stopPropagation()}
      >
        <h3>AI-переписывание (GigaChat)</h3>
        {confidential ? (
          <p className="office-card-error">
            Для документов с грифом Confidential переписывание через нейросеть недоступно.
          </p>
        ) : (
          <>
            <div className="rewrite-form-grid">
              <label className="parse-field">
                <span className="parse-field-label">Режим</span>
                <select value={mode} onChange={(e) => setMode(e.target.value)} disabled={busy}>
                  {REWRITE_MODES.map((m) => (
                    <option key={m} value={m}>
                      {m}
                    </option>
                  ))}
                </select>
              </label>
              <label className="parse-field">
                <span className="parse-field-label">Тон</span>
                <select value={tone} onChange={(e) => setTone(e.target.value)} disabled={busy}>
                  {TONES.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
              </label>
              <label className="parse-field">
                <span className="parse-field-label">Длина</span>
                <select value={length} onChange={(e) => setLength(e.target.value)} disabled={busy}>
                  {LENGTHS.map((l) => (
                    <option key={l} value={l}>
                      {l}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <p className="registry-meta">
              Будет обработано до ~12 000 символов текущего {sourceText.length > 12000 ? '(текст обрежется)' : ''}{' '}
              фрагмента.
            </p>
            <div className="modal-actions">
              <button type="button" onClick={onClose} disabled={busy}>
                Закрыть
              </button>
              <button type="button" className="btn-primary" onClick={() => void runRewrite()} disabled={busy}>
                {busy ? 'Переписывание…' : 'Переписать'}
              </button>
            </div>
            {error && <p className="office-card-error">{error}</p>}
            {comment && <p className="registry-meta">{comment}</p>}
            {result && (
              <div className="rewrite-result">
                <label className="parse-field">
                  <span className="parse-field-label">Результат</span>
                  <textarea className="rewrite-result-text" readOnly value={result} rows={12} />
                </label>
                <div className="modal-actions">
                  <button
                    type="button"
                    className="btn-primary"
                    onClick={() => {
                      onApply(result);
                      onClose();
                    }}
                  >
                    Подставить в редактор
                  </button>
                </div>
              </div>
            )}
          </>
        )}
        {confidential && (
          <div className="modal-actions">
            <button type="button" onClick={onClose}>
              Закрыть
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
