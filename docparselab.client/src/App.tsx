import { useEffect, useMemo, useRef, useState } from 'react';
import './App.css';
import type { ParsedDocument, SpellcheckMistake, SpellcheckResponse } from './types/api';
import { normalizeNewlines } from './lib/text';
import {
  authLoginOrRegister,
  deleteDocument as deleteDocumentRequest,
  exportDocument,
  getDocument,
  listMyDocuments,
  parseDocument,
  runSpellcheck,
  saveDocumentText,
  sendDocumentByEmail,
} from './api/backend';
import { prepareDocLikeSource, renderDocLikeText, renderHighlightedText } from './components/DocumentTextViews';
import { findTextMatches } from './lib/search';

function sameSpellcheckMistake(a: SpellcheckMistake, b: SpellcheckMistake): boolean {
  return a.start === b.start && a.length === b.length && a.word === b.word;
}

/** После замены фрагмента [replaceStart, replaceEnd) сдвигаем остальные ошибки; пересекающие зону замены отбрасываем. */
function adjustSpellcheckMistakesAfterReplace(
  mistakes: SpellcheckMistake[],
  applied: SpellcheckMistake,
  replaceStart: number,
  replaceEnd: number,
  delta: number,
): SpellcheckMistake[] {
  return mistakes
    .filter((m) => !sameSpellcheckMistake(m, applied))
    .map((m) => {
      const ms = m.start;
      const me = m.start + m.length;
      if (me <= replaceStart) return m;
      if (ms >= replaceEnd) return { ...m, start: m.start + delta };
      return null;
    })
    .filter((m): m is SpellcheckMistake => m != null);
}

function App() {
  const [file, setFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ParsedDocument | null>(null);
  const [authToken, setAuthToken] = useState<string | null>(() => localStorage.getItem('authToken'));
  const [authEmail, setAuthEmail] = useState<string | null>(() => localStorage.getItem('authEmail'));
  const [authMode, setAuthMode] = useState<'login' | 'register'>('login');
  const [authEmailInput, setAuthEmailInput] = useState('');
  const [authPasswordInput, setAuthPasswordInput] = useState('');
  const [isAuthLoading, setIsAuthLoading] = useState(false);
  const [myDocs, setMyDocs] = useState<ParsedDocument[]>([]);
  const [isLoadingDocs, setIsLoadingDocs] = useState(false);
  const [openingDocId, setOpeningDocId] = useState<number | null>(null);
  const [deletingDocId, setDeletingDocId] = useState<number | null>(null);
  const [isSendModalOpen, setIsSendModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [deleteDocId, setDeleteDocId] = useState<number | null>(null);
  const [deleteDocName, setDeleteDocName] = useState('');
  const [sendDocId, setSendDocId] = useState<number | null>(null);
  const [sendDocName, setSendDocName] = useState('');
  const [sendEmail, setSendEmail] = useState('');
  const [sendFormat, setSendFormat] = useState<'docx' | 'pdf'>('docx');
  const [isSendingEmail, setIsSendingEmail] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [editText, setEditText] = useState('');
  const [isSavingText, setIsSavingText] = useState(false);
  const [isSpellchecking, setIsSpellchecking] = useState(false);
  const [spellcheck, setSpellcheck] = useState<SpellcheckResponse | null>(null);
  const [activeMistakeIndex, setActiveMistakeIndex] = useState<number>(-1);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const [spellcheckSourceText, setSpellcheckSourceText] = useState('');
  const [textSearchQuery, setTextSearchQuery] = useState('');
  const [textSearchActiveIndex, setTextSearchActiveIndex] = useState(0);
  const textSearchInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (authToken) {
      void loadMyDocuments();
    } else {
      setMyDocs([]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authToken]);

  useEffect(() => {
    if (!result) {
      setIsEditing(false);
      setEditText('');
      setSpellcheck(null);
      setActiveMistakeIndex(-1);
      return;
    }
    setIsEditing(false);
    setEditText(normalizeNewlines(result.fullText ?? ''));
    setSpellcheck(null);
    setSpellcheckSourceText('');
    setActiveMistakeIndex(-1);
    setTextSearchQuery('');
    setTextSearchActiveIndex(0);
  }, [result?.id]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setResult(null);
    setError(null);
    const f = e.target.files?.[0] ?? null;
    setFile(f);
  };

  const handleUpload = async () => {
    if (!file) {
      setError('Выберите PDF или DOCX файл.');
      return;
    }

    setIsUploading(true);
    setError(null);
    try {
      const data = await parseDocument(file, authToken);
      setResult(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Неизвестная ошибка');
    } finally {
      setIsUploading(false);
    }
  };

  const handleAuthSubmit = async () => {
    setError(null);
    if (!authEmailInput || !authPasswordInput) {
      setError('Укажите email и пароль.');
      return;
    }

    setIsAuthLoading(true);
    try {
      const data = await authLoginOrRegister(authMode, authEmailInput, authPasswordInput);
      setAuthToken(data.token);
      setAuthEmail(data.email);
      localStorage.setItem('authToken', data.token);
      localStorage.setItem('authEmail', data.email);
      setAuthPasswordInput('');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Неизвестная ошибка при авторизации');
    } finally {
      setIsAuthLoading(false);
    }
  };

  const handleLogout = () => {
    setAuthToken(null);
    setAuthEmail(null);
    localStorage.removeItem('authToken');
    localStorage.removeItem('authEmail');
    setMyDocs([]);
  };

  const loadMyDocuments = async () => {
    if (!authToken) return;

    setIsLoadingDocs(true);
    try {
      const data = await listMyDocuments(authToken);
      setMyDocs(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить список документов');
    } finally {
      setIsLoadingDocs(false);
    }
  };

  const openSendModal = (docId: number, fileName: string) => {
    setSendDocId(docId);
    setSendDocName(fileName);
    setSendEmail('');
    setSendFormat('docx');
    setIsSendModalOpen(true);
  };

  const closeSendModal = (force = false) => {
    if (isSendingEmail && !force) return;
    setIsSendModalOpen(false);
    setSendDocId(null);
    setSendDocName('');
    setSendEmail('');
  };

  const openDeleteModal = (docId: number, fileName: string) => {
    setDeleteDocId(docId);
    setDeleteDocName(fileName);
    setIsDeleteModalOpen(true);
  };

  const closeDeleteModal = (force = false) => {
    if (deletingDocId !== null && !force) return;
    setIsDeleteModalOpen(false);
    setDeleteDocId(null);
    setDeleteDocName('');
  };

  const handleSendEmail = async () => {
    if (!authToken || !sendDocId) {
      setError('Сначала войдите в аккаунт.');
      return;
    }
    if (!sendEmail.trim()) {
      setError('Укажите email получателя.');
      return;
    }

    setError(null);
    setIsSendingEmail(true);
    try {
      await sendDocumentByEmail(authToken, sendDocId, sendEmail.trim(), sendFormat);
      closeSendModal(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось отправить документ на почту');
    } finally {
      setIsSendingEmail(false);
    }
  };

  const openDocumentById = async (docId: number) => {
    if (!authToken) {
      setError('Открытие сохранённых документов доступно после входа.');
      return;
    }

    setError(null);
    setOpeningDocId(docId);
    try {
      const data = await getDocument(authToken, docId);
      setResult(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось открыть документ');
    } finally {
      setOpeningDocId(null);
    }
  };

  const handleDownloadJson = () => {
    if (!result) return;
    const extracted =
      result.originalText != null && result.originalText.trim().length > 0
        ? normalizeNewlines(result.originalText)
        : normalizeNewlines(result.fullText ?? '');
    const payload = {
      documentId: result.id,
      fileName: result.fileName,
      originalFileType: result.originalFileType,
      uploadedAt: result.uploadedAt,
      text: extracted,
    };
    const json = `${JSON.stringify(payload, null, 2)}\n`;
    const blob = new Blob([json], { type: 'application/json;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const safeBase =
      `${result.fileName}`.replace(/\.[^./\\]+$/i, '').trim() ||
      `${result.id}_document`;
    a.download = `${safeBase}_text.json`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const copyCurrentText = async () => {
    const source = isEditing ? editText : normalizeNewlines(result?.fullText ?? '');
    if (!source) return;
    try {
      await navigator.clipboard.writeText(source);
    } catch {
      // ignore clipboard failures in unsupported contexts
    }
  };

  const handleExport = async (format: 'docx' | 'pdf') => {
    if (!result || !authToken) {
      setError('Экспорт DOCX/PDF доступен после входа в аккаунт.');
      return;
    }
    try {
      await exportDocument(authToken, result.id, result.fileName, format);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось экспортировать документ');
    }
  };

  const handleSaveText = async () => {
    if (!result) return;
    setError(null);
    setIsSavingText(true);
    try {
      if (!authToken) {
        // Гостевой режим: редактирование только локально (без сохранения в БД).
        setResult({ ...result, fullText: editText });
        setSpellcheckSourceText('');
        setIsEditing(false);
        return;
      }

      const data = await saveDocumentText(authToken, result.id, editText);
      setResult(data);
      setSpellcheckSourceText('');
      setIsEditing(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить изменения');
    } finally {
      setIsSavingText(false);
    }
  };

  const spellcheckText = async (textToCheck: string) => {
    const normalizedText = normalizeNewlines(textToCheck ?? '');
    setError(null);
    setIsSpellchecking(true);
    try {
      const data = await runSpellcheck(normalizedText);
      setSpellcheck(data);
      setSpellcheckSourceText(normalizedText);
      setActiveMistakeIndex(data.mistakes.length > 0 ? 0 : -1);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось проверить орфографию');
    } finally {
      setIsSpellchecking(false);
    }
  };

  const handleSpellcheck = async () => {
    if (isEditing) {
      await spellcheckText(editText);
      return;
    }
    let original = normalizeNewlines(result?.originalText ?? result?.fullText ?? '');
    if ((!result?.originalText || result.originalText.trim().length === 0) && authToken && result?.id) {
      try {
        const fresh = await getDocument(authToken, result.id);
        setResult(fresh);
        original = normalizeNewlines(fresh.originalText ?? fresh.fullText ?? '');
      } catch {
        // Игнорируем фоновую подгрузку и используем текущий текст.
      }
    }
    await spellcheckText(original);
  };

  const deleteDocument = async (docId: number) => {
    if (!authToken) return;
    setDeletingDocId(docId);
    setError(null);
    try {
      await deleteDocumentRequest(authToken, docId);
      setMyDocs((prev) => prev.filter((d) => d.id !== docId));
      if (result?.id === docId) {
        setResult(null);
        setSpellcheck(null);
        setSpellcheckSourceText('');
      }
      closeDeleteModal(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось удалить документ');
    } finally {
      setDeletingDocId(null);
    }
  };

  const applySuggestion = (mistake: SpellcheckMistake, suggestion: string) => {
    if (!spellcheck) return;
    const base =
      spellcheckSourceText.length > 0 ? spellcheckSourceText : normalizeNewlines(editText ?? '');
    const start = Math.max(0, Math.min(mistake.start, base.length));
    const end = Math.max(0, Math.min(mistake.start + mistake.length, base.length));
    if (end <= start) return;

    const newText = base.slice(0, start) + suggestion + base.slice(end);
    const delta = suggestion.length - (end - start);
    const newMistakes = adjustSpellcheckMistakesAfterReplace(
      spellcheck.mistakes,
      mistake,
      start,
      end,
      delta,
    );
    const removeIdx = spellcheck.mistakes.findIndex((m) => sameSpellcheckMistake(m, mistake));

    setEditText(newText);
    setSpellcheckSourceText(newText);
    setSpellcheck({ ...spellcheck, mistakes: newMistakes, textLength: newText.length });
    setActiveMistakeIndex((prev) => {
      if (newMistakes.length === 0) return -1;
      if (removeIdx < 0) return Math.min(prev, newMistakes.length - 1);
      if (prev > removeIdx) return prev - 1;
      return Math.min(prev, newMistakes.length - 1);
    });
  };

  const applyAllSuggestions = async () => {
    if (!spellcheck || spellcheck.mistakes.length === 0) return;
    if (!isEditing) return;

    // Применяем замены с конца текста к началу, чтобы индексы не "съезжали".
    const text =
      spellcheckSourceText.length > 0 ? spellcheckSourceText : normalizeNewlines(editText ?? '');
    const candidates = [...spellcheck.mistakes]
      .filter((m) => m.suggestions && m.suggestions.length > 0)
      .map((m) => ({
        start: Math.max(0, Math.min(m.start, text.length)),
        end: Math.max(0, Math.min(m.start + m.length, text.length)),
        suggestion: m.suggestions[0],
      }))
      .filter((m) => m.end > m.start)
      .sort((a, b) => b.start - a.start);

    if (candidates.length === 0) return;

    let newText = text;
    let lastStart = Number.POSITIVE_INFINITY;
    for (const c of candidates) {
      // пропускаем пересекающиеся диапазоны
      if (c.end > lastStart) continue;
      newText = newText.slice(0, c.start) + c.suggestion + newText.slice(c.end);
      lastStart = c.start;
    }

    setEditText(newText);
    await spellcheckText(newText);
  };

  const displaySearchText = useMemo(() => {
    if (!result) return '';
    if (isEditing) return editText;
    const full = normalizeNewlines(result.fullText ?? '');
    const showDocLike = !spellcheck || (spellcheck.mistakes?.length ?? 0) === 0;
    if (showDocLike) {
      return prepareDocLikeSource(full);
    }
    return spellcheck ? spellcheckSourceText : full;
  }, [result, isEditing, editText, spellcheck, spellcheckSourceText]);

  const textSearchMatches = useMemo(
    () => findTextMatches(displaySearchText, textSearchQuery),
    [displaySearchText, textSearchQuery],
  );

  useEffect(() => {
    setTextSearchActiveIndex(0);
  }, [textSearchQuery, isEditing, spellcheck?.mistakes?.length]);

  useEffect(() => {
    if (textSearchMatches.length === 0) {
      setTextSearchActiveIndex(0);
      return;
    }
    setTextSearchActiveIndex((i) => Math.min(i, textSearchMatches.length - 1));
  }, [textSearchMatches.length]);

  useEffect(() => {
    if (isEditing || !textSearchQuery.trim() || textSearchMatches.length === 0) return;
    requestAnimationFrame(() => {
      document.querySelector<HTMLElement>('.search-hit-active')?.scrollIntoView({
        block: 'nearest',
        behavior: 'smooth',
      });
    });
  }, [
    textSearchActiveIndex,
    textSearchMatches,
    isEditing,
    textSearchQuery,
    spellcheck?.mistakes?.length,
  ]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault();
        textSearchInputRef.current?.focus();
        textSearchInputRef.current?.select();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const goSearchNext = () => {
    if (textSearchMatches.length === 0) return;
    const next = (textSearchActiveIndex + 1) % textSearchMatches.length;
    setTextSearchActiveIndex(next);
    if (!isEditing) return;
    const m = textSearchMatches[next];
    const ta = textareaRef.current;
    if (ta && m) {
      ta.focus();
      ta.setSelectionRange(m.start, m.end);
    }
  };

  const goSearchPrev = () => {
    if (textSearchMatches.length === 0) return;
    const prev = (textSearchActiveIndex - 1 + textSearchMatches.length) % textSearchMatches.length;
    setTextSearchActiveIndex(prev);
    if (!isEditing) return;
    const m = textSearchMatches[prev];
    const ta = textareaRef.current;
    if (ta && m) {
      ta.focus();
      ta.setSelectionRange(m.start, m.end);
    }
  };

  const normalizedMistakes = useMemo(() => {
    const text = editText ?? '';
    const mistakes = spellcheck?.mistakes ?? [];
    return mistakes
      .filter((m) => Number.isFinite(m.start) && Number.isFinite(m.length))
      .map((m, idx) => ({
        ...m,
        start: Math.max(0, Math.min(m.start, text.length)),
        end: Math.max(0, Math.min(m.start + m.length, text.length)),
        _idx: idx,
      }))
      .filter((m) => m.end > m.start)
      .sort((a, b) => (a.start !== b.start ? a.start - b.start : a.end - b.end));
  }, [editText, spellcheck?.mistakes]);

  const jumpToMistake = (idx: number) => {
    if (!isEditing) return;
    if (idx < 0 || idx >= normalizedMistakes.length) return;
    const m = normalizedMistakes[idx];
    setActiveMistakeIndex(idx);
    const ta = textareaRef.current;
    if (!ta) return;
    try {
      ta.focus();
      ta.setSelectionRange(m.start, m.end);
    } catch {
      // ignore
    }
  };

  return (
    <div className="app-root">
      <header className="app-header">
        <div>
          <h1>DocParseLab</h1>
          <p>Загрузите PDF или DOCX — извлечение текста, проверка и экспорт.</p>
        </div>
        <div className="auth-panel">
          {authToken && authEmail ? (
            <div className="auth-info">
              <span>Вы вошли как {authEmail}</span>
              <button onClick={handleLogout}>Выйти</button>
            </div>
          ) : (
            <div className="auth-form">
              <div className="auth-toggle">
                <button
                  className={authMode === 'login' ? 'active' : ''}
                  onClick={() => setAuthMode('login')}
                >
                  Вход
                </button>
                <button
                  className={authMode === 'register' ? 'active' : ''}
                  onClick={() => setAuthMode('register')}
                >
                  Регистрация
                </button>
              </div>
              <input
                type="email"
                placeholder="Email"
                value={authEmailInput}
                onChange={(e) => setAuthEmailInput(e.target.value)}
              />
              <input
                type="password"
                placeholder="Пароль"
                value={authPasswordInput}
                onChange={(e) => setAuthPasswordInput(e.target.value)}
              />
              <button onClick={handleAuthSubmit} disabled={isAuthLoading}>
                {isAuthLoading ? 'Обработка...' : authMode === 'login' ? 'Войти' : 'Зарегистрироваться'}
              </button>
              <small>Можно пользоваться приложением и без входа как гость.</small>
            </div>
          )}
        </div>
      </header>

      <div className="upload-panel">
        <label className="file-input">
          <span className="file-input-label">{file ? file.name : 'Выберите PDF/DOCX-файл'}</span>
          <span className="file-input-button">Обзор</span>
          <input type="file" accept="application/pdf,.docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document" onChange={handleFileChange} />
        </label>
        <button onClick={handleUpload} disabled={isUploading || !file}>
          {isUploading ? 'Обработка...' : 'Загрузить и распарсить'}
        </button>
      </div>

      {error && <div className="error">{error}</div>}

      {authToken && (
        <section className="my-docs">
          <h2>Мои загруженные файлы</h2>
          <button onClick={loadMyDocuments} disabled={isLoadingDocs}>
            {isLoadingDocs ? 'Загрузка...' : 'Обновить список'}
          </button>
          {myDocs.length === 0 && !isLoadingDocs && <p>У вас пока нет сохранённых документов.</p>}
          {myDocs.length > 0 && (
            <ul className="docs-list">
              {myDocs.map((doc) => (
                <li key={doc.id} className="docs-list-item">
                  <div>
                    <strong>{doc.fileName}</strong>
                    <div className="docs-meta">
                      Загружен: {new Date(doc.uploadedAt).toLocaleString()}
                    </div>
                  </div>
                  <div className="docs-actions">
                    <button
                      onClick={() => {
                        void openDocumentById(doc.id);
                      }}
                      disabled={openingDocId === doc.id}
                    >
                      {openingDocId === doc.id ? 'Открытие...' : 'Открыть'}
                    </button>
                    <button
                      onClick={() => {
                        openSendModal(doc.id, doc.fileName);
                      }}
                    >
                      Отправить на email
                    </button>
                    <button
                      onClick={() => {
                        openDeleteModal(doc.id, doc.fileName);
                      }}
                      disabled={deletingDocId === doc.id}
                    >
                      {deletingDocId === doc.id ? 'Удаление...' : 'Удалить'}
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {result && (
        <div className="result">
          <h2>Результат</h2>
          <p>
            <strong>Файл:</strong> {result.fileName}
          </p>
          {result.originalFileType && (
            <p>
              <strong>Тип:</strong> {result.originalFileType.toUpperCase()}
            </p>
          )}
          <p>
            <strong>Загружен:</strong> {new Date(result.uploadedAt).toLocaleString()}
          </p>

          <div className="result-actions">
            <button onClick={handleDownloadJson}>Скачать JSON</button>
            <button onClick={() => void handleExport('docx')}>Экспорт DOCX</button>
            <button onClick={() => void handleExport('pdf')}>Экспорт PDF</button>
          </div>

          <div className="single-block">
            <div className="column">
              <h3>Текст</h3>
              <div className="editor-toolbar">
                <div className="text-search-row">
                  <label className="text-search-label" htmlFor="text-search-input">
                    Поиск
                  </label>
                  <input
                    ref={textSearchInputRef}
                    id="text-search-input"
                    type="search"
                    className="text-search-input"
                    placeholder="Найти в тексте…"
                    value={textSearchQuery}
                    onChange={(e) => setTextSearchQuery(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') {
                        e.preventDefault();
                        if (e.shiftKey) goSearchPrev();
                        else goSearchNext();
                      }
                    }}
                    autoComplete="off"
                  />
                  <button type="button" onClick={goSearchPrev} disabled={textSearchMatches.length === 0}>
                    Назад
                  </button>
                  <button type="button" onClick={goSearchNext} disabled={textSearchMatches.length === 0}>
                    Далее
                  </button>
                  <span className="text-search-meta" aria-live="polite">
                    {textSearchQuery.trim()
                      ? textSearchMatches.length > 0
                        ? `${textSearchActiveIndex + 1} / ${textSearchMatches.length}`
                        : 'Нет совпадений'
                      : ''}
                  </span>
                </div>
                <div className="editor-toolbar-main">
                  <div className="editor-toolbar-left">
                  {!isEditing ? (
                    <button
                      onClick={() => {
                        setIsEditing(true);
                        setEditText(normalizeNewlines(result.fullText ?? ''));
                        setSpellcheck(null);
                        setSpellcheckSourceText('');
                        setActiveMistakeIndex(-1);
                      }}
                    >
                      Редактировать
                    </button>
                  ) : (
                    <>
                      <button onClick={handleSaveText} disabled={isSavingText}>
                        {isSavingText ? 'Сохранение...' : authToken ? 'Сохранить' : 'Применить (локально)'}
                      </button>
                      <button
                        onClick={() => {
                          setIsEditing(false);
                          setEditText(normalizeNewlines(result.fullText ?? ''));
                          setSpellcheck(null);
                          setSpellcheckSourceText('');
                          setActiveMistakeIndex(-1);
                        }}
                        disabled={isSavingText}
                      >
                        Отмена
                      </button>
                    </>
                  )}

                  <button onClick={handleSpellcheck} disabled={isSpellchecking}>
                    {isSpellchecking ? 'Проверка (нейросеть)...' : 'Проверить орфографию (нейросеть)'}
                  </button>

                  <button onClick={() => void copyCurrentText()}>{isEditing ? 'Копировать из редактора' : 'Копировать текст'}</button>

                  {isEditing && spellcheck && spellcheck.mistakes.length > 0 && (
                    <button onClick={() => void applyAllSuggestions()} disabled={isSpellchecking}>
                      Исправить всё
                    </button>
                  )}

                  {spellcheck && spellcheck.mistakes.length > 0 && isEditing && (
                    <div className="editor-nav">
                      <button
                        type="button"
                        className="editor-nav-btn"
                        disabled={activeMistakeIndex <= 0 || isSpellchecking}
                        onClick={() => jumpToMistake(activeMistakeIndex - 1)}
                      >
                        ←
                      </button>
                      <div className="editor-nav-counter">
                        {Math.max(1, activeMistakeIndex + 1)} / {spellcheck.mistakes.length}
                      </div>
                      <button
                        type="button"
                        className="editor-nav-btn"
                        disabled={activeMistakeIndex < 0 || activeMistakeIndex >= spellcheck.mistakes.length - 1 || isSpellchecking}
                        onClick={() => jumpToMistake(activeMistakeIndex + 1)}
                      >
                        →
                      </button>
                    </div>
                  )}
                </div>

                <div className="editor-toolbar-right">
                  {result.editedAt && <span>Сохранено: {new Date(result.editedAt).toLocaleString()}</span>}
                </div>
                </div>
              </div>

              {isEditing ? (
                <div className="editor-layout editor-layout-focus">
                  <div className="editor-main">
                    <textarea
                      ref={textareaRef}
                      className="editor-textarea"
                      value={editText}
                      onChange={(e) => setEditText(e.target.value)}
                      spellCheck={false}
                    />

                    {spellcheck && (
                      <div className="editor-preview">
                        <div className="editor-preview-title">Превью с подсветкой</div>
                        {renderHighlightedText(editText, spellcheck.mistakes)}
                      </div>
                    )}
                  </div>

                  {spellcheck && spellcheck.mistakes.length > 0 && (
                    <aside className="mistakes-panel mistakes-panel-inline">
                      <div className="mistakes-header">
                        <div className="mistakes-title">Ошибки</div>
                        <div className="mistakes-badge">{spellcheck.mistakes.length}</div>
                      </div>
                      <div className="mistakes-list">
                        {spellcheck.mistakes.length > normalizedMistakes.length && (
                          <div className="mistakes-footer" style={{ marginBottom: 10 }}>
                            Показаны {normalizedMistakes.length} из {spellcheck.mistakes.length} (часть ошибок пересекается).
                          </div>
                        )}
                        {normalizedMistakes.slice(0, 200).map((m, idx) => {
                          const isActive = idx === activeMistakeIndex;
                          return (
                            <div
                              key={`${m.start}_${idx}`}
                              className={`mistake-item${isActive ? ' active' : ''}`}
                              role="button"
                              tabIndex={0}
                              onClick={() => jumpToMistake(idx)}
                              onKeyDown={(e) => {
                                if (e.key === 'Enter' || e.key === ' ') jumpToMistake(idx);
                              }}
                              title="Перейти к месту в тексте"
                            >
                              <div className="mistake-word">{m.word}</div>
                              {m.suggestions?.length ? (
                                <div className="spell-suggestions">
                                  {m.suggestions.slice(0, 5).map((s) => (
                                    <button
                                      key={s}
                                      type="button"
                                      className="spell-suggestion"
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        void applySuggestion(m, s);
                                      }}
                                      disabled={isSpellchecking}
                                      title="Заменить в тексте"
                                    >
                                      {s}
                                    </button>
                                  ))}
                                </div>
                              ) : (
                                <div className="mistake-muted">подсказок нет (часто слово корректное / имя / термин)</div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                      {spellcheck.mistakes.length > 200 && (
                        <div className="mistakes-footer">Показаны первые 200 (всего {spellcheck.mistakes.length}).</div>
                      )}
                    </aside>
                  )}
                </div>
              ) : (
                <div className="document-view">
                  {(() => {
                    const q = textSearchQuery.trim();
                    const searchM = q ? textSearchMatches : undefined;
                    const searchIdx = q ? textSearchActiveIndex : undefined;
                    return !spellcheck || spellcheck.mistakes.length === 0
                      ? renderDocLikeText(normalizeNewlines(result.fullText || ''), searchM, searchIdx)
                      : renderHighlightedText(
                          spellcheck ? spellcheckSourceText : normalizeNewlines(result.fullText || ''),
                          spellcheck?.mistakes ?? [],
                          searchM,
                          searchIdx,
                        );
                  })()}
                </div>
              )}

              {spellcheck && (
                !isEditing && (
                  <div className="spell-summary">
                    <div className="spell-summary-title">Орфография</div>
                    <div className="spell-summary-text">
                      {spellcheck.mistakes.length === 0 ? 'Ошибок не найдено.' : `Найдено ошибок: ${spellcheck.mistakes.length}`}
                      {spellcheck.mistakes.length > 0 && <span className="spell-summary-hint"> (включите “Редактировать”, чтобы исправлять кликом)</span>}
                    </div>
                  </div>
                )
              )}
            </div>
          </div>

          <div className="single-block">
            <div className="column">
              <h3>AI‑описание документа</h3>
              <pre className="text-output">
                {result.aiSummary && result.aiSummary.trim().length > 0 ? result.aiSummary : '(описание от GigaChat отсутствует)'}
              </pre>
            </div>
          </div>

        </div>
      )}

      {isSendModalOpen && (
        <div className="modal-overlay" onClick={() => closeSendModal()}>
          <div
            className="modal-card"
            role="dialog"
            aria-modal="true"
            aria-label="Отправка документа на email"
            onClick={(e) => e.stopPropagation()}
          >
            <h3>Отправить документ на email</h3>
            <p>
              <strong>Файл:</strong> {sendDocName}
            </p>
            <label className="modal-label" htmlFor="send-email-input">
              Email получателя
            </label>
            <input
              id="send-email-input"
              type="email"
              placeholder="name@example.com"
              value={sendEmail}
              onChange={(e) => setSendEmail(e.target.value)}
              disabled={isSendingEmail}
            />

            <label className="modal-label" htmlFor="send-format-select">
              Формат вложения
            </label>
            <select
              id="send-format-select"
              value={sendFormat}
              onChange={(e) => setSendFormat(e.target.value as 'docx' | 'pdf')}
              disabled={isSendingEmail}
            >
              <option value="docx">DOCX</option>
              <option value="pdf">PDF</option>
            </select>

            <div className="modal-actions">
              <button type="button" onClick={() => closeSendModal()} disabled={isSendingEmail}>
                Отмена
              </button>
              <button type="button" onClick={() => void handleSendEmail()} disabled={isSendingEmail}>
                {isSendingEmail ? 'Отправка...' : 'Отправить'}
              </button>
            </div>
          </div>
        </div>
      )}

      {isDeleteModalOpen && (
        <div className="modal-overlay" onClick={() => closeDeleteModal()}>
          <div
            className="modal-card modal-card-danger"
            role="dialog"
            aria-modal="true"
            aria-label="Подтверждение удаления документа"
            onClick={(e) => e.stopPropagation()}
          >
            <h3>Удалить документ?</h3>
            <p>
              Будет удален документ <strong>{deleteDocName}</strong>.
            </p>
            <div className="modal-actions">
              <button type="button" onClick={() => closeDeleteModal()} disabled={deletingDocId !== null}>
                Отмена
              </button>
              <button
                type="button"
                className="btn-danger"
                onClick={() => {
                  if (deleteDocId != null) void deleteDocument(deleteDocId);
                }}
                disabled={deletingDocId !== null}
              >
                {deletingDocId !== null ? 'Удаление...' : 'Удалить'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default App;