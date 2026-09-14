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

export { apiClient as api };
export default apiClient;
