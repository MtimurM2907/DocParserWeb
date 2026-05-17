import { useEffect, useMemo, useRef, useState } from 'react';
import type {
  ParsedDocument,
  SpellcheckMistake,
  SpellcheckResponse,
} from './types/api';
import { normalizeNewlines } from './lib/text';
import { isKnownRussianSpellWord } from './lib/russianSpellLexicon';
import {
  deleteDocument as deleteDocumentRequest,
  downloadOriginalDocument,
  exportDocument,
  getDocument,
  parseBatch,
  parseDocument,
  runSpellcheck,
  fetchGigaChatStatus,
  regenerateDocumentSummary,
  saveDocumentText,
  sendDocumentByEmail,
} from './api/backend';
import { DATA_CLASSIFICATION_LABELS } from './types/office';
import { optionsFromLabels } from './components/AppSelect';
import { prepareDocLikeSource, renderDocLikeText, renderHighlightedText } from './components/DocumentTextViews';
import { DocumentPagedView } from './components/DocumentPagedView';
import {
  DocumentTextComparePanel,
  DocumentTextViewModeToolbar,
  type DocumentTextViewMode,
} from './components/DocumentTextComparePanel';
import { DocumentTextViewModal } from './components/DocumentTextViewModal';
import { splitDocumentIntoPages } from './lib/documentPages';
import { AdminAuditView } from './components/AdminAuditView';
import { AdminUsersView } from './components/AdminUsersView';
import { DocumentSharePanel } from './components/DocumentSharePanel';
import { DocumentMetadataPanel } from './components/DocumentMetadataPanel';
import { DocumentRegistryView } from './components/DocumentRegistryView';
import { DocumentVersionsPanel } from './components/DocumentVersionsPanel';
import { DocumentWorkspace } from './components/DocumentWorkspace';
import { MyTasksView } from './components/MyTasksView';
import { OfficeWorkflowPanel } from './components/OfficeWorkflowPanel';
import { DocumentSignaturePanel } from './components/DocumentSignaturePanel';
import { DocumentCommentsPanel } from './components/DocumentCommentsPanel';
import { UserHeaderMenu } from './components/UserHeaderMenu';
import { AdminDepartmentsView } from './components/AdminDepartmentsView';
import { useDocumentEditLock } from './hooks/useDocumentEditLock';
import { LoginScreen } from './components/LoginScreen';
import { ProfileSettingsModal } from './components/ProfileSettingsModal';
import type { AuthResponse } from './types/api';
import { IconEdit, IconFolder, IconLogo, IconStack, IconTasks, IconUpload } from './components/AppIcons';
import { AppSelect } from './components/AppSelect';
import { UploadProgressBar } from './components/UploadProgressBar';
import { createServerWaitProgress } from './lib/parseUploadProgress';
import { useParseProgressHub } from './hooks/useParseProgressHub';
import { fetchCurrentUser, fetchMyTasks } from './api/office';
import type { CurrentUser, MainView } from './types/office';
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
  useEffect(() => {
    document.body.style.overflow = '';
    return () => {
      document.body.style.overflow = '';
    };
  }, []);

  useEffect(() => {
    void fetchGigaChatStatus()
      .then((status) => setGigaChatConfigured(status.configured))
      .catch(() => setGigaChatConfigured(null));
  }, []);

  const [file, setFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [uploadProgressLabel, setUploadProgressLabel] = useState('');
  const parseProgressHub = useParseProgressHub();
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ParsedDocument | null>(null);
  const [authToken, setAuthToken] = useState<string | null>(() => localStorage.getItem('authToken'));
  const [authEmail, setAuthEmail] = useState<string | null>(() => localStorage.getItem('authEmail'));
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
  const [dataClassification, setDataClassification] = useState('Internal');
  const [batchFiles, setBatchFiles] = useState<File[]>([]);
  const [isBatchUploading, setIsBatchUploading] = useState(false);
  const [batchProgress, setBatchProgress] = useState(0);
  const [batchProgressLabel, setBatchProgressLabel] = useState('');
  const [batchSummary, setBatchSummary] = useState<string | null>(null);
  const [aiSummaryBusy, setAiSummaryBusy] = useState(false);
  const [gigaChatConfigured, setGigaChatConfigured] = useState<boolean | null>(null);
  const [mainView, setMainView] = useState<MainView>('registry');

  const classificationOptions = useMemo(() => optionsFromLabels(DATA_CLASSIFICATION_LABELS), []);
  const [pendingTasksCount, setPendingTasksCount] = useState(0);
  const [currentUser, setCurrentUser] = useState<CurrentUser | null>(null);
  const [profileOpen, setProfileOpen] = useState(false);
  const [documentTextViewMode, setDocumentTextViewMode] = useState<DocumentTextViewMode>('processed');
  const [textViewModalOpen, setTextViewModalOpen] = useState(false);

  useEffect(() => {
    setDocumentTextViewMode('processed');
    setTextViewModalOpen(false);
  }, [result?.id]);

  const isAdmin = currentUser?.role === 'Admin';
  const documentTextLocked = Boolean(
    authToken && result?.canEdit === false && result.ownerId != null,
  );

  const { lock: editLock } = useDocumentEditLock(
    authToken,
    result?.id ?? null,
    Boolean(result && isEditing && !documentTextLocked),
  );

  const editLockMessage =
    editLock && !editLock.canEdit && editLock.lockedByEmail
      ? `Сейчас редактирует: ${editLock.lockedByEmail}`
      : null;

  const editorLocked = documentTextLocked || Boolean(editLock && !editLock.canEdit);

  useEffect(() => {
    if (!authToken) {
      setCurrentUser(null);
      return;
    }
    let cancelled = false;
    void fetchCurrentUser(authToken)
      .then((u) => {
        if (!cancelled) setCurrentUser(u);
      })
      .catch(() => {
        if (!cancelled) setCurrentUser(null);
      });
    return () => {
      cancelled = true;
    };
  }, [authToken]);

  useEffect(() => {
    if (!authToken) {
      setPendingTasksCount(0);
      return;
    }
    void fetchMyTasks(authToken)
      .then((t) => setPendingTasksCount(t.length))
      .catch(() => setPendingTasksCount(0));
  }, [authToken, result?.workflowStatus]);

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
    if (!authToken) return;
    if (!file) {
      setError('Выберите PDF или DOCX файл.');
      return;
    }

    setIsUploading(true);
    setUploadProgress(0);
    setUploadProgressLabel('Подготовка…');
    setError(null);
    setBatchSummary(null);

    const progress = createServerWaitProgress((p) => {
      setUploadProgress(p.percent);
      setUploadProgressLabel(p.message);
    });

    try {
      await parseProgressHub.subscribe(authToken, (ev) => {
        progress.applyServerPageProgress(ev);
      });

      const data = await parseDocument(
        file,
        authToken,
        {
          dataClassification,
        },
        (p) => {
          setUploadProgress(p.percent);
          setUploadProgressLabel(p.message);
        },
        progress,
      );
      setResult(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Неизвестная ошибка');
    } finally {
      progress.stop();
      await parseProgressHub.unsubscribe();
      setIsUploading(false);
      setUploadProgress(0);
      setUploadProgressLabel('');
    }
  };

  const handleBatchUpload = async () => {
    if (!authToken) return;
    if (batchFiles.length === 0) {
      setError('Выберите один или несколько PDF/DOCX для пакетной загрузки.');
      return;
    }
    if (batchFiles.length > 30) {
      setError('Не более 30 файлов за один запрос.');
      return;
    }

    setIsBatchUploading(true);
    setBatchProgress(0);
    setBatchProgressLabel('Подготовка…');
    setError(null);
    setBatchSummary(null);
    try {
      const r = await parseBatch(
        batchFiles.slice(0, 30),
        authToken,
        {
          dataClassification,
        },
        null,
        (p) => {
          setBatchProgress(p.percent);
          setBatchProgressLabel(p.message);
        },
      );
      if (r.documents.length > 0) {
        setResult(r.documents[0]);
      } else {
        setResult(null);
      }
      setBatchSummary(
        `Пакет: обработано ${r.documents.length}, с ошибками ${r.errors.length}.`,
      );
      setBatchFiles([]);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка пакетной загрузки');
    } finally {
      setIsBatchUploading(false);
      setBatchProgress(0);
      setBatchProgressLabel('');
    }
  };

  const applyAuth = (data: AuthResponse) => {
    setAuthToken(data.token);
    setAuthEmail(data.email);
    localStorage.setItem('authToken', data.token);
    localStorage.setItem('authEmail', data.email);
    setCurrentUser({
      id: data.userId,
      email: data.email,
      role: data.role,
      departmentId: data.departmentId,
      departmentName: data.departmentName,
      displayName: data.displayName,
    });
    setMainView('registry');
    setError(null);
    setResult(null);
  };

  const handleLogout = () => {
    setAuthToken(null);
    setAuthEmail(null);
    setCurrentUser(null);
    setProfileOpen(false);
    setResult(null);
    setMainView('registry');
    localStorage.removeItem('authToken');
    localStorage.removeItem('authEmail');
    setSpellcheck(null);
    setFile(null);
    setBatchFiles([]);
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
    try {
      const data = await getDocument(authToken, docId);
      setResult(data);
      setMainView('workspace');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось открыть документ');
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

  const handleExport = async (format: 'docx' | 'pdf' | 'signed-pdf') => {
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

  const handleApplyVersionText = (text: string) => {
    const normalized = normalizeNewlines(text);
    setIsEditing(true);
    setEditText(normalized);
    setSpellcheck(null);
    setSpellcheckSourceText('');
    setActiveMistakeIndex(-1);
  };

  const handleRestoreVersion = async (text: string) => {
    if (!result || !authToken) {
      throw new Error('Войдите в аккаунт, чтобы сохранить версию в документ.');
    }
    if (documentTextLocked) {
      throw new Error('Редактирование недоступно для текущего статуса документа.');
    }
    const normalized = normalizeNewlines(text);
    const data = await saveDocumentText(authToken, result.id, normalized);
    setResult(data);
    setIsEditing(false);
    setEditText(normalizeNewlines(data.fullText ?? ''));
    setSpellcheck(null);
    setSpellcheckSourceText('');
    setActiveMistakeIndex(-1);
  };

  const refreshDocument = async () => {
    if (!authToken || !result?.id) return;
    try {
      setResult(await getDocument(authToken, result.id));
    } catch {
      // ignore background refresh errors
    }
  };

  const handleSaveText = async () => {
    if (!result || !authToken) return;
    setError(null);
    setIsSavingText(true);
    try {
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
    if (!authToken) return;
    const normalizedText = normalizeNewlines(textToCheck ?? '');
    setError(null);
    setIsSpellchecking(true);
    try {
      const docCtx = result?.id != null ? { documentId: result.id } : undefined;
      const data = await runSpellcheck(normalizedText, authToken, docCtx);
      const filtered = {
        ...data,
        mistakes: data.mistakes.filter((m) => !isKnownRussianSpellWord(m.word)),
        spellcheckEngine: data.spellcheckEngine ?? 'hunspell',
      };
      setSpellcheck(filtered);
      setSpellcheckSourceText(normalizedText);
      setActiveMistakeIndex(filtered.mistakes.length > 0 ? 0 : -1);
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

  const textSearchMatches = useMemo(() => {
    const q = textSearchQuery.trim();
    if (!q) return [];
    if (isEditing) return findTextMatches(editText, q);
    const full = normalizeNewlines(result?.fullText ?? '');
    const usePaged =
      !isEditing && result && (!spellcheck || (spellcheck.mistakes?.length ?? 0) === 0);
    if (usePaged) {
      const pages = splitDocumentIntoPages(full).map((p) => prepareDocLikeSource(p));
      if (pages.length > 1) {
        return pages.flatMap((pageText) => findTextMatches(pageText, q));
      }
    }
    return findTextMatches(displaySearchText, q);
  }, [displaySearchText, textSearchQuery, isEditing, editText, result, spellcheck]);

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

  if (!authToken) {
    return <LoginScreen onAuthenticated={applyAuth} />;
  }

  return (
    <div className={`app-root${result ? ' app-root--document' : ''}`}>
      <header className="app-header">
        <div className="app-brand">
          <IconLogo className="app-logo" />
          <div className="app-brand-text">
            <h1>DocParseLab</h1>
            <p>ИС документов офиса: реестр, согласование, разбор PDF/DOCX и правка текста.</p>
          </div>
        </div>
        <UserHeaderMenu
          token={authToken}
          user={currentUser}
          email={authEmail ?? ''}
          onOpenProfile={() => setProfileOpen(true)}
          onLogout={handleLogout}
          onOpenDocument={(docId) => {
            void getDocument(authToken, docId).then((d) => {
              setResult(d);
              setMainView('workspace');
            });
          }}
        />
      </header>

      <nav className="main-nav" aria-label="Разделы">
          <button type="button" className={mainView === 'registry' ? 'active' : ''} onClick={() => setMainView('registry')}>
            <IconFolder /> Реестр
          </button>
          <button type="button" className={mainView === 'tasks' ? 'active' : ''} onClick={() => setMainView('tasks')}>
            <IconTasks /> Мои задачи
            {pendingTasksCount > 0 && <span className="nav-badge">{pendingTasksCount}</span>}
          </button>
          <button type="button" className={mainView === 'workspace' ? 'active' : ''} onClick={() => setMainView('workspace')}>
            <IconEdit /> Работа с документом
          </button>
          {isAdmin && (
            <button type="button" className={mainView === 'admin' ? 'active' : ''} onClick={() => setMainView('admin')}>
              Администрирование
            </button>
          )}
        </nav>
      {mainView === 'admin' && isAdmin && (
        <div className="admin-page">
          <AdminUsersView token={authToken} />
          <AdminDepartmentsView token={authToken} />
          <AdminAuditView token={authToken} />
        </div>
      )}
      {mainView === 'registry' && (
        <DocumentRegistryView token={authToken} onOpenDocument={(id) => void openDocumentById(id)} onSwitchView={setMainView} />
      )}
      {mainView === 'tasks' && (
        <MyTasksView token={authToken} onOpenDocument={(id) => void openDocumentById(id)} />
      )}
      {mainView === 'workspace' && (
      <>
      <section className="upload-panel">
        <div className="upload-hero">
          <div className="upload-hero-copy">
            <div className="upload-hero-icon" aria-hidden>
              <IconUpload />
            </div>
            <h2>Новый документ</h2>
            <p>Загрузите PDF или DOCX — текст извлечётся автоматически, дальше правка и согласование в одном месте.</p>
          </div>
          <div className="upload-hero-actions">
            <label className={`upload-dropzone${file ? ' has-file' : ''}`}>
              <input
                type="file"
                accept="application/pdf,.docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                onChange={handleFileChange}
                disabled={isUploading}
              />
              <span className="upload-dropzone-title">
                {file ? file.name : 'Нажмите или перетащите файл'}
              </span>
              <span className="upload-dropzone-hint">PDF, DOCX</span>
            </label>
            <div className="upload-submit-block">
              <button
                type="button"
                className="btn-primary btn-lg"
                onClick={() => void handleUpload()}
                disabled={isUploading || !file}
              >
                {isUploading ? 'Обработка…' : 'Загрузить и распарсить'}
              </button>
              {isUploading && (
                <UploadProgressBar percent={uploadProgress} label={uploadProgressLabel} />
              )}
            </div>
          </div>
        </div>
        <div className="upload-options-grid">
          <label className="parse-field">
            <span className="parse-field-label">Классификация</span>
            <AppSelect
              value={dataClassification}
              onChange={setDataClassification}
              options={classificationOptions.map((o) =>
                o.value === 'Confidential'
                  ? { ...o, label: `${o.label} (без GigaChat)` }
                  : o,
              )}
            />
          </label>
        </div>
        <div className="upload-batch">
          <div className="upload-batch-head">
            <IconStack />
            <div>
              <strong>Пакетная загрузка</strong>
              <span>До 30 файлов за один раз</span>
            </div>
          </div>
          <div className="upload-batch-actions">
            <label className="batch-file-btn">
              <input
                type="file"
                multiple
                accept="application/pdf,.docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                onChange={(e) => setBatchFiles(Array.from(e.target.files ?? []))}
              />
              {batchFiles.length ? `Выбрано: ${batchFiles.length}` : 'Выбрать файлы'}
            </label>
            <div className="upload-submit-block upload-submit-block--batch">
              <button
                type="button"
                className="btn-secondary"
                onClick={() => void handleBatchUpload()}
                disabled={isBatchUploading || batchFiles.length === 0}
              >
                {isBatchUploading ? 'Загрузка…' : 'Загрузить пакет'}
              </button>
              {isBatchUploading && (
                <UploadProgressBar percent={batchProgress} label={batchProgressLabel} />
              )}
            </div>
          </div>
        </div>
      </section>

      {batchSummary && <div className="batch-summary">{batchSummary}</div>}

      {error && <div className="error">{error}</div>}

      {result && (
        <DocumentWorkspace
          document={result}
          authToken={authToken}
          documentTextLocked={documentTextLocked}
          editLockMessage={editLockMessage}
          onBack={() => setMainView('registry')}
          onDownloadJson={handleDownloadJson}
          onExport={(fmt) => void handleExport(fmt)}
          onDownloadOriginal={
            result.hasOriginalFile && authToken
              ? () => void downloadOriginalDocument(authToken, result.id, result.fileName)
              : undefined
          }
          onSendEmail={() => openSendModal(result.id, result.fileName)}
          onDelete={() => openDeleteModal(result.id, result.fileName)}
          sidebar={
            <>
              {authToken && result.workflowStatus && (
                <>
                  <OfficeWorkflowPanel
                    token={authToken}
                    document={result}
                    onUpdated={setResult}
                    onError={(msg) => setError(msg)}
                  />
                  <DocumentSignaturePanel
                    token={authToken}
                    document={result}
                    onUpdated={setResult}
                    onError={(msg) => setError(msg)}
                  />
                  <DocumentCommentsPanel token={authToken} documentId={result.id} />
                  <DocumentMetadataPanel
                    token={authToken}
                    document={result}
                    onUpdated={setResult}
                    onError={(msg) => setError(msg)}
                  />
                  {currentUser && (
                    <DocumentSharePanel
                      token={authToken}
                      document={result}
                      currentUserId={currentUser.id}
                      onShared={() => void refreshDocument()}
                      onError={(msg) => setError(msg)}
                    />
                  )}
                  <DocumentVersionsPanel
                    key={`${result.id}-${result.editedAt ?? result.uploadedAt}`}
                    token={authToken}
                    documentId={result.id}
                    onApplyVersion={handleApplyVersionText}
                    onRestoreVersion={handleRestoreVersion}
                    applyDisabled={editorLocked}
                  />
                </>
              )}
            </>
          }
          editor={
            <div className="doc-text-panel">
              <div className="doc-text-panel__head">
                <h3>Текст документа</h3>
                {result.editedAt && (
                  <span className="doc-text-panel__saved">
                    Сохранено: {new Date(result.editedAt).toLocaleString()}
                  </span>
                )}
              </div>
              {authToken && !isEditing && (
                <div className="doc-text-inline-actions">
                  <DocumentTextViewModeToolbar
                    viewMode={documentTextViewMode}
                    onViewModeChange={setDocumentTextViewMode}
                    showSource={
                      Boolean(result.hasOriginalFile) &&
                      (result.originalFileType ?? '').toLowerCase() === 'pdf'
                    }
                  />
                  <button
                    type="button"
                    className="btn-secondary btn-sm"
                    onClick={() => setTextViewModalOpen(true)}
                  >
                    На весь экран
                  </button>
                </div>
              )}
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
                      disabled={editorLocked}
                      title={
                        documentTextLocked
                          ? 'Редактирование недоступно для текущего статуса'
                          : editLockMessage ?? undefined
                      }
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
                        {isSavingText ? 'Сохранение...' : 'Сохранить'}
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
                    {isSpellchecking ? 'Проверка…' : 'Проверить орфографию'}
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
                      <p className="spell-engine-hint mistakes-engine-hint">Локальная проверка (Hunspell).</p>
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
                    const full = normalizeNewlines(result.fullText || '');
                    if (spellcheck && spellcheck.mistakes.length > 0) {
                      return renderHighlightedText(
                        spellcheck ? spellcheckSourceText : full,
                        spellcheck?.mistakes ?? [],
                        searchM,
                        searchIdx,
                      );
                    }
                    if (authToken) {
                      return (
                        <div className="doc-text-inline-view">
                          <DocumentTextComparePanel
                            processedText={full}
                            originalText={normalizeNewlines(result.originalText ?? '')}
                            documentId={result.id}
                            authToken={authToken}
                            isPdf={(result.originalFileType ?? '').toLowerCase() === 'pdf'}
                            hasOriginalFile={Boolean(result.hasOriginalFile)}
                            initialPdfPageCount={result.originalPageCount ?? null}
                            searchQuery={textSearchQuery}
                            activeSearchIndex={textSearchActiveIndex}
                            onSearchIndexChange={setTextSearchActiveIndex}
                            viewMode={documentTextViewMode}
                          />
                        </div>
                      );
                    }
                    return (
                      <DocumentPagedView
                        fullText={full}
                        searchQuery={textSearchQuery}
                        activeSearchIndex={textSearchActiveIndex}
                        onSearchIndexChange={setTextSearchActiveIndex}
                        renderPage={(pageText, pageSearchM, pageSearchIdx) =>
                          renderDocLikeText(pageText, pageSearchM, pageSearchIdx)
                        }
                      />
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
                    <p className="spell-engine-hint">Локальная проверка (Hunspell).</p>
                  </div>
                )
              )}
            </div>
          }
          aiSummary={
            <details className="doc-ai-details" open>
              <summary>Описание документа</summary>
              <div className="doc-ai-details__body">
                {(() => {
                  const raw = result.aiSummary?.trim() ?? '';
                  const local = raw.startsWith('[local-summary]');
                  const legacyUnavailable = raw.startsWith('Краткое AI-описание недоступно');
                  const text = local
                    ? raw.slice('[local-summary]'.length).trimStart()
                    : legacyUnavailable && raw.includes('\n\n')
                      ? raw.slice(raw.indexOf('\n\n') + 2)
                      : raw;

                  if (!text) {
                    return (
                      <p className="doc-ai-details__hint">
                        Описание не сформировано. Нажмите «Обновить описание» или загрузите документ заново.
                      </p>
                    );
                  }

                  return (
                    <>
                      {local && gigaChatConfigured === false && (
                        <p className="doc-ai-details__hint doc-ai-details__hint--info">
                          Автоматическое описание по структуре текста (GigaChat не подключён). Укажите ClientId и
                          ClientSecret в <code>DocParseLab.Server/gigachat.secrets.json</code> — см. RUN_INSTRUCTIONS.md,
                          затем перезапустите сервер и нажмите «Обновить описание».
                        </p>
                      )}
                      {local && gigaChatConfigured === true && (
                        <p className="doc-ai-details__hint doc-ai-details__hint--info">
                          GigaChat не ответил — показано локальное описание. Проверьте ключи, сертификат в{' '}
                          <code>certs/</code> и доступ к сети, затем нажмите «Обновить описание».
                        </p>
                      )}
                      {legacyUnavailable && !local && (
                        <p className="doc-ai-details__hint doc-ai-details__hint--info">
                          Нажмите «Обновить описание», чтобы получить структурированное описание.
                        </p>
                      )}
                      <div className="doc-ai-details__text">{text}</div>
                    </>
                  );
                })()}
                {authToken && result.id && (
                  <button
                    type="button"
                    className="btn-secondary btn-sm doc-ai-details__refresh"
                    disabled={aiSummaryBusy}
                    onClick={() => {
                      setAiSummaryBusy(true);
                      void regenerateDocumentSummary(authToken, result.id)
                        .then((r) => {
                          setResult((prev) => (prev ? { ...prev, aiSummary: r.aiSummary } : prev));
                          if (r.source === 'gigachat') {
                            setGigaChatConfigured(true);
                          }
                        })
                        .catch((e) => setError(e instanceof Error ? e.message : 'Не удалось обновить описание'))
                        .finally(() => setAiSummaryBusy(false));
                    }}
                  >
                    {aiSummaryBusy ? 'Формирование…' : 'Обновить описание'}
                  </button>
                )}
              </div>
            </details>
          }
        />
      )}

      </>
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
            <AppSelect
              id="send-format-select"
              value={sendFormat}
              onChange={(v) => setSendFormat(v as 'docx' | 'pdf')}
              disabled={isSendingEmail}
              options={[
                { value: 'docx', label: 'DOCX' },
                { value: 'pdf', label: 'PDF' },
              ]}
            />

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

      {result && authToken && (
        <DocumentTextViewModal
          open={textViewModalOpen}
          onClose={() => setTextViewModalOpen(false)}
          title={result.title?.trim() || result.fileName}
          fileName={result.fileName}
          processedText={normalizeNewlines(result.fullText ?? '')}
          originalText={normalizeNewlines(result.originalText ?? '')}
          initialPdfPageCount={result.originalPageCount ?? null}
          documentId={result.id}
          authToken={authToken}
          isPdf={(result.originalFileType ?? '').toLowerCase() === 'pdf'}
          hasOriginalFile={Boolean(result.hasOriginalFile)}
          viewMode={documentTextViewMode}
          onViewModeChange={(mode) => setDocumentTextViewMode(mode)}
          showSource={
            Boolean(result.hasOriginalFile) &&
            (result.originalFileType ?? '').toLowerCase() === 'pdf'
          }
          searchQuery={textSearchQuery}
          onSearchQueryChange={setTextSearchQuery}
          onSearchPrev={goSearchPrev}
          onSearchNext={goSearchNext}
          searchDisabled={textSearchMatches.length === 0}
          searchMeta={
            textSearchQuery.trim()
              ? textSearchMatches.length > 0
                ? `${textSearchActiveIndex + 1} / ${textSearchMatches.length}`
                : 'Нет совпадений'
              : ''
          }
          activeSearchIndex={textSearchActiveIndex}
          onSearchIndexChange={setTextSearchActiveIndex}
        />
      )}

      {profileOpen && currentUser && (
        <ProfileSettingsModal
          user={currentUser}
          isAdmin={isAdmin}
          onClose={() => setProfileOpen(false)}
        />
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