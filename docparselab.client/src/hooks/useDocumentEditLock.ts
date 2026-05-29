import type { HubConnection } from '@microsoft/signalr';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { DocumentEditLockStatus } from '../types/office';
import { acquireEditLock, fetchEditLock, releaseEditLock } from '../api/office';
import { createDocumentHubConnection } from '../lib/documentHub';

export function useDocumentEditLock(
  token: string | null,
  documentId: number | null,
  editing: boolean,
) {
  const [lock, setLock] = useState<DocumentEditLockStatus | null>(null);
  const hubRef = useRef<HubConnection | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    if (!token || documentId == null) return;
    try {
      setLock(await fetchEditLock(token, documentId));
      setError(null);
    } catch (e) {
      setLock(null);
      setError(e instanceof Error ? e.message : 'Не удалось получить состояние блокировки');
    }
  }, [token, documentId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!token || documentId == null) return;
    const hub = createDocumentHubConnection(token);
    hubRef.current = hub;
    hub.on('lockChanged', (status: DocumentEditLockStatus) => setLock(status));
    void hub
      .start()
      .then(() => hub.invoke('JoinDocument', documentId))
      .catch(() => {
        setError('Не удалось подключить обновления блокировки документа');
      });
    return () => {
      hubRef.current = null;
      void hub
        .invoke('LeaveDocument', documentId)
        .catch(() => {})
        .finally(() => void hub.stop());
    };
  }, [token, documentId]);

  useEffect(() => {
    if (!token || documentId == null || !editing) return;
    let cancelled = false;
    void acquireEditLock(token, documentId)
      .then((s) => {
        if (!cancelled) setLock(s);
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Не удалось захватить блокировку');
      });
    const interval = setInterval(() => {
      void acquireEditLock(token, documentId)
        .then((status) => {
          setLock(status);
          setError(null);
        })
        .catch((e) => setError(e instanceof Error ? e.message : 'Не удалось продлить блокировку'));
    }, 120_000);
    return () => {
      cancelled = true;
      clearInterval(interval);
      void releaseEditLock(token, documentId).catch(() => {});
    };
  }, [token, documentId, editing]);

  return { lock, refresh, error };
}
