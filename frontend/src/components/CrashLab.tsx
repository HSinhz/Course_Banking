import { useState } from 'react';
import { simulateCrash, type Account, type CrashResult, type CrashScenario } from '../api';

const fmt = (n: number) => n.toLocaleString('en-US');

const SCENARIOS: { key: CrashScenario; label: string; hint: string; danger: boolean }[] = [
  { key: 'AppCrash', label: '2A · App chết', hint: 'Tiến trình app chết giữa chừng, chưa kịp COMMIT', danger: true },
  { key: 'DbCrash', label: '2B · DB chết', hint: 'DB sập khi WAL chưa có commit record', danger: true },
  { key: 'Commit', label: 'Đối chứng · COMMIT', hint: 'Chạy trọn vẹn để so sánh', danger: false },
];

// Bảng so sánh Trước / Trong (kết nối khác) / Sau.
function SnapshotTable({ r }: { r: CrashResult }) {
  const rows: { label: string; get: (s: CrashResult['before']) => string; flag?: boolean }[] = [
    { label: `Số dư ${r.fromName}`, get: (s) => `${fmt(s.fromBalance)} VND` },
    { label: `Số dư ${r.toName}`, get: (s) => `${fmt(s.toBalance)} VND` },
    { label: 'Tổng số Transfers', get: (s) => String(s.transferCount) },
    { label: 'Key tồn tại trong DB?', get: (s) => (s.keyExists ? '✅ Có' : '❌ Không'), flag: true },
  ];
  const cell: React.CSSProperties = { padding: '6px 8px', borderBottom: '1px solid #e6edf6', fontSize: 12.5 };
  const head: React.CSSProperties = { ...cell, fontWeight: 800, color: '#12336e', background: '#f2f7ff' };
  return (
    <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: 10 }}>
      <thead>
        <tr>
          <th style={{ ...head, textAlign: 'left' }}>Trạng thái</th>
          <th style={head}>Trước</th>
          <th style={head}>Trong (kết nối khác)</th>
          <th style={head}>Sau</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.label}>
            <td style={{ ...cell, textAlign: 'left', color: '#41506a' }}>{row.label}</td>
            <td style={{ ...cell, textAlign: 'center' }}>{row.get(r.before)}</td>
            <td style={{ ...cell, textAlign: 'center', color: '#0a8a4f', fontWeight: 700 }}>{row.get(r.during)}</td>
            <td style={{ ...cell, textAlign: 'center', fontWeight: row.flag ? 800 : 400 }}>{row.get(r.after)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export default function CrashLab({ accounts, defaultFromId, defaultToId }: {
  accounts: Account[];
  defaultFromId: string;
  defaultToId: string;
}) {
  const [fromId, setFromId] = useState(defaultFromId);
  const [toId, setToId] = useState(defaultToId);
  const [amountStr, setAmountStr] = useState('100');
  const [running, setRunning] = useState<CrashScenario | null>(null);
  const [result, setResult] = useState<CrashResult | null>(null);
  const [error, setError] = useState('');

  const amount = Number(amountStr || 0);

  const run = async (scenario: CrashScenario) => {
    setError('');
    if (!fromId || !toId) { setError('Chưa chọn tài khoản.'); return; }
    if (fromId === toId) { setError('Nguồn và đích phải khác nhau.'); return; }
    if (amount <= 0) { setError('Số tiền phải > 0.'); return; }
    setRunning(scenario);
    setResult(null);
    try {
      const { data } = await simulateCrash(fromId, toId, amount, scenario);
      setResult(data);
    } catch (e: any) {
      setError(e?.response?.data?.error ?? e?.message ?? 'Mô phỏng thất bại.');
    } finally {
      setRunning(null);
    }
  };

  const box: React.CSSProperties = {
    background: '#fff', border: '1px solid #dbe4f0', borderRadius: 14,
    padding: 16, marginTop: 14, boxShadow: '0 6px 18px rgba(30,60,110,.06)',
  };
  const sel: React.CSSProperties = {
    flex: 1, padding: '8px 10px', borderRadius: 8, border: '1px solid #cdd8ea', fontSize: 13,
  };

  return (
    <div style={box}>
      <div style={{ fontWeight: 800, color: '#12336e', fontSize: 15 }}>🧪 Lab: Crash giữa Transaction <span style={{ fontSize: 11, color: '#c0392b', fontWeight: 700 }}>· DB THẬT</span></div>
      <p style={{ fontSize: 12.5, color: '#5c6b82', margin: '6px 0 12px', lineHeight: 1.5 }}>
        Cố tình mở transaction thật, trừ/cộng tiền + INSERT key, rồi mô phỏng crash <b>trước COMMIT</b>.
        Chứng minh bằng số liệu: chưa COMMIT ⇒ DB tự rollback, không gì được ghi.
      </p>

      <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
        <select style={sel} value={fromId} onChange={(e) => setFromId(e.target.value)}>
          {accounts.map((a) => <option key={a.id} value={a.id}>Nguồn: {a.name} — {fmt(a.balance)}</option>)}
        </select>
        <select style={sel} value={toId} onChange={(e) => setToId(e.target.value)}>
          {accounts.map((a) => <option key={a.id} value={a.id}>Đích: {a.name} — {fmt(a.balance)}</option>)}
        </select>
        <input
          style={{ ...sel, flex: '0 0 110px' }} inputMode="numeric" placeholder="Số tiền"
          value={amount ? fmt(amount) : ''}
          onChange={(e) => setAmountStr(e.target.value.replace(/\D/g, ''))}
        />
      </div>

      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {SCENARIOS.map((s) => (
          <button
            key={s.key}
            title={s.hint}
            disabled={running !== null}
            onClick={() => run(s.key)}
            style={{
              flex: 1, minWidth: 130, padding: '10px 8px', borderRadius: 9, border: 'none', cursor: 'pointer',
              fontWeight: 700, fontSize: 12.5, color: '#fff',
              background: s.danger ? '#e11d48' : '#059669',
              opacity: running !== null && running !== s.key ? 0.5 : 1,
            }}
          >
            {running === s.key ? 'Đang chạy…' : s.label}
          </button>
        ))}
      </div>

      {error && <div style={{ marginTop: 12, color: '#b3122f', fontSize: 12.5, background: '#fdeef0', borderRadius: 8, padding: '8px 10px' }}>{error}</div>}

      {result && (
        <div style={{ marginTop: 14 }}>
          <div style={{
            fontWeight: 800, fontSize: 13.5, padding: '8px 12px', borderRadius: 9,
            color: '#fff', background: result.committed ? '#0e7a3e' : '#b45309',
          }}>
            {result.scenarioTitle}
          </div>

          <SnapshotTable r={result} />

          <ol style={{ margin: '12px 0 0', paddingLeft: 0, listStyle: 'none' }}>
            {result.steps.map((st, i) => (
              <li key={i} style={{ display: 'flex', gap: 8, padding: '5px 0', borderBottom: '1px dashed #eef2f7', fontSize: 12.3 }}>
                <span style={{ color: st.ok ? '#0a8a4f' : '#c0392b', fontWeight: 800 }}>{st.ok ? '✓' : '✗'}</span>
                <span><b style={{ color: '#12336e' }}>{st.label}</b> — <span style={{ color: '#5c6b82' }}>{st.detail}</span></span>
              </li>
            ))}
          </ol>

          <div style={{
            marginTop: 12, fontSize: 12.8, lineHeight: 1.5, padding: '10px 12px', borderRadius: 9,
            background: '#e7f8f0', border: '1px solid #059669', color: '#0e5a37', fontWeight: 600,
          }}>
            {result.verdict}
          </div>

          <div style={{ marginTop: 8, fontSize: 11, color: '#8091ad', wordBreak: 'break-all' }}>
            🔑 Idempotency-Key mô phỏng: {result.idempotencyKey}
          </div>
        </div>
      )}
    </div>
  );
}
