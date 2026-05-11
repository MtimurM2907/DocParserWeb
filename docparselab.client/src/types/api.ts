export interface ParsedDocument {
  id: number;
  fileName: string;
  originalFileType?: string;
  ownerId?: number | null;
  uploadedAt: string;
  originalText?: string;
  fullText: string;
  editedText?: string | null;
  editedAt?: string | null;
  structuredJson: string;
  aiSummary?: string | null;
}

export interface AuthResponse {
  email: string;
  token: string;
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
}
