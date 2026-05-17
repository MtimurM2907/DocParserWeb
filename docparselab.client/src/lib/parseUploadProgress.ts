export type ParseProgress = {
  phase: 'upload' | 'parse';
  percent: number;
  message: string;
};

export type ParseProgressCallback = (progress: ParseProgress) => void;

export type ParseUploadProgressController = ReturnType<typeof createServerWaitProgress>;

export type ServerParseProgressEvent = {
  page: number;
  totalPages: number;
  percent: number;
  message: string;
};

const UPLOAD_MAX_PERCENT = 32;
/** Верхняя граница «угадывания», пока нет событий SignalR со страницами. */
const SIMULATED_SERVER_CAP = 42;

type ServerWaitProgressOptions = {
  uploadMessage?: string;
  prepareMessage?: string;
};

/** Плавный прогресс загрузки; серверный этап — через SignalR (parseProgress). */
export function createServerWaitProgress(
  onProgress?: ParseProgressCallback,
  options?: ServerWaitProgressOptions,
) {
  let timer: ReturnType<typeof setInterval> | null = null;
  let value = UPLOAD_MAX_PERCENT;
  let serverEventsActive = false;

  const uploadMessage = options?.uploadMessage ?? 'Загрузка файла на сервер…';
  const prepareMessage = options?.prepareMessage ?? 'Подготовка…';

  const emit = (percent: number, message: string, phase: ParseProgress['phase'] = 'parse') => {
    value = percent;
    onProgress?.({ phase, percent: Math.round(percent), message });
  };

  return {
    uploadPercent(loadedRatio: number) {
      const pct = Math.min(UPLOAD_MAX_PERCENT, Math.max(4, Math.round(loadedRatio * UPLOAD_MAX_PERCENT)));
      onProgress?.({ phase: 'upload', percent: pct, message: uploadMessage });
    },

    uploadIndeterminate() {
      onProgress?.({ phase: 'upload', percent: 12, message: uploadMessage });
    },

    preparing() {
      onProgress?.({ phase: 'upload', percent: 3, message: prepareMessage });
    },

    /** Реальный прогресс со страниц (SignalR). */
    applyServerPageProgress(event: ServerParseProgressEvent) {
      serverEventsActive = true;
      if (timer) {
        clearInterval(timer);
        timer = null;
      }
      emit(event.percent, event.message);
    },

    startServerProcessing() {
      if (serverEventsActive) return;

      if (timer) clearInterval(timer);
      value = UPLOAD_MAX_PERCENT + 2;
      emit(value, 'Обработка на сервере (многостраничный PDF может занять несколько минут)…');

      timer = setInterval(() => {
        if (serverEventsActive) return;
        const cap = SIMULATED_SERVER_CAP;
        const remaining = cap - value;
        if (remaining <= 0.12) return;
        const step = Math.max(0.2, remaining * 0.04);
        value = Math.min(cap, value + step);
        emit(value, 'Ожидание ответа сервера…');
      }, 900);
    },

    serverAlmostDone(message = 'Формирование результата…') {
      if (timer) clearInterval(timer);
      timer = null;
      const pct = serverEventsActive ? Math.max(value, 96) : 96;
      emit(pct, message);
    },

    complete() {
      if (timer) clearInterval(timer);
      timer = null;
      onProgress?.({ phase: 'parse', percent: 100, message: 'Готово' });
    },

    stop() {
      if (timer) clearInterval(timer);
      timer = null;
      serverEventsActive = false;
    },
  };
}
