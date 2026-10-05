import { useState } from 'react';
import { login } from '../api';
import type { Theme } from '../App';

export default function Login({ onLogin, theme, onToggleTheme }: {
  onLogin: (token: string, name: string) => void;
  theme: Theme;
  onToggleTheme: () => void;
}) {
  const [username, setUsername] = useState('demo');
  const [password, setPassword] = useState('demo123');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const { data } = await login(username, password);
      onLogin(data.token, data.displayName);
    } catch {
      setError('Sai tài khoản hoặc mật khẩu.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="center">
      <button className="theme-fab" onClick={onToggleTheme} title="Đổi giao diện sáng/tối">
        {theme === 'light' ? '🌙' : '☀️'}
      </button>
      <form className="card" onSubmit={submit}>
        <h1>Banking Basic</h1>
        <p className="muted">Đăng nhập (demo / demo123)</p>
        <label>Tài khoản
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoFocus />
        </label>
        <label>Mật khẩu
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        {error && <div className="error">{error}</div>}
        <button disabled={busy} type="submit">{busy ? 'Đang đăng nhập…' : 'Đăng nhập'}</button>
      </form>
    </div>
  );
}
