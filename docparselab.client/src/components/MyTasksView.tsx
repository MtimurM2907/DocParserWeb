import { useCallback, useEffect, useState } from 'react';
import type { ApprovalTask } from '../types/office';
import { fetchMyTasks } from '../api/office';

type Props = {
  token: string;
  onOpenDocument: (id: number) => void;
};

export function MyTasksView({ token, onOpenDocument }: Props) {
  const [tasks, setTasks] = useState<ApprovalTask[]>([]);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setTasks(await fetchMyTasks(token));
    } catch {
      setTasks([]);
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <section className="office-tasks">
      <div className="office-tasks-header">
        <h2>Мои задачи на согласование</h2>
        <button type="button" onClick={() => void load()} disabled={loading}>
          {loading ? '…' : 'Обновить'}
        </button>
      </div>
      {tasks.length === 0 && !loading ? (
        <p>Нет документов, ожидающих вашего согласования.</p>
      ) : (
        <ul className="tasks-list">
          {tasks.map((t) => (
            <li key={t.documentId} className="tasks-list-item">
              <div>
                <strong>{t.title}</strong>
                <div className="tasks-meta">
                  От: {t.ownerEmail ?? '—'}
                  {t.submittedAt && ` · ${new Date(t.submittedAt).toLocaleString()}`}
                </div>
                {t.workflowComment && <p className="tasks-comment">{t.workflowComment}</p>}
              </div>
              <button type="button" onClick={() => onOpenDocument(t.documentId)}>
                Открыть и согласовать
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
