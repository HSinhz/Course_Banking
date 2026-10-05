import axios from 'axios';

export const api = axios.create({ baseURL: '/api/v1' });

// Gắn Bearer token vào mọi request (giống frontend/src/api/axios.ts của HCM).
api.interceptors.request.use((cfg) => {
  const token = localStorage.getItem('token');
  if (token) cfg.headers.Authorization = `Bearer ${token}`;
  return cfg;
});

// 401 -> đăng xuất.
api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err?.response?.status === 401) {
      localStorage.removeItem('token');
      localStorage.removeItem('displayName');
      window.location.reload();
    }
    return Promise.reject(err);
  },
);

export interface Account { id: string; name: string; balance: number; }
export interface TransferResult { transferId: string; fromBalanceAfter: number; status: string; servedFromCache: boolean; }

export const login = (username: string, password: string) =>
  api.post<{ token: string; displayName: string }>('/auth/login', { username, password });

export const getAccounts = () => api.get<Account[]>('/accounts');

export const transfer = (
  idempotencyKey: string,
  fromAccountId: string,
  toAccountId: string,
  amount: number,
) =>
  api.post<TransferResult>(
    '/transfers',
    { fromAccountId, toAccountId, amount },
    { headers: { 'Idempotency-Key': idempotencyKey } },
  );

// ---- Lab: mô phỏng crash giữa transaction (DB thật) ----
export type CrashScenario = 'AppCrash' | 'DbCrash' | 'Commit';
export interface CrashSnapshot { fromBalance: number; toBalance: number; transferCount: number; keyExists: boolean; }
export interface CrashStep { label: string; detail: string; ok: boolean; }
export interface CrashResult {
  scenario: string;
  scenarioTitle: string;
  idempotencyKey: string;
  amount: number;
  fromName: string;
  toName: string;
  before: CrashSnapshot;
  during: CrashSnapshot;
  after: CrashSnapshot;
  committed: boolean;
  moneyMoved: boolean;
  keyWritten: boolean;
  retrySafe: boolean;
  verdict: string;
  steps: CrashStep[];
}

export const simulateCrash = (
  fromAccountId: string,
  toAccountId: string,
  amount: number,
  scenario: CrashScenario,
) =>
  api.post<CrashResult>('/crash-lab/simulate', { fromAccountId, toAccountId, amount, scenario });
