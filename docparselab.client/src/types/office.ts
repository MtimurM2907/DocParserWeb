export type MainView = 'workspace' | 'registry' | 'tasks' | 'admin';

export interface Department {
  id: number;
  name: string;
}

export interface UserBrief {
  id: number;
  email: string;
  displayName?: string | null;
  role: string;
  departmentId?: number | null;
  departmentName?: string | null;
}

export interface CurrentUser {
  id: number;
  email: string;
  displayName?: string | null;
  role: string;
  departmentId?: number | null;
  departmentName?: string | null;
}

export interface DocumentRegistryItem {
  id: number;
  fileName: string;
  title: string;
  documentType: string;
  workflowStatus: string;
  dataClassification: string;
  uploadedAt: string;
  ownerId?: number | null;
  ownerEmail?: string | null;
  departmentId?: number | null;
  departmentName?: string | null;
  responsibleUserId?: number | null;
  responsibleUserEmail?: string | null;
  currentApproverUserId?: number | null;
  currentApproverEmail?: string | null;
  tags?: string | null;
  aiSummaryPreview?: string | null;
}

export interface DocumentRegistryPage {
  items: DocumentRegistryItem[];
  total: number;
}

export interface ApprovalTask {
  documentId: number;
  title: string;
  fileName: string;
  workflowStatus: string;
  submittedAt?: string | null;
  ownerEmail?: string | null;
  workflowComment?: string | null;
}

export interface DocumentVersionBrief {
  id: number;
  versionNumber: number;
  changeType: string;
  createdAt: string;
  createdByUserId?: number | null;
  createdByEmail?: string | null;
  textLength: number;
}

export interface DocumentVersionDetail extends DocumentVersionBrief {
  text: string;
}

export interface WorkflowHistoryItem {
  id: number;
  action: string;
  comment?: string | null;
  createdAt: string;
  userEmail?: string | null;
}

export interface DocumentSignature {
  id: number;
  documentId: number;
  textHashSha256: string;
  signedAt: string;
  signerEmail: string;
  signerDisplayName?: string | null;
  signerRole: string;
  comment?: string | null;
  signatureKind: string;
  certificateSubject?: string | null;
  certificateThumbprint?: string | null;
  externalCryptoVerified?: boolean | null;
}

export interface DocumentEditLockStatus {
  isLocked: boolean;
  canEdit: boolean;
  lockedByUserId?: number | null;
  lockedByEmail?: string | null;
  expiresAt?: string | null;
}

export interface SignatureVerification {
  hasSignatures: boolean;
  textMatchesLastSignature: boolean;
  currentTextHashSha256: string;
  lastSignatureHashSha256?: string | null;
  lastSignedAt?: string | null;
  lastSignerEmail?: string | null;
}

export interface DocumentSigningPayload {
  textHashSha256: string;
  contentBase64: string;
  contentByteLength: number;
}

export const WORKFLOW_STATUS_LABELS: Record<string, string> = {
  Draft: 'Черновик',
  OnApproval: 'На согласовании',
  Approved: 'Согласован',
  Signed: 'Подписан',
  Rejected: 'На доработке',
  Archived: 'В архиве',
};

export const DOCUMENT_TYPE_LABELS: Record<string, string> = {
  general: 'Общий',
  letter: 'Письмо',
  memo: 'Служебная записка',
  order: 'Приказ',
  contract: 'Договор',
  instruction: 'Инструкция',
  regulation: 'Регламент',
};

export const DEFAULT_DATA_CLASSIFICATION = 'Public';

export const DATA_CLASSIFICATION_LABELS: Record<string, string> = {
  Public: 'Публичный',
  Confidential: 'Конфиденциальный',
};

/** Подпись грифа (устаревший Internal отображается как публичный). */
export function classificationLabel(value: string | undefined | null): string {
  if (!value || value === 'Internal') return DATA_CLASSIFICATION_LABELS.Public;
  return DATA_CLASSIFICATION_LABELS[value] ?? value;
}

export const OFFICE_ROLES = ['Admin', 'Manager', 'Employee', 'Viewer'] as const;

export const ROLE_LABELS: Record<string, string> = {
  Admin: 'Администратор',
  Manager: 'Руководитель',
  Employee: 'Сотрудник',
  Viewer: 'Только просмотр',
};

export interface UserNotification {
  id: number;
  title: string;
  body: string;
  documentId?: number | null;
  isRead: boolean;
  createdAt: string;
}

export interface DocumentComment {
  id: number;
  text: string;
  createdAt: string;
  userEmail?: string | null;
  userDisplayName?: string | null;
}

export interface ApprovalStep {
  stepOrder: number;
  approverUserId: number;
  approverEmail?: string | null;
  status: string;
  comment?: string | null;
  actedAt?: string | null;
}

export interface VersionDiff {
  fromVersionId: number;
  toVersionId: number;
  lines: { kind: string; text: string }[];
}

export interface DocumentShareItem {
  shareId: number;
  toUserId: number;
  toUserEmail: string;
  sharedAt: string;
}
