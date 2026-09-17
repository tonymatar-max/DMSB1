import axios from 'axios';

// Same-origin in every environment: the API serves this SPA directly from wwwroot in production,
// and the Vite dev server proxies /api to the API (see vite.config.ts) in development. Call sites
// already include the "/api/..." prefix, so this stays empty rather than "/api".
const baseURL = import.meta.env.VITE_API_URL ?? '';

const apiClient = axios.create({
  baseURL,
});

apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// A stale/expired token previously just surfaced as a confusing "could not load" error on
// whatever page happened to be open, since nothing ever redirected back to login. Any 401 now
// clears the dead token and sends the user to sign in again immediately.
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401 && window.location.pathname !== '/login') {
      localStorage.removeItem('accessToken');
      window.location.href = '/login';
    }
    return Promise.reject(error);
  },
);

export function logout() {
  localStorage.removeItem('accessToken');
  window.location.href = '/login';
}

export { apiClient as api };
export default apiClient;
