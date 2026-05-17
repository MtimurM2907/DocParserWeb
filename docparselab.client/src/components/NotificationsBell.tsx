import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { fetchNotifications, markNotificationRead } from '../api/office';
import { createDocumentHubConnection } from '../lib/documentHub';
import type { UserNotification } from '../types/office';
import { IconBell } from './AppIcons';

type Props = { token: string; onOpenDocument?: (docId: number) => void };

export function NotificationsBell({ token, onOpenDocument }: Props) {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<UserNotification[]>([]);
  const [panelStyle, setPanelStyle] = useState<React.CSSProperties>({});
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const load = useCallback(async () => {
    try {
      setItems(await fetchNotifications(token, true));
    } catch {
      setItems([]);
    }
  }, [token]);

  useEffect(() => {
    void load();
    const t = setInterval(() => void load(), 60_000);
    const hub = createDocumentHubConnection(token);
    hub.on('notification', () => void load());
    void hub.start();
    return () => {
      clearInterval(t);
      void hub.stop();
    };
  }, [load, token]);

  const updatePanelPosition = useCallback(() => {
    const el = triggerRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const width = Math.min(360, window.innerWidth - 24);
    const left = Math.min(Math.max(12, rect.right - width), window.innerWidth - width - 12);
    const top = rect.bottom + 8;
    setPanelStyle({
      position: 'fixed',
      top,
      left,
      width,
      zIndex: 200,
    });
  }, []);

  useEffect(() => {
    if (!open) return;
    updatePanelPosition();
    const onReposition = () => updatePanelPosition();
    window.addEventListener('resize', onReposition);
    window.addEventListener('scroll', onReposition, true);
    return () => {
      window.removeEventListener('resize', onReposition);
      window.removeEventListener('scroll', onReposition, true);
    };
  }, [open, updatePanelPosition]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (rootRef.current?.contains(target)) return;
      const panel = document.getElementById('notifications-bell-panel');
      if (panel?.contains(target)) return;
      setOpen(false);
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  const unread = items.filter((n) => !n.isRead).length;

  const panel =
    open &&
    createPortal(
      <>
        <button
          type="button"
          className="notifications-bell__backdrop"
          aria-label="Закрыть уведомления"
          onClick={() => setOpen(false)}
        />
        <div
          id="notifications-bell-panel"
          className="notifications-bell__panel"
          style={panelStyle}
          role="dialog"
          aria-label="Уведомления"
        >
          <div className="notifications-bell__panel-head">
            <h3>Уведомления</h3>
            {unread > 0 && <span className="notifications-bell__panel-count">{unread} новых</span>}
          </div>
          {items.length === 0 ? (
            <p className="notifications-bell__empty">Нет непрочитанных уведомлений</p>
          ) : (
            <ul className="notifications-list">
              {items.map((n) => (
                <li key={n.id}>
                  <button
                    type="button"
                    className="notifications-list__item"
                    onClick={() => {
                      void markNotificationRead(token, n.id).then(load);
                      if (n.documentId && onOpenDocument) onOpenDocument(n.documentId);
                      setOpen(false);
                    }}
                  >
                    <strong>{n.title}</strong>
                    <span>{n.body}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </>,
      document.body,
    );

  return (
    <div ref={rootRef} className={`notifications-bell${open ? ' notifications-bell--open' : ''}`}>
      <button
        ref={triggerRef}
        type="button"
        className="notifications-bell__trigger"
        onClick={() => {
          if (!open) updatePanelPosition();
          setOpen((o) => !o);
        }}
        aria-expanded={open}
        aria-haspopup="true"
        title="Уведомления"
      >
        <IconBell className="notifications-bell__icon" />
        {unread > 0 && (
          <span className="notifications-bell__badge" aria-label={`Непрочитанных: ${unread}`}>
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>
      {panel}
    </div>
  );
}
