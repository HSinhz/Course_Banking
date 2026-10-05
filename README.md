# Banking Basic — Idempotent Money Transfer (self-study)

Project tự học, mô phỏng **Clean Architecture của HCM** để luyện tập chống "chuyển tiền 2 lần".
KHÔNG thuộc git của HCM.

## Cấu trúc (Clean Architecture)

```
src/
  Banking.Domain          # Entities thuần: Account, Transfer, IdempotencyRecord (không phụ thuộc gì)
  Banking.Application      # CQRS/MediatR: TransferMoneyCommand + Handler, IBankingDbContext, Exceptions
  Banking.Infrastructure   # EF Core: BankingDbContext (SQLite), DI
  Banking.API              # ASP.NET host + TransfersController (đọc header Idempotency-Key)
tests/
  Banking.Application.Tests  # xUnit: kiểm chứng các case idempotency
```

Chiều phụ thuộc: `API → Infrastructure → Application → Domain` (Domain không tham chiếu ai).

## Cơ chế chống trùng (trái tim)

1. Client gửi `Idempotency-Key` (UUID) — cùng 1 key cho mọi lần retry của cùng 1 lệnh.
2. Handler **INSERT thẳng** key vào bảng `IdempotencyRecords` (Key = PRIMARY KEY → UNIQUE).
   Không "SELECT rồi INSERT" (check-then-act có khe hở race).
3. INSERT thành công → thực hiện chuyển tiền + lưu response + đánh dấu `Completed` (một `SaveChanges` = một transaction nguyên tử).
4. INSERT vi phạm UNIQUE (request lặp) →
   - key `Completed` → trả **response cũ** (không trừ lần 2);
   - key `InProgress` → **409** (request trước đang chạy).

## Chạy test

```bash
cd "D:/course_my_self/BankingBasic"
dotnet test
```

Các case được kiểm chứng: chuyển hợp lệ trừ 1 lần · retry cùng key trả cache ·
thiếu key bị từ chối · request 2 khi đang xử lý → 409 · **N request đồng thời chỉ trừ 1 lần** ·
key khác nhau đều chạy · số dư không đủ.

## Chạy đầy đủ: DB + Backend + Frontend (đăng nhập được)

Runtime DB = **PostgreSQL** (`banking_basic`), xem được trong pgAdmin4.
Connection string ở `src/Banking.API/appsettings.json`:
`Host=127.0.0.1;Port=55432;Database=banking_basic;Username=postgres;Password=admin`
Bảng + seed (user `demo`, Alice 1000 / Bob 0) tự tạo khi backend khởi động (`EnsureCreated`).
Trước khi chạy lần đầu, tạo DB rỗng: `CREATE DATABASE banking_basic;`
(Test vẫn dùng SQLite in-memory riêng — không đụng Postgres.)

**1) Backend (API :5199)**
```bash
dotnet run --project src/Banking.API --no-launch-profile
```

**2) Frontend (Vite :5180 — proxy /api sang :5199)**
```bash
cd frontend
npm install     # lần đầu
npm run dev
```

**3) Mở trình duyệt** → http://localhost:5180
- Đăng nhập: **demo / demo123**
- Trang Dashboard: xem số dư, chuyển tiền, và nút **"Mô phỏng double-click"** (bắn 2 request song song cùng Idempotency-Key) → tiền chỉ trừ 1 lần.

Cổng: backend `:5199`, frontend `:5180` (đổi trong `frontend/vite.config.ts` và `Program.cs` nếu trùng).

### Kiến trúc auth
- Login: `POST /api/v1/auth/login` (ẩn danh) → trả JWT **HS256** (HCM production dùng RS256).
- Mật khẩu băm **PBKDF2** (built-in, không package ngoài).
- `/api/v1/accounts` và `/api/v1/transfers` yêu cầu `[Authorize]` (Bearer token).
- Frontend lưu token ở `localStorage`, tự gắn `Authorization` header (giống `frontend/src/api/axios.ts` của HCM), 401 → tự đăng xuất.

## API test tay (curl/PowerShell)

```bash
# Header: Idempotency-Key: abc-123  (gửi lại y hệt → vẫn 200, số dư KHÔNG đổi thêm)
POST /api/v1/transfers
Body: { "fromAccountId": "11111111-1111-1111-1111-111111111111",
        "toAccountId":   "22222222-2222-2222-2222-222222222222", "amount": 100 }
```

## Ghi chú giới hạn (để học tiếp)

- Nếu business logic ném lỗi (vd số dư không đủ) SAU khi đã claim key, key bị kẹt `InProgress`.
  Thực tế xử lý bằng **TTL/expiry** hoặc chuyển key sang `Failed` để cho retry. Ở đây giữ đơn giản.
- Runtime dùng PostgreSQL (giống HCM); test dùng SQLite in-memory — **cùng một code handler** vì UNIQUE INSERT là chuẩn SQL (handler bắt cả `UNIQUE constraint failed` của SQLite lẫn `duplicate key` của Postgres).
