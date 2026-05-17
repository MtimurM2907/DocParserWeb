import { type FormEvent, useEffect, useState } from 'react';
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

  const submit = async (e?: FormEvent) => {
    e?.preventDefault();
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
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка входа');
    } finally {
      setBusy(false);
    }
  };

  if (needsBootstrap === null) {
    return (
      <div className="login-screen">
        <div className="login-card login-card--loading">
          <div className="login-brand">
            <IconLogo className="login-logo" />
            <h1>DocParseLab</h1>
            <p className="login-subtitle">Проверка системы…</p>
          </div>
          <div className="login-spinner" aria-hidden />
        </div>
      </div>
    );
  }

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="login-brand">
          <IconLogo className="login-logo" />
          <h1>DocParseLab</h1>
          {needsBootstrap && <span className="login-badge">Первоначальная настройка</span>}
          <p className="login-subtitle">
            {needsBootstrap
              ? 'Создайте учётную запись администратора для запуска системы'
              : 'Вход в информационную систему документов'}
          </p>
        </div>

        <form
          className="login-form login-form--compact"
          onSubmit={(e) => {
            void submit(e);
          }}
        >
          {error && (
            <div className="login-error" role="alert">
              {error}
            </div>
          )}

          <label className="login-field">
            <span className="login-field__label">Эл. почта</span>
            <input
              type="email"
              autoComplete="username"
              placeholder="user@company.ru"
              value={email}
              onChange={(ev) => setEmail(ev.target.value)}
              disabled={busy}
            />
          </label>

          <label className="login-field">
            <span className="login-field__label">Пароль</span>
            <input
              type="password"
              autoComplete={needsBootstrap ? 'new-password' : 'current-password'}
              placeholder="••••••••"
              value={password}
              onChange={(ev) => setPassword(ev.target.value)}
              disabled={busy}
            />
          </label>

          {needsBootstrap && (
            <label className="login-field">
              <span className="login-field__label">Повторите пароль</span>
              <input
                type="password"
                autoComplete="new-password"
                placeholder="••••••••"
                value={confirmPassword}
                onChange={(ev) => setConfirmPassword(ev.target.value)}
                disabled={busy}
              />
            </label>
          )}

          <button type="submit" className="login-submit btn-primary" disabled={busy}>
            {busy ? 'Выполняется вход…' : needsBootstrap ? 'Создать администратора' : 'Войти'}
          </button>
        </form>
      </div>
    </div>
  );
}
