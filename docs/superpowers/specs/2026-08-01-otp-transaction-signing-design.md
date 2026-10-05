# Spec — OTP Transaction Signing (2FA) — Protections P23 & P24

- **Date:** 2026-08-01
- **Status:** Design (approved to document; not yet implemented).
- **Adds protection rules:** `P23` (OTP second factor) and `P24` (OTP challenge binding / single‑use / expiry / lockout) to `docs/PROTECTIONS.md`.
- **Goal:** turn the current *client‑side fake OTP* (`Dashboard.tsx` accepts `123456`) into a **real server‑verified second factor** that gates money movement, without breaking the existing idempotency/ledger protections.

---

## 1. Options considered & chosen

### QĐ‑OTP‑1. Where is the OTP verified?
- **(A) Server‑side, two endpoints: `initiate` (issue) + `confirm` (verify then execute) — ✅ CHOSEN.** Money moves only inside `confirm`, after the code is verified server‑side.
- (B) Client‑side check — ❌ current stub; trivially bypassed. Rejected.

### QĐ‑OTP‑2. Binding (anti‑replay)
- **(A) Bind the OTP to `(Idempotency‑Key, FromAccountId, ToAccountId, Amount)` — ✅ CHOSEN.** A code issued for "100 to Bob" cannot confirm "1000 to Mallory". Single‑use + short TTL + attempt lockout.
- (B) Bind only to user/session — weaker; a stolen code could sign a different transfer. Rejected.

### QĐ‑OTP‑3. Idempotency‑Key lifecycle
- **CHOSEN:** the key is generated once at the **Confirm** step (already true in UI, P1), sent to `initiate`, stored on the `OtpChallenge`, and **reused** by `confirm`. So the OTP, the challenge, and the eventual `TransferMoneyCommand` all share one key → retries are safe end‑to‑end.

### QĐ‑OTP‑4. Delivery
- **CHOSEN (learning):** dev `IOtpSender` logs / returns the code (no SMS gateway). Interface lets a real SMS/push provider drop in later. Code is 6 digits.

### QĐ‑OTP‑5. Code at rest
- **CHOSEN:** store only a **PBKDF2 hash** of the code (reuse `Pbkdf2PasswordHasher`, P21). Never store the plaintext OTP.

---

## 2. Data model (planned)

`src/Banking.Domain/Otp/OtpChallenge.cs`
```
class OtpChallenge : BaseEntity {
    string  IdempotencyKey     // binds challenge ↔ transfer (P1/P24)
    Guid    FromAccountId
    Guid    ToAccountId
    decimal Amount             // bound params (P24)
    string  CodeHash           // PBKDF2 (P21) — never plaintext (P24)
    DateTime ExpiresAt         // short TTL, e.g. 2 min (P24)
    int      Attempts          // wrong‑entry counter (P24)
    bool     Consumed          // single‑use (P24)
    DateTime CreatedAt
}
```
`BankingDbContext`: add `DbSet<OtpChallenge>`, index on `IdempotencyKey` (unique).

## 3. Backend (planned)

- `IOtpSender` (Application) + `DevOtpSender` (Infrastructure) — "sends" the code.
- `InitiateTransferCommand(IdempotencyKey, From, To, Amount)` → validates account exists (P20 upstream), **soft** funds check (P6), generates 6‑digit code, stores hashed `OtpChallenge` bound to params (P23/P24), sends it. **Moves no money.** Returns `{ challengeSent: true }` (+ dev code).
- `ConfirmTransferCommand(IdempotencyKey, Otp)`:
  1. Load challenge by `IdempotencyKey`; **verify** not `Consumed`, not expired, `Attempts < max`, `CodeHash` matches (P24). On failure: `Attempts++`, save, return `401/422`.
  2. Mark `Consumed = true` (single‑use, P24).
  3. Execute existing `TransferMoneyCommand` with the **same** `IdempotencyKey` → triggers P1/P2/P3/P5/P6/P8.
  4. Same commit stamps the idempotency record (P2).
- `TransfersController`: `POST /api/v1/transfers/initiate`, `POST /api/v1/transfers/confirm` (both `[Authorize]`, P20).

## 4. UI (planned)

`frontend/src/pages/Dashboard.tsx`:
- **Confirm step** button → calls `initiateTransfer(key, from, to, amount)` (was: just move to OTP screen). Key already made in `goConfirm` (P1).
- **OTP step** → real 6‑box entry (already built) now calls `confirmTransfer(key, otp)` instead of comparing to `123456`. Surface expiry countdown + remaining attempts (P24).
- **Result step** unchanged (P18).

`frontend/src/api.ts`: add `initiateTransfer` and `confirmTransfer`.

---

## 5. Workflow — "Confirm Deposit" → OTP → confirm again
(Which protection rule fires at each step. Money moves ONLY at step 6.)

| # | Action | Where | Protections triggered |
|---|--------|-------|-----------------------|
| 0 | User logged in; every call carries JWT | `api.ts` interceptor | **P20** (JWT), **P21** (hash at login), **P22** (401→logout) |
| 1 | Fill from/to/amount, tap **Tiếp tục** | `Dashboard.tsx` input | client validation + soft **P6** |
| 2 | Confirm screen shown; client generates **one** Idempotency‑Key | `Dashboard.tsx goConfirm` | **P1** (key created & bound) |
| 3 | Tap **Xác nhận / Confirm Deposit** → `POST /transfers/initiate` | `TransfersController.initiate` → `InitiateTransferCommand` | **P4** (key required), **P6** (soft funds check), **P23** (issue OTP), **P24** (challenge bound to key+from+to+amount, hashed **P21**, TTL, Attempts=0, Consumed=false). **No money moves.** |
| 4 | OTP delivered out‑of‑band (dev: shown/logged) | `DevOtpSender` | **P23** delivery |
| 5 | User types 6‑digit code, taps confirm → `POST /transfers/confirm` | `TransfersController.confirm` → `ConfirmTransferCommand` | **P20** (JWT), **P24** verify: match hash / not expired / `Attempts<max` / not `Consumed`; wrong → `Attempts++` + reject; right → mark `Consumed` (single‑use) |
| 6 | On valid OTP, same handler runs `TransferMoneyCommand` (same key) | `ConfirmTransferCommand` → `TransferMoneyCommandHandler` | **P1** (INSERT‑first UNIQUE), **P2** (one atomic commit), **P3** (cache if confirm retried), **P5** (balance race + retry), **P6** (final funds check), **P7** (decimal), **P8/P9** (double‑entry ledger + BalanceAfter). **Money moves here.** |
| 7 | Result screen shows status + txn id | `Dashboard.tsx` result / `GET /transfers/{id}` | **P18** (status inquiry) |
| 7b | (Interbank only) worker settles / reconcile | `ProcessOutbox`, `AdminController.reconcile` | **P10–P17** (suspense, saga, outbox, reconcile, state machine) |

### Why the ordering matters
- OTP is verified (P24) **before** the money‑moving command (step 6), so a stolen JWT alone can't move funds (P23).
- The **same Idempotency‑Key** spans initiate → confirm → transfer, so a double‑tap on "confirm" is absorbed by P1/P3 (charged once), and the OTP itself is single‑use (P24) — two independent anti‑replay layers.
- Money moves in **one commit** at step 6 (P2), guarded by concurrency (P5) and ledger (P8) — the OTP layer adds authorization without weakening any existing guarantee.

---

## 6. Out of scope
Real SMS/push provider, rate‑limiting per user/IP, OTP resend throttling, account↔user ownership (separate gap), remembering trusted devices.
