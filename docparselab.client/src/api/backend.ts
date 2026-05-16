import type {
  AuditLogEntry,
  AuthResponse,
  BatchParseResponse,
  ChecklistValidateResult,
  ExtractedEntities,
  ParsedDocument,
  RewriteResponse,
  SpellcheckResponse,
} from '../types/api';
import type { UserBrief } from '../types/office';
import { authHeaders, readResponseError } from '../lib/http';

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

export async function parseDocument(
  file: File,
  token: string,
  importOpts?: ParseImportOptions,
): Promise<ParsedDocument> {
  const formData = new FormData();
  formData.append('file', file);
  const response = await fetch(`/api/pdf/parse${parseImportQuery(importOpts)}`, {
    method: 'POST',
    body: formData,
    headers: authHeaders(token),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка при загрузке файла'));
  }
  return response.json() as Promise<ParsedDocument>;
}

export async function parseBatch(
  files: File[],
  token: string,
  importOpts?: ParseImportOptions,
  batchApiKey?: string | null,
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
  const headers = new Headers(authHeaders(token) as HeadersInit);
  if (batchApiKey?.trim()) {
    headers.set('X-Enterprise-Batch-Key', batchApiKey.trim());
  }
  const response = await fetch('/api/enterprise/parse-batch', {
    method: 'POST',
    body: formData,
    headers,
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка пакетной загрузки'));
  }
  return response.json() as Promise<BatchParseResponse>;
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

export type RewriteTextParams = {
  text: string;
  mode?: string;
  tone?: string;
  length?: string;
  documentId?: number;
  token: string;
};

export async function rewriteText(params: RewriteTextParams): Promise<RewriteResponse> {
  const body: Record<string, unknown> = {
    text: params.text,
    mode: params.mode ?? 'Более формально',
    tone: params.tone ?? 'нейтральный',
    length: params.length ?? 'сопоставимая с оригиналом',
  };
  if (params.documentId != null) {
    body.documentId = params.documentId;
  }
  const response = await fetch('/api/ai/rewrite', {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(params.token) },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось переписать текст'));
  }
  return response.json() as Promise<RewriteResponse>;
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
  role: string;
  departmentId?: number | null;
  displayName?: string | null;
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

export async function exportDocument(token: string, docId: number, fileName: string, format: 'docx' | 'pdf'): Promise<void> {
  const response = await fetch(`/api/pdf/${docId}/export?format=${format}`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка экспорта'));
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${fileName}.${format}`;
  a.click();
  URL.revokeObjectURL(url);
}

