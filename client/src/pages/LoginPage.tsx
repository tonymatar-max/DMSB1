import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import apiClient from '../api/client';
import BrandMark from '../components/BrandMark';

export default function LoginPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const response = await apiClient.post('/api/auth/login', { email, password });
      const { accessToken } = response.data;
      if (!accessToken) {
        throw new Error('No access token returned');
      }
      localStorage.setItem('accessToken', accessToken);
      navigate('/cabinets');
    } catch {
      setError('Invalid email or password.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      className="flex min-h-screen items-center justify-center px-4"
      style={{ background: 'var(--page)' }}
    >
      <div className="panel w-full max-w-sm !m-0 p-8">
        <div className="mb-6 flex flex-col items-center gap-3 text-center">
          <BrandMark size="lg" />
          <h1 className="text-lg font-bold" style={{ color: 'var(--ink)' }}>
            Nexus <span className="font-medium" style={{ color: 'var(--muted)' }}>Docs</span>
          </h1>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <div>
            <label className="field-label" htmlFor="email">
              Email
            </label>
            <input
              id="email"
              type="email"
              className="field-input"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
              required
            />
          </div>

          <div>
            <label className="field-label" htmlFor="password">
              Password
            </label>
            <input
              id="password"
              type="password"
              className="field-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
          </div>

          {error && (
            <p className="text-sm" style={{ color: 'var(--danger)' }}>
              {error}
            </p>
          )}

          <button type="submit" className="btn primary w-full" disabled={loading}>
            {loading ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        <p className="mt-5 text-center text-xs" style={{ color: 'var(--muted)' }}>
          dev seed login: admin@nexusdocs.dev / ChangeMe123!
        </p>
      </div>
    </div>
  );
}
