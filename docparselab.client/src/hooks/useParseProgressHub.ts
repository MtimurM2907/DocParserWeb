import { useCallback, useRef } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { createDocumentHubConnection } from '../lib/documentHub';
import type { ServerParseProgressEvent } from '../lib/parseUploadProgress';

export function useParseProgressHub() {
  const hubRef = useRef<HubConnection | null>(null);

  const subscribe = useCallback(
    async (token: string, onPageProgress: (event: ServerParseProgressEvent) => void) => {
      // Ensure old connection is fully stopped before creating a new one.
      if (hubRef.current) {
        try {
          hubRef.current.off('parseProgress');
          await hubRef.current.stop();
        } catch {
          // ignore cleanup errors
        } finally {
          hubRef.current = null;
        }
      }
      const hub = createDocumentHubConnection(token);
      hub.on('parseProgress', onPageProgress);
      await hub.start();
      hubRef.current = hub;
      return hub;
    },
    [],
  );

  const unsubscribe = useCallback(async () => {
    const hub = hubRef.current;
    hubRef.current = null;
    if (!hub) return;
    try {
      hub.off('parseProgress');
      await hub.stop();
    } catch {
      // ignore disconnect errors
    }
  }, []);

  return { subscribe, unsubscribe };
}
