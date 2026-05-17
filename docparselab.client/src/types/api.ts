export interface ParsedDocument {
  id: number;
  fileName: string;
  originalFileType?: string;
  ownerId?: number | null;
  ownerEmail?: string | null;
  uploadedAt: string;
  originalText?: string;
  fullText: string;
  editedText?: string | null;
  editedAt?: string | null;
  structuredJson: string;
  aiSummary?: string | null;
  processingProfile?: string;
  dataClassification?: string;
  shareCount?: number;
  textLength?: number;
  title?: string | null;
  documentType?: string;
  workflowStatus?: string;
  departmentId?: number | null;
  departmentName?: string | null;
  responsibleUserId?: number | null;
  responsibleUserEmail?: string | null;
  currentApproverUserId?: number | null;
  currentApproverEmail?: string | null;
  workflowComment?: string | null;
  submittedAt?: string | null;
  tags?: string | null;
  canEdit?: boolean;
  canApprove?: boolean;
  canSign?: boolean;
  signatureCount?: number;
  textIntegrityValid?: boolean;
  lastSignedAt?: string | null;
  lastSignerEmail?: string | null;
  hasOriginalFile?: boolean;
  originalPageCount?: number | null;
}

export interface BatchParseResponse {
  documents: ParsedDocument[];
  errors: string[];
}

export interface AuditLogEntry {
  id: number;
  createdAt: string;
  userId?: number | null;
  userEmailSnapshot?: string | null;
  action: string;
  resource?: string | null;
  details?: string | null;
  ipAddress?: string | null;
}

export interface ExtractedEntities {
  dates: string[];
  money: string[];
  inn: string[];
  emails: string[];
}

export interface ChecklistValidateResult {
  checklistId: string;
  ok: boolean;
  missing: string[];
}

export interface AuthResponse {
  userId: number;
  email: string;
  token: string;
  role: string;
  departmentId?: number | null;
  departmentName?: string | null;
  displayName?: string | null;
}

export interface SpellcheckMistake {
  word: string;
  start: number;
  length: number;
  suggestions: string[];
}

export interface SpellcheckResponse {
  language: string;
  textLength: number;
  mistakes: SpellcheckMistake[];
  /** gigachat | hunspell — для Confidential используется локальный словарь */
  spellcheckEngine?: string;
}
