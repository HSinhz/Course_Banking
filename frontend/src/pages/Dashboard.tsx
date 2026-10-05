import { useEffect, useRef, useState } from 'react';
import { getAccounts, transfer, type Account, type TransferResult } from '../api';
import CrashLab from '../components/CrashLab';
import type { Theme } from '../App';

type Step = 'input' | 'confirm' | 'otp' | 'result';
const DEMO_OTP = '123456';

const CATEGORIES = [
  { icon: '🧾', label: 'Hóa đơn' },
  { icon: '🍱', label: 'Thực phẩm & ăn uống' },
  { icon: '🚕', label: 'Du lịch' },
  { icon: '🛍️', label: 'Mua sắm' },
  { icon: '📦', label: 'Khác' },
];

const fmt = (n: number) => n.toLocaleString('en-US');
const maskStk = (id: string) => '•••• ' + id.replace(/-/g, '').slice(-4).toUpperCase();

export default function Dashboard({ displayName, onLogout, theme, onToggleTheme }: {
  displayName: string;
  onLogout: () => void;
  theme: Theme;
  onToggleTheme: () => void;
}) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [step, setStep] = useState<Step>('input');

  const [fromId, setFromId] = useState('');
  const [toId, setToId] = useState('');
  const [amountStr, setAmountStr] = useState('');
  const [content, setContent] = useState(`${(displayName || '').toUpperCase()} chuyen tien`);
  const [category, setCategory] = useState(0);
  const [hideBalance, setHideBalance] = useState(false);

  const [idemKey, setIdemKey] = useState('');
  const [otp, setOtp] = useState(['', '', '', '', '', '']);
  const otpRefs = useRef<(HTMLInputElement | null)[]>([]);

  const [result, setResult] = useState<TransferResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [log, setLog] = useState<string[]>([]);
  const [showLab, setShowLab] = useState(false);

  const refresh = async () => {
    const { data } = await getAccounts();
    setAccounts(data);
    setFromId((f) => f || data[0]?.id || '');
    setToId((t) => t || data[1]?.id || '');
  };

  useEffect(() => { refresh(); /* eslint-disable-next-line */ }, []);

  const fromAcc = accounts.find((a) => a.id === fromId);
  const toAcc = accounts.find((a) => a.id === toId);
  const amount = Number(amountStr || 0);

  const changeFrom = (id: string) => {
    setFromId(id);
    if (id === toId) setToId(accounts.find((a) => a.id !== id)?.id ?? '');
  };
  const changeTo = (id: string) => {
    setToId(id);
    if (id === fromId) setFromId(accounts.find((a) => a.id !== id)?.id ?? '');
  };

  // ---------- Bước 1: Nhập -> Xác nhận ----------
  const goConfirm = () => {
    setError('');
    if (!fromAcc || !toAcc) return setError('Chưa chọn tài khoản.');
    if (fromId === toId) return setError('Nguồn tiền và người nhận phải khác nhau.');
    if (amount <= 0) return setError('Số tiền phải lớn hơn 0.');
    if (amount > fromAcc.balance) return setError('Số dư không đủ.');
    // Idempotency-Key sinh Ở ĐÂY: giữ nguyên qua OTP + mọi lần retry -> chống trừ 2 lần.
    setIdemKey(crypto.randomUUID());
    setStep('confirm');
  };

  // ---------- Bước 2: Xác nhận -> OTP ----------
  const goOtp = () => {
    setOtp(['', '', '', '', '', '']);
    setError('');
    setStep('otp');
    setTimeout(() => otpRefs.current[0]?.focus(), 50);
  };

  const onOtpChange = (i: number, v: string) => {
    const d = v.replace(/\D/g, '').slice(-1);
    const next = [...otp];
    next[i] = d;
    setOtp(next);
    if (d && i < 5) otpRefs.current[i + 1]?.focus();
  };
  const onOtpKey = (i: number, e: React.KeyboardEvent) => {
    if (e.key === 'Backspace' && !otp[i] && i > 0) otpRefs.current[i - 1]?.focus();
  };

  // ---------- Bước 3: OTP -> gọi API thật ----------
  // Trả về true nếu giao dịch gọi thành công (dùng để quyết định có sang màn Kết quả hay không).
  // KHÔNG tự chuyển trang ở đây → nút TEST giữ nguyên màn OTP để bấm 2 lần cùng key.
  const doPay = async (): Promise<boolean> => {
    if (otp.join('') !== DEMO_OTP) { setError('Mã OTP không đúng (demo: 123456).'); return false; }
    setError('');
    try {
      const { data } = await transfer(idemKey, fromId, toId, amount);
      setResult(data);
      const tag = data?.servedFromCache ? '(cache — không trừ lại)' : '(thực thi)';
      setLog((l) => [`Click OTP: ${data?.status ?? '?'} ${tag}`, ...l].slice(0, 6));
      await refresh();
      return true;
    } catch (e: any) {
      setError(e?.response?.data?.error ?? e?.message ?? 'Giao dịch thất bại.');
      return false;
    }
  };

  // ĐÚNG WORKFLOW: xác nhận OTP → chuyển tiền → sang màn Kết quả (khoá nút khi đang xử lý).
  const doPayAndFinish = async () => {
    setBusy(true);
    try {
      const ok = await doPay();
      if (ok) setStep('result');
    } finally {
      setBusy(false);
    }
  };

  // ---------- Demo chống trùng: 2 request SONG SONG cùng 1 key ----------
  const doubleClick = async () => {
    setBusy(true);
    try {
      const key = crypto.randomUUID();
      const amt = Math.min(amount || 10000, fromAcc?.balance ?? 0);
      const [r1, r2] = await Promise.allSettled([
        transfer(key, fromId, toId, amt),
        transfer(key, fromId, toId, amt),
      ]);
      const desc = (r: PromiseSettledResult<any>) =>
        r.status === 'fulfilled'
          ? `${r.value.data.status}${r.value.data.servedFromCache ? ' (cache)' : ' (thực thi)'}`
          : `bị chặn (${r.reason?.response?.status ?? 'err'})`;
      setLog((l) => [`Double-click cùng key: A=${desc(r1)}, B=${desc(r2)} → chỉ trừ 1 lần`, ...l].slice(0, 6));
      await refresh();
    } finally {
      setBusy(false);
    }
  };

  const reset = () => {
    setStep('input');
    setAmountStr('');
    setOtp(['', '', '', '', '', '']);
    setResult(null);
    setError('');
    setLog([]);
    refresh();
  };

  const back = () => {
    setError('');
    if (step === 'confirm') setStep('input');
    else if (step === 'otp') setStep('confirm');
    else onLogout();
  };

  const titleByStep: Record<Step, string> = {
    input: 'Chuyển tiền trong nước',
    confirm: 'Xác nhận',
    otp: 'Xác thực',
    result: 'Kết quả',
  };

  return (
    <div className="app">
      <div className="topbar">
        <button className="icon-btn" onClick={back} title="Quay lại">←</button>
        <div className="title">{titleByStep[step]}</div>
        <button className="icon-btn" onClick={() => setShowLab((v) => !v)} title="Lab crash transaction" style={showLab ? { color: '#e11d48' } : undefined}>🧪</button>
        <button className="icon-btn" onClick={onToggleTheme} title="Sáng/tối">{theme === 'light' ? '🌙' : '☀️'}</button>
        <button className="icon-btn" onClick={onLogout} title="Đăng xuất">⏻</button>
      </div>

      <div className="flow">
        {showLab && (
          <CrashLab
            accounts={accounts}
            defaultFromId={fromId || accounts[0]?.id || ''}
            defaultToId={toId || accounts[1]?.id || ''}
          />
        )}
        {step === 'input' && fromAcc && toAcc && (
          <>
            {/* Nguồn tiền */}
            <div className="src-card">
              <span className="pill">
                Nguồn tiền
                <span className="eye" onClick={() => setHideBalance((h) => !h)}>{hideBalance ? '🙈' : '👁'}</span>
              </span>
              <div className="src-row">
                <div className="avatar" />
                <div className="src-main">
                  <div className="balance">{hideBalance ? '•••••• VND' : `${fmt(fromAcc.balance)} VND`}</div>
                  <div className="masked">{fromAcc.name} • {maskStk(fromAcc.id)}</div>
                </div>
                <span className="chev">▾</span>
              </div>
              <select className="card-select" value={fromId} onChange={(e) => changeFrom(e.target.value)}>
                {accounts.map((a) => <option key={a.id} value={a.id}>{a.name} — {fmt(a.balance)} VND</option>)}
              </select>
            </div>

            {/* Đến người nhận */}
            <div className="section-title">Đến người nhận</div>
            <div className="recipient-card">
              <div className="bank-logo">🏦</div>
              <div className="recip-main">
                <div className="recip-name">{toAcc.name.toUpperCase()}</div>
                <div className="recip-sub">{maskStk(toAcc.id)} • Banking Basic</div>
              </div>
              <span className="chev">▾</span>
              <select className="card-select" value={toId} onChange={(e) => changeTo(e.target.value)}>
                {accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
              </select>
            </div>

            {/* Thông tin chuyển khoản */}
            <div className="section-title">Thông tin chuyển khoản <span className="link">Hạn mức</span></div>
            <div className="field">
              <div className="flabel">Số tiền</div>
              <div className="field-row">
                <input
                  className="field-input" inputMode="numeric" placeholder="0"
                  value={amount ? fmt(amount) : ''}
                  onChange={(e) => setAmountStr(e.target.value.replace(/\D/g, ''))}
                />
                <span className="vnd">VND</span>
              </div>
            </div>
            <div className="field">
              <div className="flabel">Nội dung</div>
              <div className="field-row">
                <input className="content-input" value={content} onChange={(e) => setContent(e.target.value)} />
              </div>
            </div>

            {/* Phân loại giao dịch */}
            <div className="section-title" style={{ color: 'var(--muted)', fontWeight: 600 }}>Phân loại giao dịch</div>
            <div className="chips">
              {CATEGORIES.map((c, i) => (
                <div key={c.label} className={`chip ${i === category ? 'active' : ''}`} onClick={() => setCategory(i)}>
                  <span>{c.icon}</span>{c.label}
                </div>
              ))}
            </div>

            {/* napas banner */}
            <div className="banner">
              Đây là dịch vụ chuyển tiền nhanh
              <span className="napas"><b>napas</b><i>247</i></span>
              <span className="qmark">?</span>
            </div>

            {error && <div className="error" style={{ marginTop: 14 }}>{error}</div>}
          </>
        )}

        {step === 'confirm' && fromAcc && toAcc && (
          <>
            <div className="review">
              <div className="review-head">
                <div className="bank-logo">🏦</div>
                <div className="recip-main">
                  <div className="recip-name">{toAcc.name.toUpperCase()}</div>
                  <div className="recip-sub">{maskStk(toAcc.id)} • Banking Basic</div>
                </div>
              </div>
              <div className="rrow"><span className="k">Số tiền</span><span className="v amount">{fmt(amount)} VND</span></div>
              <div className="rrow"><span className="k">Phí giao dịch</span><span className="v">Miễn phí</span></div>
              <div className="rrow"><span className="k">Nội dung</span><span className="v">{content}</span></div>
              <div className="rrow"><span className="k">Nguồn tiền</span><span className="v">{fromAcc.name} • {maskStk(fromAcc.id)}</span></div>
              <div className="rrow"><span className="k">Phân loại</span><span className="v">{CATEGORIES[category].icon} {CATEGORIES[category].label}</span></div>
            </div>
            <div className="keybox">
              🔑 <b>Idempotency-Key</b> (khoá chống chuyển 2 lần, giữ nguyên qua OTP &amp; mọi lần thử lại):<br />{idemKey}
            </div>
            {error && <div className="error" style={{ marginTop: 12 }}>{error}</div>}
          </>
        )}

        {step === 'otp' && (
          <div className="otp-wrap">
            <p className="muted">Nhập mã OTP đã gửi tới số điện thoại của bạn</p>
            <p style={{ fontSize: 13, color: 'var(--accent)', fontWeight: 700 }}>Mã demo: {DEMO_OTP}</p>
            <div className="otp-boxes">
              {otp.map((d, i) => (
                <input
                  key={i}
                  ref={(el) => { otpRefs.current[i] = el; }}
                  className="otp-box" inputMode="numeric" maxLength={1} value={d}
                  onChange={(e) => onOtpChange(i, e.target.value)}
                  onKeyDown={(e) => onOtpKey(i, e)}
                />
              ))}
            </div>
            <p className="muted">Chuyển {fmt(amount)} VND tới {toAcc?.name.toUpperCase()}</p>
            {error && <div className="error">{error}</div>}
          </div>
        )}

        {step === 'result' && result && (
          <div className="result">
            <div className="check">✓</div>
            <h2>Chuyển tiền thành công!</h2>
            <div className="big-amt">-{fmt(amount)} VND</div>
            <p className="muted">Tới {toAcc?.name.toUpperCase()} • {content}</p>
            <div className="review" style={{ textAlign: 'left', marginTop: 18 }}>
              <div className="rrow"><span className="k">Mã giao dịch</span><span className="v">{result.transferId.slice(0, 8).toUpperCase()}</span></div>
              <div className="rrow"><span className="k">Trạng thái</span><span className="v">{result.status}{result.servedFromCache ? ' (cache)' : ''}</span></div>
              <div className="rrow"><span className="k">Số dư sau GD</span><span className="v">{fmt(result.fromBalanceAfter)} VND</span></div>
            </div>
            {log.length > 0 && <ul className="log">{log.map((l, i) => <li key={i}>{l}</li>)}</ul>}
          </div>
        )}
      </div>

      {/* Nút hành động dưới cùng theo từng bước */}
      <div className="bottom">
        {step === 'input' && <button className="btn-primary" onClick={goConfirm}>Tiếp tục</button>}
        {step === 'confirm' && <button className="btn-primary" disabled={busy} onClick={goOtp}>Xác nhận</button>}
        {step === 'otp' && (
          <>
            {/* ĐÚNG WORKFLOW: chuyển tiền xong sang màn Kết quả */}
            <button className="btn-primary" disabled={busy || otp.join('').length < 6} onClick={doPayAndFinish}>
              {busy ? 'Đang xử lý…' : 'Xác nhận chuyển tiền'}
            </button>
            {/* TEST: ở lại màn OTP để bấm nhiều lần cùng 1 key (kiểm chứng idempotency) */}
            <button className="btn-warn" disabled={otp.join('').length < 6} onClick={doPay}>
              🔁 Test bấm nhiều lần (giữ màn OTP)
            </button>
          </>
        )}
        {step === 'result' && (
          <>
            <button className="btn-warn" disabled={busy} onClick={doubleClick}>
              Mô phỏng double-click (2 request cùng key)
            </button>
            <button className="btn-ghost" onClick={reset}>Chuyển tiền mới</button>
          </>
        )}
      </div>
    </div>
  );
}
