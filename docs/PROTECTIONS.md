# Protection Rules Registry — Banking Basic

Canonical list of every safeguard in the system. Each rule has a stable ID (`P#`),
what it defends against, and **where it lives in code / UI**.
Status: ✅ implemented · 🟡 planned (design approved, not yet coded).

> IDs are referenced by the OTP transaction‑signing workflow in
> `docs/superpowers/specs/2026-08-01-otp-transaction-signing-design.md`.

---

## A. Anti double‑charge (idempotency)

### P1 — Idempotency‑Key + UNIQUE constraint (INSERT‑first) ✅
Same logical transfer sent twice → charged once. DB `PRIMARY KEY` on the key is the arbiter (no check‑then‑act).
- Code: `src/Banking.Domain/Idempotency/IdempotencyRecord.cs` (Key = PK), `src/Banking.Infrastructure/Persistence/BankingDbContext.cs` (`HasKey(x => x.Key)`), `src/Banking.Application/Transfers/Commands/TransferMoney/TransferMoneyCommandHandler.cs` (INSERT‑first + catch UNIQUE).
- UI: key generated at the Confirm step — `frontend/src/pages/Dashboard.tsx` (`goConfirm` → `crypto.randomUUID()`), sent via header in `frontend/src/api.ts`.

### P2 — Single‑transaction claim+execute+complete ✅
Claim key + move money + stamp Completed in one atomic commit → the "stuck `InProgress`" bug can't occur.
- Code: `TransferMoneyCommandHandler.cs` (one `SaveChangesAsync`).

### P3 — Fast‑path cache replay ✅
Retry of a completed key returns the exact stored response (`ServedFromCache=true`), never re‑charges.
- Code: `TransferMoneyCommandHandler.cs` (read `Completed` record → return cached).

### P4 — Reject missing Idempotency‑Key ✅
No key → `400`. Server does not trust the client.
- Code: `TransferMoneyCommandHandler.cs` (`MissingIdempotencyKeyException`), mapped in `src/Banking.API/Controllers/TransfersController.cs`.

## B. Money integrity & concurrency

### P5 — Optimistic concurrency token (`Account.Version`) + bounded retry ✅
Defends lost‑update between *different‑key* transfers on the same account. `WHERE Version=@old`; stale → retry; exhausted → `409`.
- Code: `src/Banking.Domain/Accounts/Account.cs` (`Version`), `BankingDbContext.cs` (`IsConcurrencyToken()`), `TransferMoneyCommandHandler.cs` (retry loop).

### P6 — Balance check before debit ✅
No overdraw. Thrown before any write.
- Code: `TransferMoneyCommandHandler.cs` (`InsufficientFundsException` → `422`).
- UI: soft pre‑check in `Dashboard.tsx` (`goConfirm`).

### P7 — `decimal` money type ✅
Exact money math (never float).
- Code: all `Amount`/`Balance` are `decimal`; `BankingDbContext.cs` (`decimal(18,2)`).

## C. Accounting integrity (audit)

### P8 — Double‑entry ledger (immutable / append‑only) ✅
Every movement = Debit + Credit pair; conservation (signed sum = 0).
- Code: `src/Banking.Domain/Ledger/LedgerEntry.cs`, written in `TransferMoneyCommandHandler.cs` and `src/Banking.Application/Transfers/Common/InterbankSettlement.cs`.

### P9 — `BalanceAfter` audit column ✅
Each posting records balance right after it.
- Code: `LedgerEntry.cs`.

## D. Surviving failures mid‑transaction (interbank)

### P10 — Suspense account ✅
In‑flight money held in a real account; always countable.
- Code: `src/Banking.Domain/Accounts/WellKnownAccounts.cs` (`SuspenseId`), seeded in `src/Banking.API/Program.cs`.

### P11 — EXTERNAL_OUT counter‑account ✅
Keeps double‑entry balanced when money leaves the system.
- Code: `WellKnownAccounts.cs` (`ExternalOutId`), `InterbankSettlement.cs`.

### P12 — Saga + compensating transaction (reversal) ✅
External reject → automatic refund to source. Converges to fully‑sent or fully‑refunded.
- Code: `InterbankSettlement.cs`, `src/Banking.Application/Transfers/Commands/ProcessOutbox/ProcessOutboxCommand.cs`.

### P13 — Transactional Outbox ✅
Intent written in the same transaction as the debit; worker re‑runs after crash.
- Code: `src/Banking.Domain/Outbox/OutboxMessage.cs`, `src/Banking.API/OutboxProcessorHostedService.cs`.

### P14 — Idempotent outbox processing ✅
Re‑running never settles twice (guarded by `OutboxStatus` + `Transfer.Status == Pending`).
- Code: `ProcessOutboxCommand.cs`, `InterbankSettlement.cs`.

### P15 — "Don't guess" on timeout/Unknown ✅
Unknown external result → stays `Pending`; never assumes.
- Code: `InterbankSettlement.cs` (returns false on `Unknown`).

### P16 — Reconciliation (safety net) ✅
Re‑queries stuck `Pending` transfers and resolves them.
- Code: `src/Banking.Application/Transfers/Commands/Reconcile/ReconcileCommand.cs`, `src/Banking.API/Controllers/AdminController.cs`.

### P17 — Explicit state machine ✅
`Pending → Completed / Reversed`; no ambiguous boolean.
- Code: `src/Banking.Domain/Transfers/Transfer.cs` (`TransferStatus`).

### P18 — Status inquiry ✅
Observe true state anytime.
- Code: `src/Banking.Application/Transfers/Queries/GetTransferStatusQuery.cs`, `TransfersController.cs` (`GET {id}`).

### P19 — Hide system accounts from customer API ✅
`SUSPENSE`/`EXTERNAL_OUT` filtered from `/accounts`.
- Code: `src/Banking.Application/Accounts/Queries/GetAccountsQuery.cs`.

## E. Access control

### P20 — JWT auth (`[Authorize]`) ✅
- Code: `TransfersController.cs`, `AccountsController.cs`, `Program.cs` (JWT bearer).

### P21 — PBKDF2 password hashing ✅
- Code: `src/Banking.Infrastructure/Auth/Pbkdf2PasswordHasher.cs`.

### P22 — 401 → auto‑logout ✅
- UI: `frontend/src/api.ts` (response interceptor).

## F. Transaction signing (2FA) — NEW

### P23 — OTP second factor on money movement 🟡
A confirmed transfer requires a one‑time code delivered out‑of‑band before money moves. Defends against
session hijack / accidental confirms / unauthorized transfers even with a valid JWT.
- Code (planned): `src/Banking.Domain/Otp/OtpChallenge.cs`; `src/Banking.Application/Transfers/Commands/InitiateTransfer/*` (issue OTP) and `.../ConfirmTransfer/*` (verify OTP then run `TransferMoneyCommand`); `IOtpSender` in Application + dev sender in Infrastructure; endpoints in `TransfersController.cs` (`POST initiate`, `POST confirm`).
- UI: `frontend/src/pages/Dashboard.tsx` — Confirm step calls `initiate`; OTP step calls `confirm` (replaces the current client‑side `123456` stub); `frontend/src/api.ts` adds `initiateTransfer` / `confirmTransfer`.

### P24 — OTP challenge binding, single‑use, expiry, attempt lockout 🟡
Anti‑replay for the OTP itself: the code is bound to `(Idempotency‑Key, from, to, amount)`, hashed at rest,
valid for a short TTL, usable once, and locked after N wrong attempts.
- Code (planned): `OtpChallenge.cs` (CodeHash via P21 hasher, `ExpiresAt`, `Attempts`, `Consumed`, bound params); verification in `ConfirmTransfer` handler.
- UI: attempts/expiry surfaced on the OTP step in `Dashboard.tsx`.

---

## Known gaps (documented, not yet closed)
- **Authorization scope:** `[Authorize]` proves "logged in" but accounts aren't tied to a user → does not yet enforce "move only your own money."
- **Outbox lease/TTL:** Phase‑1 stuck‑record takeover is unconditional (fine for single‑transaction Phase 1; Phase 2 async would want a lease).
