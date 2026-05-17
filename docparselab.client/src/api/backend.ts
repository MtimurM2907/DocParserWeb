import type {
  AuditLogEntry,
  AuthResponse,
  BatchParseResponse,
  ChecklistValidateResult,
  ExtractedEntities,
  ParsedDocument,
  SpellcheckResponse,
} from '../types/api';
import type { UserBrief } from '../types/office';
import { authHeaders, readResponseError } from '../lib/http';
import {
  createServerWaitProgress,
  type ParseProgress,
  type ParseProgressCallback,
  type ParseUploadProgressController,
} from '../lib/parseUploadProgress';

export type { ParseProgress, ParseProgressCallback, ParseUploadProgressController };

const jsonContent = { 'Content-Type': 'application/json' };

export type ParseImportOptions = {
  processingProfile?: string;
  dataClassification?: string;
};

function parseImportQuery(opts?: ParseImportOptions): string {
  if (!opts) return '';
  const q = new URLSearchParams();
  if (opts.processingProfile?.trim()) q.set('processingProfile', opts.processingProfile.trim());
  if (opts.dataClassification?.trim()) q.set('dataClassification', opts.dataClassification.trim());
  const s = q.toString();
  return s ? `?${s}` : '';
}

function postFormWithProgress(
  url: string,
  formData: FormData,
  token: string,
  onProgress?: ParseProgressCallback,
  progressTracker?: ParseUploadProgressController,
): Promise<ParsedDocument> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    const progress = progressTracker ?? createServerWaitProgress(onProgress);
    let serverPhase = false;

    const fail = (message: string) => {
      progress.stop();
      reject(new Error(message));
    };

    xhr.upload.addEventListener('progress', (e) => {
      if (e.lengthComputable && e.total > 0) {
        progress.uploadPercent(e.loaded / e.total);
      } else {
        progress.uploadIndeterminate();
      }
    });

    xhr.upload.addEventListener('load', () => {
      serverPhase = true;
      progress.startServerProcessing();
    });

    xhr.addEventListener('readystatechange', () => {
      if (xhr.readyState === XMLHttpRequest.HEADERS_RECEIVED && serverPhase) {
        progress.serverAlmostDone('Сохранение и подготовка результата…');
      }
      if (xhr.readyState !== XMLHttpRequest.DONE) return;

      if (xhr.status >= 200 && xhr.status < 300) {
        progress.complete();
        try {
          resolve(JSON.parse(xhr.responseText) as ParsedDocument);
        } catch {
          fail('Некорректный ответ сервера');
        }
        return;
      }

      const errText = xhr.responseText?.trim();
      fail(errText || 'Ошибка при загрузке файла');
    });

    xhr.addEventListener('error', () => fail('Сетевая ошибка при загрузке'));
    xhr.addEventListener('abort', () => fail('Загрузка отменена'));

    xhr.open('POST', url);
    const headers = authHeaders(token) as Record<string, string>;
    if (headers.Authorization) {
      xhr.setRequestHeader('Authorization', headers.Authorization);
    }
    progress.preparing();
    xhr.send(formData);
  });
}

export async function parseDocument(
  file: File,
  token: string,
  importOpts?: ParseImportOptions,
  onProgress?: ParseProgressCallback,
  progressTracker?: ParseUploadProgressController,
): Promise<ParsedDocument> {
  const formData = new FormData();
  formData.append('file', file);
  const url = `/api/pdf/parse${parseImportQuery(importOpts)}`;
  return postFormWithProgress(url, formData, token, onProgress, progressTracker);
}

export async function parseBatch(
  files: File[],
  token: string,
  importOpts?: ParseImportOptions,
  batchApiKey?: string | null,
  onProgress?: ParseProgressCallback,
): Promise<BatchParseResponse> {
  const formData = new FormData();
  for (const f of files) {
    formData.append('files', f);
  }
  if (importOpts?.processingProfile?.trim()) {
    formData.append('processingProfile', importOpts.processingProfile.trim());
  }
  if (importOpts?.dataClassification?.trim()) {
    formData.append('dataClassification', importOpts.dataClassification.trim());
  }

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    const progress = createServerWaitProgress(onProgress, {
      uploadMessage: `Загрузка пакета (${files.length} файлов)…`,
      prepareMessage: 'Подготовка пакета…',
    });
    let serverPhase = false;

    const fail = (message: string) => {
      progress.stop();
      reject(new Error(message));
    };

    xhr.upload.addEventListener('progress', (e) => {
      if (e.lengthComputable && e.total > 0) {
        progress.uploadPercent(e.loaded / e.total);
      } else {
        progress.uploadIndeterminate();
      }
    });

    xhr.upload.addEventListener('load', () => {
      serverPhase = true;
      progress.startServerProcessing();
    });

    xhr.addEventListener('readystatechange', () => {
      if (xhr.readyState === XMLHttpRequest.HEADERS_RECEIVED && serverPhase) {
        progress.serverAlmostDone('Формирование результата…');
      }
      if (xhr.readyState !== XMLHttpRequest.DONE) return;

      if (xhr.status >= 200 && xhr.status < 300) {
        progress.complete();
        try {
          resolve(JSON.parse(xhr.responseText) as BatchParseResponse);
        } catch {
          fail('Некорректный ответ сервера');
        }
        return;
      }
      const errText = xhr.responseText?.trim();
      fail(errText || 'Ошибка пакетной загрузки');
    });

    xhr.addEventListener('error', () => fail('Сетевая ошибка при пакетной загрузке'));

    xhr.open('POST', '/api/enterprise/parse-batch');
    const headers = authHeaders(token) as Record<string, string>;
    if (headers.Authorization) {
      xhr.setRequestHeader('Authorization', headers.Authorization);
    }
    if (batchApiKey?.trim()) {
      xhr.setRequestHeader('X-Enterprise-Batch-Key', batchApiKey.trim());
    }
    progress.preparing();
    xhr.send(formData);
  });
}

export async function fetchAuditLog(
  token: string,
  opts?: { take?: number; all?: boolean },
): Promise<AuditLogEntry[]> {
  const q = new URLSearchParams();
  const take = opts?.take ?? 100;
  q.set('take', String(take));
  if (opts?.all) q.set('all', 'true');
  const response = await fetch(`/api/enterprise/audit?${q}`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось загрузить журнал'));
  }
  return response.json() as Promise<AuditLogEntry[]>;
}

export async function fetchDocumentEntities(token: string, docId: number): Promise<ExtractedEntities> {
  const response = await fetch(`/api/enterprise/documents/${docId}/entities`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось извлечь сущности'));
  }
  return response.json() as Promise<ExtractedEntities>;
}

export async function validateDocumentChecklist(
  token: string,
  docId: number,
  checklistId: string,
): Promise<ChecklistValidateResult> {
  const q = encodeURIComponent(checklistId);
  const response = await fetch(`/api/enterprise/documents/${docId}/checklist?checklistId=${q}`, {
    method: 'POST',
    headers: authHeaders(token),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Проверка чек-листа не выполнена'));
  }
  return response.json() as Promise<ChecklistValidateResult>;
}

export interface SetupStatus {
  needsBootstrap: boolean;
}

export async function fetchSetupStatus(): Promise<SetupStatus> {
  const response = await fetch('/api/auth/setup-status');
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось проверить состояние системы'));
  }
  return response.json() as Promise<SetupStatus>;
}

export async function authLogin(email: string, password: string): Promise<AuthResponse> {
  const response = await fetch('/api/auth/login', {
    method: 'POST',
    headers: jsonContent,
    body: JSON.stringify({ email, password }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка входа'));
  }
  return response.json() as Promise<AuthResponse>;
}

export async function authBootstrap(email: string, password: string): Promise<AuthResponse> {
  const response = await fetch('/api/auth/bootstrap', {
    method: 'POST',
    headers: jsonContent,
    body: JSON.stringify({ email, password }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось создать администратора'));
  }
  return response.json() as Promise<AuthResponse>;
}

export type CreateUserParams = {
  email: string;
  password: string;
  displayName: string;
  role: string;
  departmentId: number;
};

export async function createUserAccount(token: string, params: CreateUserParams): Promise<UserBrief> {
  const response = await fetch('/api/auth/users', {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify(params),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось создать пользователя'));
  }
  return response.json() as Promise<UserBrief>;
}

export async function listMyDocuments(token: string): Promise<ParsedDocument[]> {
  const response = await fetch('/api/pdf/my', { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка загрузки документов'));
  }
  return response.json() as Promise<ParsedDocument[]>;
}

export async function shareDocument(token: string, documentId: number, targetEmail: string): Promise<void> {
  const response = await fetch('/api/pdf/share', {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ documentId, targetEmail }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка при отправке документа'));
  }
}

export async function fetchDocumentShares(token: string, documentId: number) {
  const response = await fetch(`/api/pdf/${documentId}/shares`, { headers: authHeaders(token) });
  if (!response.ok) throw new Error(await readResponseError(response, 'Не удалось загрузить список доступа'));
  return response.json() as Promise<import('../types/office').DocumentShareItem[]>;
}

export async function revokeDocumentShare(token: string, documentId: number, shareId: number) {
  const response = await fetch(`/api/pdf/${documentId}/shares/${shareId}`, {
    method: 'DELETE',
    headers: authHeaders(token),
  });
  if (!response.ok) throw new Error(await readResponseError(response, 'Не удалось отозвать доступ'));
}

export function originalDocumentUrl(documentId: number) {
  return `/api/pdf/${documentId}/original`;
}

export function pagePreviewUrl(documentId: number, page: number) {
  return `/api/pdf/${documentId}/pages/${page}/preview`;
}

export async function fetchDocumentPageCount(token: string, documentId: number): Promise<number> {
  const response = await fetch(`/api/pdf/${documentId}/page-count`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось получить число страниц'));
  }
  const data = (await response.json()) as { pageCount: number };
  return Math.max(1, data.pageCount ?? 1);
}

export async function fetchPagePreviewBlob(
  token: string,
  documentId: number,
  page: number,
): Promise<Blob> {
  const response = await fetch(pagePreviewUrl(documentId, page), { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось загрузить превью страницы'));
  }
  return response.blob();
}

export async function downloadOriginalDocument(
  token: string,
  docId: number,
  fileName: string,
): Promise<void> {
  const response = await fetch(originalDocumentUrl(docId), { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось скачать оригинал'));
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}

export async function sendDocumentByEmail(
  token: string,
  documentId: number,
  targetEmail: string,
  format: 'docx' | 'pdf',
): Promise<void> {
  const response = await fetch(`/api/pdf/${documentId}/send-email`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ targetEmail, format }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось отправить документ на email'));
  }
}

export async function getDocument(token: string, docId: number): Promise<ParsedDocument> {
  const response = await fetch(`/api/pdf/${docId}`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось открыть документ'));
  }
  return response.json() as Promise<ParsedDocument>;
}

export async function saveDocumentText(token: string, docId: number, text: string): Promise<ParsedDocument> {
  const response = await fetch(`/api/pdf/${docId}/text`, {
    method: 'PUT',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ text }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось сохранить изменения'));
  }
  return response.json() as Promise<ParsedDocument>;
}

export async function fetchGigaChatStatus(): Promise<{ configured: boolean }> {
  const response = await fetch('/api/ai/status');
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось проверить GigaChat'));
  }
  return response.json() as Promise<{ configured: boolean }>;
}

export async function regenerateDocumentSummary(
  token: string,
  docId: number,
): Promise<{ aiSummary: string; source: string }> {
  const response = await fetch(`/api/ai/summarize/${docId}`, {
    method: 'POST',
    headers: authHeaders(token),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось сформировать описание'));
  }
  return response.json() as Promise<{ aiSummary: string; source: string }>;
}

export async function runSpellcheck(
  text: string,
  token: string,
  opts?: { documentId?: number },
): Promise<SpellcheckResponse> {
  const body: Record<string, unknown> = {
    text,
    language: 'ru_RU',
    maxSuggestions: 5,
    maxMistakes: 200,
  };
  if (opts?.documentId != null) {
    body.documentId = opts.documentId;
  }
  const response = await fetch('/api/spellcheck/check', {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка проверки орфографии'));
  }
  return response.json() as Promise<SpellcheckResponse>;
}

export async function deleteDocument(token: string, docId: number): Promise<void> {
  const response = await fetch(`/api/pdf/${docId}`, {
    method: 'DELETE',
    headers: authHeaders(token),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось удалить документ'));
  }
}

export async function exportDocument(
  token: string,
  docId: number,
  fileName: string,
  format: 'docx' | 'pdf' | 'signed-pdf',
): Promise<void> {
  const response = await fetch(`/api/pdf/${docId}/export?format=${format}`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка экспорта'));
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = format === 'signed-pdf' ? `${fileName}.pdf` : `${fileName}.${format}`;
  a.click();
  URL.revokeObjectURL(url);
}

