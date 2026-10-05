# BankingBasic — Bản tổng hợp bàn giao (handoff sang session khác)

> Copy toàn bộ file này dán vào session mới để làm tiếp. Dự án **tự học**, KHÔNG thuộc git HCM.
> Vị trí: `D:\course_my_self\BankingBasic`

---

## 1. Mục tiêu dự án
Mô phỏng **Clean Architecture của HCM** để luyện tập chống **"chuyển tiền 2 lần"** (idempotency) khi khách chỉ bấm 1 lần. Chỉ test các case idempotency đã thảo luận, cộng thêm auth + frontend đăng nhập được.

## 2. Trạng thái hiện tại — ĐÃ HOÀN THÀNH & CHẠY ĐƯỢC
- Backend .NET 10 + Frontend React 19 chạy end-to-end, login được, chống trùng hoạt động.
- 7/7 test xUnit PASS.
- DB đã chuyển từ SQLite sang **PostgreSQL** để xem trong pgAdmin4.

## 3. Kiến trúc
```
src/
  Banking.Domain          # Entity thuần: Account, Transfer, IdempotencyRecord, User (0 dependency)
  Banking.Application     # CQRS/MediatR: TransferMoneyCommand+Handler, LoginCommand, GetAccountsQuery,
                          #   IBankingDbContext, IAuthServices, Exceptions
  Banking.Infrastructure  # EF Core: BankingDbContext (UseNpgsql), Pbkdf2PasswordHasher, JwtTokenService, DI
  Banking.API             # ASP.NET host + AuthController/AccountsController/TransfersController
tests/
  Banking.Application.Tests  # xUnit — vẫn dùng SQLite in-memory (độc lập với Postgres)
frontend/                 # React 19 + Vite 6 + TS + axios
```
Chiều phụ thuộc: `API → Infrastructure → Application → Domain`.
Solution: **`Banking.slnx`** (.NET 10 tạo định dạng .slnx, KHÔNG phải .sln) → build bằng `dotnet build Banking.slnx`.

## 4. TRÁI TIM — cơ chế chống trùng (INSERT-first)
File: `src/Banking.Application/Transfers/Commands/TransferMoney/TransferMoneyCommandHandler.cs`
1. Client gửi header `Idempotency-Key` (UUID) — cùng 1 key cho mọi lần retry.
2. Handler **INSERT thẳng** key vào `IdempotencyRecords` (Key = PK → UNIQUE). KHÔNG "SELECT rồi INSERT" (check-then-act có khe hở race).
3. INSERT OK → chuyển tiền + lưu response + set `Completed` (một `SaveChanges` = 1 transaction nguyên tử).
4. INSERT vi phạm UNIQUE (request lặp) →
   - `ChangeTracker.Clear()`, query lại `AsNoTracking` theo key;
   - key `Completed` → trả **response cũ** (`ServedFromCache=true`, không trừ lần 2);
   - key `InProgress` → **409 ConcurrentRequestException**.
5. `IsUniqueViolation` bắt CẢ hai: `"UNIQUE constraint failed"` (SQLite) VÀ `"duplicate key"` (Postgres) → **cùng 1 handler chạy trên cả 2 DB**.

⚠️ Lưu ý khi đọc log: EF Core **ghi stack trace lỗi `23505 duplicate key` ở mức Error TRƯỚC KHI** handler catch → nhìn đáng sợ nhưng **vô hại**, request lặp vẫn trả cache thành công. Đây chính là cơ chế đang chạy đúng.

## 5. Cấu hình runtime (PostgreSQL)
- Server: `127.0.0.1:55432` (PG18 nghe cổng **55432**, không phải 5432 — confirm trong postgresql.conf).
- Credentials: **postgres / admin**.
- DB: `banking_basic` (public schema). Bảng EF đặt tên PascalCase → trong pgAdmin4 phải dùng **quoted identifier** (`"Users"`, `"Accounts"`, `"Transfers"`, `"IdempotencyRecords"`).
- Connection string (`src/Banking.API/appsettings.json`):
  `Host=127.0.0.1;Port=55432;Database=banking_basic;Username=postgres;Password=admin`
- Trước lần chạy đầu: `CREATE DATABASE banking_basic;` rồi backend tự `EnsureCreated()` + seed.
- Seed: user `demo`/`demo123`, Alice = 1000, Bob = 0.
- psql phải dùng full path: `C:\Program Files\PostgreSQL\18\bin\psql.exe` với `$env:PGPASSWORD`. Khi cần identifier có hoa → viết vào file `.sql` rồi `psql -f` (PowerShell nuốt dấu nháy trong `-c`).

## 6. Auth
- `POST /api/v1/auth/login` (ẩn danh) → JWT **HS256** (HCM production dùng RS256). Key/Issuer/Audience trong `appsettings.json`.
- Mật khẩu băm **PBKDF2** built-in (100000 iters, SHA256), format `base64(salt).base64(hash)`.
- `/api/v1/accounts` + `/api/v1/transfers` yêu cầu `[Authorize]` Bearer.
- Frontend lưu token `localStorage`, interceptor gắn `Authorization`, 401 → tự logout.

## 7. Cách chạy
```bash
# Backend (API :5199)  — dùng --no-launch-profile
cd "D:/course_my_self/BankingBasic"
dotnet run --project src/Banking.API --no-launch-profile

# Frontend (Vite :5180, proxy /api → :5199)
cd frontend
npm install     # lần đầu
npm run dev
# → mở http://localhost:5180, login demo/demo123
# → nút "Mô phỏng double-click": bắn 2 request song song cùng Idempotency-Key → tiền chỉ trừ 1 lần

# Test (SQLite in-memory, không đụng Postgres)
dotnet test
```
Cổng: backend `:5199`, frontend `:5180` (5173 bị SalaryTalent của HCM chiếm → phải strictPort 5180). CORS backend đã whitelist cả 5180 và 5173.

## 8. 7 test case (đều PASS) — `TransferMoneyIdempotencyTests.cs`
1. Chuyển hợp lệ → trừ đúng 1 lần.
2. Retry cùng key → trả cache, KHÔNG trừ lần 2.
3. Thiếu Idempotency-Key → bị từ chối.
4. Key đang `InProgress` → 409.
5. **N request đồng thời cùng key → chỉ trừ 1 lần** (race serialize bằng UNIQUE constraint).
6. Key khác nhau → đều chạy.
7. Số dư không đủ → không trừ.

Fixture (`BankDbFixture.cs`): SQLite `Cache=Shared;Default Timeout=5` + keep-alive connection để test concurrency.

## 9. Gotchas / lỗi đã gặp & cách xử lý (để không dẫm lại)
- **PowerShell 5.1 không load được assembly .NET 10** ("Could not load System.Runtime 6.0.0.0") → muốn dump DB bằng script thì dùng **.NET 10 file-based app** (`dotnet run dbdump.cs` với `#:package ...`), KHÔNG dùng Add-Type trong PowerShell.
- **`dotnet build Banking.sln` fail MSB1009** → đúng tên là `Banking.slnx`.
- **Vite drift khỏi 5173** vì HCM chiếm → đã pin `port:5180, strictPort:true`.
- **Auto-mode classifier chặn thử nhiều cặp credential** → KHÔNG đoán credential hàng loạt; hỏi user.
- Background task báo "exit 1" khi bị session kill ngoài, KHÔNG phải crash — chỉ cần restart.

## 10. Giới hạn còn lại (hướng học tiếp)
- Nếu business ném lỗi (vd thiếu tiền) SAU khi đã claim key → key kẹt `InProgress`. Thực tế xử lý bằng **TTL/expiry** hoặc set key sang `Failed` cho phép retry. Hiện giữ đơn giản.
- Có thể chỉnh log EF (ẩn stack trace duplicate-key đã xử lý) bằng cách hạ LogLevel cho `Microsoft.EntityFrameworkCore.Update` → `Critical`/`None` trong `appsettings.json` (chưa làm).
- Chưa có refresh token, chưa có RS256, chưa có TTL cleanup job.
