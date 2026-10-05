import { useEffect, useState } from 'react';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';

export type Theme = 'light' | 'dark';

export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('token'));
  const [displayName, setDisplayName] = useState<string>(() => localStorage.getItem('displayName') ?? '');
  const [theme, setTheme] = useState<Theme>(() => (localStorage.getItem('theme') as Theme) ?? 'light');

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('theme', theme);
  }, [theme]);

  const toggleTheme = () => setTheme((t) => (t === 'light' ? 'dark' : 'light'));

  const handleLogin = (t: string, name: string) => {
    localStorage.setItem('token', t);
    localStorage.setItem('displayName', name);
    setToken(t);
    setDisplayName(name);
  };

  const handleLogout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('displayName');
    setToken(null);
    setDisplayName('');
  };

  return token
    ? <Dashboard displayName={displayName} onLogout={handleLogout} theme={theme} onToggleTheme={toggleTheme} />
    : <Login onLogin={handleLogin} theme={theme} onToggleTheme={toggleTheme} />;
}
