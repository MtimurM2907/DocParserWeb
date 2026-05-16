import { useEffect, useState } from 'react';
import { IconLogo } from './AppIcons';
import { authBootstrap, authLogin, fetchSetupStatus } from '../api/backend';
import type { AuthResponse } from '../types/api';

type Props = {
  onAuthenticated: (data: AuthResponse) => void;
};

export function LoginScreen({ onAuthenticated }: Props) {
  const [needsBootstrap, setNeedsBootstrap] = useState<boolean | null>(null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void fetchSetupStatus()
      .then((s) => {
        if (!cancelled) setNeedsBootstrap(s.needsBootstrap);
      })
      .catch(() => {
        if (!cancelled) setNeedsBootstrap(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const submit = async () => {
    setError(null);
    if (!email.trim() || !password) {
      setError('Укажите email и пароль.');
      return;
    }
    if (needsBootstrap && password !== confirmPassword) {
      setError('Пароли не совпадают.');
      return;
    }
    if (needsBootstrap && password.length < 6) {
      setError('Пароль должен быть не короче 6 символов.');
      return;
    }

    setBusy(true);
    try {
      const data = needsBootstrap
        ? await authBootstrap(email.trim(), password)
        : await authLogin(email.trim(), password);
      onAuthenticated(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка входа');
    } finally {
      setBusy(false);
    }
  };

  if (needsBootstrap === null) {
    return (
      <div className="login-screen">
        <p className="registry-meta">Проверка системы…</p>
      </div>
    );
  }

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="login-brand">
          <IconLogo className="app-logo login-logo" />
          <h1>DocParseLab</h1>
          <p className="login-subtitle">
            {needsBootstrap
              ? 'Первый запуск: создайте учётную запись администратора'
              : 'Вход в информационную систему документов'}
          </p>
        </div>
        {error && <p className="office-card-error">{error}</p>}
        <label className="parse-field">
          <span className="parse-field-label">Email</span>
          <input
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            disabled={busy}
            onKeyDown={(e) => e.key === 'Enter' && void submit()}
          />
        </label>
        <label className="parse-field">
          <span className="parse-field-label">Пароль</span>
          <input
            type="password"
            autoComplete={needsBootstrap ? 'new-password' : 'current-password'}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={busy}
            onKeyDown={(e) => e.key === 'Enter' && void submit()}
          />
        </label>
        {needsBootstrap && (
          <label className="parse-field">
            <span className="parse-field-label">Повторите пароль</span>
            <input
              type="password"
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              disabled={busy}
              onKeyDown={(e) => e.key === 'Enter' && void submit()}
            />
          </label>
        )}
        <button type="button" className="login-submit" disabled={busy} onClick={() => void submit()}>
          {busy ? '…' : needsBootstrap ? 'Создать администратора' : 'Войти'}
        </button>
        {!needsBootstrap && (
          <p className="login-hint">Новых пользователей добавляет администратор в разделе «Администрирование».</p>
        )}
      </div>
    </div>
  );
}
