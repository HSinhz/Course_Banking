# Spec — Core Banking Giai đoạn 1: Double-Entry Ledger + Chống đua số dư

- **Ngày:** 2026-08-01
- **Project:** Banking Basic (.NET 10, Clean Architecture, EF Core, MediatR; runtime PostgreSQL, test SQLite in-memory)
- **Trạng thái:** Đã duyệt thiết kế, chờ implement.
- **Phạm vi tổng:** Người dùng chọn làm **CẢ 2 giai đoạn** core banking. Tài liệu này là **Giai đoạn 1** (nền tảng sổ cái nội bộ). Giai đoạn 2 (suspense account, saga + reversal, outbox, reconciliation, status inquiry) sẽ có spec riêng: `2026-08-01-core-banking-phase2-saga-design.md`.

---

## 1. Mục tiêu

Nâng chức năng chuyển tiền nội bộ từ "trừ/cộng `Account.Balance` trực tiếp" lên mô hình **core banking đúng chuẩn**:

1. **Double-entry ledger**: mọi chuyển động tiền được ghi bằng cặp bút toán Nợ/Có bất biến (append-only).
2. **Chống đua số dư** (lost update giữa các giao dịch **khác** Idempotency-Key) bằng optimistic concurrency.
3. **State machine + vá lỗ hổng "key kẹt `InProgress`"** đã biết trong bản cũ.
4. Giữ nguyên toàn bộ tính idempotent hiện có (chống trừ 2 lần khi retry / double-click).
5. **Không đổi hợp đồng API** (`TransferMoneyResult`, `GET /accounts`) ⇒ frontend không phải sửa.

## 2. Bối cảnh & vấn đề của bản hiện tại

`TransferMoneyCommandHandler` hiện:
- Claim key `InProgress` bằng **một** `SaveChanges` riêng, rồi chuyển tiền + đóng dấu `Completed` bằng `SaveChanges` **thứ hai**. → Nếu business logic ném lỗi sau khi đã claim (vd `InsufficientFundsException`), bản ghi `InProgress` **đã commit** không được dọn ⇒ **key kẹt vĩnh viễn**, retry cùng key luôn nhận 409.
- Trừ/cộng `Account.Balance` sau khi **đọc** rồi **ghi**, **không có khoá** ⇒ 2 giao dịch **khác key** chạy song song trên cùng tài khoản có thể cùng đọc số dư cũ và cùng trừ ⇒ **âm tiền (lost update)**.
- Không có sổ cái ⇒ không đối soát/kiểm toán được, không "double-entry".

## 3. Các quyết định thiết kế (option đã cân nhắc & option đã chọn)

> Người dùng yêu cầu ghi lại các phương án và phương án được chọn.

### QĐ-1. Mô hình số dư
- **(A) Duy trì `Balance` + concurrency token** — ✅ **ĐÃ CHỌN**. `Account.Balance` vẫn lưu sẵn (đọc nhanh) nhưng mỗi lần đổi phải kèm ghi bút toán ledger trong **cùng transaction** và tăng token chống đua. Ledger là nguồn sự thật bất biến để đối soát.
- (B) Số dư suy ra hoàn toàn từ ledger (`SUM`) — thuần kế toán, không bao giờ lệch, nhưng chậm khi nhiều bút toán (cần snapshot). Loại vì overkill cho bản học.
- (C) Running balance (`BalanceAfter` mỗi bút toán, `Account.Balance` là cache) — nhanh, nhưng buộc ghi tuần tự theo tài khoản. Không chọn làm mô hình chính; tuy nhiên **vẫn lưu `BalanceAfter` trên mỗi bút toán** như dữ liệu kiểm toán (không dùng làm nguồn số dư).

### QĐ-2. Cơ chế chống đua số dư
- **(A) Optimistic concurrency bằng token `Account.Version`** — ✅ **ĐÃ CHỌN**. Thêm cột `Version (int)` **app-managed** (tự tăng khi đổi số dư), khai báo `IsConcurrencyToken()`. EF sinh `UPDATE ... WHERE Id=@id AND Version=@old`; lệch → `DbUpdateConcurrencyException` → handler **retry** (vòng lặp có giới hạn).
  - **Vì sao không dùng `rowversion`/`xmin` của provider:** `xmin` (Postgres) và `rowversion` (SQL Server) là provider-specific; SQLite (dùng cho test) không có. Token `int` app-managed **chạy được trên cả Postgres lẫn SQLite** ⇒ test và runtime cùng một cơ chế.
- (B) Pessimistic lock (`SELECT ... FOR UPDATE`) — khoá chắc nhưng khoá lâu, cú pháp provider-specific, SQLite không hỗ trợ đầy đủ. Loại.
- (C) Atomic conditional update (`UPDATE SET Balance=Balance-@amt WHERE Balance>=@amt`) — 1 câu lệnh, nhưng phải viết SQL thô, khó ghi kèm ledger nguyên tử theo lối EF. Loại (ghi chú: đây là cách phổ biến ở hệ thống thật, để dành mở rộng).

### QĐ-3. Vá "key kẹt `InProgress`"
- **(A) Gộp claim + business logic + đóng dấu vào MỘT transaction; lỗi → rollback nhả key** — ✅ **ĐÃ CHỌN**.
  - Hệ quả then chốt: bản ghi idempotency **chỉ được commit ở trạng thái `Completed`**. Trạng thái `InProgress` **không bao giờ được persist riêng lẻ** trong Giai đoạn 1 ⇒ **bug key kẹt biến mất theo thiết kế**.
  - Lỗi nghiệp vụ (số dư không đủ) được ném **trước** `SaveChanges` ⇒ chưa có gì được ghi ⇒ key tự do, retry được.
- (B) Giữ 2-phase, khi lỗi đánh dấu `Failed` + lưu response lỗi (idempotent cả khi fail, kiểu Stripe) — đúng chuẩn hơn nhưng phức tạp; để dành Giai đoạn 2 khi giao dịch kéo dài qua hệ thống ngoài.
- (C) TTL/expiry cho `InProgress` — cần cho trường hợp crash ở Giai đoạn 2, chưa cần ở Giai đoạn 1 (single-transaction đã atomic).

### QĐ-4. Mô hình bút toán ledger
- **(A) `LedgerEntry` với `Direction` (Debit/Credit) + `Amount>0`** — ✅ **ĐÃ CHỌN**. Đúng thuật ngữ double-entry; Giai đoạn 2 thêm suspense chỉ là thêm cặp bút toán.
- (B) Một cột `Amount` có dấu (âm/dương), bất biến tổng = 0 — ít code nhưng mờ nghĩa Nợ/Có. Loại.

### QĐ-5. Ngữ nghĩa khi trùng key đồng thời (thay đổi so với bản cũ — cần cập nhật test)
- Bản cũ: request thứ 2 thấy key `InProgress` → ném `ConcurrentRequestException` (409).
- Bản mới (do QĐ-3): không còn `InProgress` committed. Hai request cùng key chạy song song → cái đến sau **chặn trên PRIMARY KEY** tới khi cái đầu commit, rồi:
  - cái đầu **thành công** → cái sau gặp UNIQUE violation → đọc bản ghi `Completed` → **trả cache**;
  - cái đầu **rollback** (lỗi) → INSERT của cái sau thành công → cái sau **thực thi thật**.
- `ConcurrentRequestException` (409) chỉ còn được ném khi **hết số lần retry** (hệ thống quá bận / xung đột token liên tục) — vẫn map 409.

## 4. Thay đổi Data model

### 4.1 Thêm `Banking.Domain/Ledger/LedgerEntry.cs`
```
enum LedgerDirection { Debit = 0, Credit = 1 }

class LedgerEntry : BaseEntity        // append-only, không sửa/xoá
{
    Guid   TransferId    // gom các bút toán của cùng 1 lệnh
    Guid   AccountId
    LedgerDirection Direction
    decimal Amount       // luôn > 0
    decimal BalanceAfter // số dư tài khoản NGAY SAU bút toán (dữ liệu kiểm toán)
    DateTime CreatedAt = UtcNow
}
```
Bất biến kế toán (invariant): với mỗi `TransferId`, **tổng `Amount` các Debit = tổng `Amount` các Credit**. Hiệu ứng số dư: `Credit` = +Balance, `Debit` = −Balance (đối với các tài khoản giao dịch nội bộ ở phạm vi này).

### 4.2 Sửa `Account`
Thêm `int Version` (mặc định 0), khai báo concurrency token trong `OnModelCreating`.

### 4.3 `Transfer` (journal header)
Giữ nguyên các trường; dùng `TransferStatus.Completed` làm trạng thái "Posted". Enum giữ `Pending`, `Failed` sẵn cho Giai đoạn 2 (sẽ bổ sung `Reversed`). Không đổi schema cột.

### 4.4 `IBankingDbContext` / `BankingDbContext`
- Thêm `DbSet<LedgerEntry> LedgerEntries`.
- `OnModelCreating`: cấu hình `LedgerEntry` (khoá, `Amount`/`BalanceAfter` là `decimal(18,2)`, index theo `AccountId`, `TransferId`); `Account.Version` là `IsConcurrencyToken()`.

## 5. Luồng handler (viết lại) — 1 transaction + retry

```
const MaxRetries = 3
if key rỗng -> throw MissingIdempotencyKeyException          // 400

for attempt in 1..MaxRetries:
    ctx.ChangeTracker.Clear()

    existing = IdempotencyRecords.AsNoTracking().FirstOrDefault(r => r.Key == key)
    if existing != null:
        if existing.Status == Completed && existing.ResponseBody != null:
            return cache(existing) with ServedFromCache = true          // retry idempotent (đọc trạng thái TERMINAL, an toàn)
        else:
            // bản ghi non-terminal còn sót (crash cũ / seed) -> Giai đoạn 1 KHÔNG có InProgress hợp lệ -> thu hồi
            remove(existing); SaveChanges(); ctx.ChangeTracker.Clear()
            // (ghi chú: Giai đoạn 2 sẽ dùng lease/TTL thay vì thu hồi vô điều kiện)

    from = Accounts.First(FromAccountId)  ?? throw AccountNotFound      // 404
    to   = Accounts.First(ToAccountId)    ?? throw AccountNotFound      // 404
    if from.Balance < Amount: throw InsufficientFundsException          // 422  (ném TRƯỚC SaveChanges => key không bị claim)

    from.Balance -= Amount; from.Version++      // EF: WHERE Version=@old
    to.Balance   += Amount; to.Version++
    transfer = new Transfer(Completed, key, from, to, Amount)
    add LedgerEntry(Debit,  from, Amount, BalanceAfter = from.Balance)
    add LedgerEntry(Credit, to,   Amount, BalanceAfter = to.Balance)
    result = new TransferMoneyResult(transfer.Id, from.Balance, "Completed")
    add IdempotencyRecord(key, Completed, ResponseBody = json(result))

    try:
        SaveChanges()               // MỘT commit nguyên tử: ledger + số dư + transfer + key
        return result
    catch DbUpdateConcurrencyException:      // token số dư lệch -> có kẻ khác vừa đổi
        continue                              // retry: đọc lại số dư mới
    catch DbUpdateException when UniqueViolation:   // key bị request khác tạo trước
        reload = IdempotencyRecords.AsNoTracking().First(key)
        if reload.Status == Completed: return cache(reload)
        else: continue                        // hiếm: retry

throw ConcurrentRequestException             // 409: hết retry, hệ thống quá bận
```

**Lưu ý teaching:** cơ chế chống trùng-key **vẫn là INSERT-first + UNIQUE constraint** (DB làm trọng tài) — phần đọc `existing` ở đầu chỉ là fast-path cho trạng thái **terminal `Completed`** (bất biến nên đọc an toàn), **không** phải "check-then-act" khi tạo mới.

## 6. Xử lý lỗi → HTTP (giữ map hiện có ở `TransfersController`)
| Tình huống | Exception | HTTP |
|---|---|---|
| Thiếu key | `MissingIdempotencyKeyException` | 400 |
| Số dư không đủ | `InsufficientFundsException` | 422 |
| Không thấy tài khoản | `AccountNotFoundException` | 404 |
| Hết retry vì xung đột | `ConcurrentRequestException` | 409 |

## 7. Kiểm thử (xUnit, SQLite in-memory, `Cache=Shared`)

**Giữ xanh:** CASE 1, 2, 3, 6, 7 hiện có (hành vi không đổi).

**Cập nhật (do QĐ-5):**
- CASE 4 (cũ: seed `InProgress` → 409) → **đổi ý nghĩa**: seed một bản ghi `InProgress` sót lại (mô phỏng crash cũ) → retry cùng key **thu hồi được và chuyển tiền thành công** (chứng minh bug key-kẹt đã hết). 
- CASE 5 (N request đồng thời cùng key): kỳ vọng mới = trừ đúng 1 lần, đúng 1 `Transfer`, **đúng 1** kết quả `ServedFromCache=false`, các kết quả còn lại `ServedFromCache=true` (không còn null/409).

**Thêm mới:**
- **T-Ledger-Conservation:** sau 1 transfer → có đúng 2 `LedgerEntry` (1 Debit, 1 Credit), tổng Debit = tổng Credit; `Account.Balance` khớp `SUM(hiệu ứng ledger)` của tài khoản.
- **T-Balance-Race:** Alice = 150; hai lệnh **khác key** 100 mỗi lệnh chạy song song → **đúng 1** thành công, **1** `InsufficientFundsException`; cuối cùng Alice = 50, không âm; đúng 1 `Transfer` + 2 `LedgerEntry`. (Chứng minh optimistic concurrency chặn lost-update; kết quả **tất định** nhờ token dù thứ tự interleave thay đổi.)
- **T-StuckKey-Recovered:** claim hụt do số dư không đủ (ném trước SaveChanges) → key không bị ghi → nạp thêm tiền → retry **cùng key** thành công (bổ trợ CASE 4).

## 8. Ảnh hưởng vận hành
- App dùng `EnsureCreated()` (không migrations) ⇒ schema mới **không tự áp** lên DB Postgres `banking_basic` đã tồn tại. Khi verify runtime phải **drop & tạo lại** DB `banking_basic` (chỉ chứa dữ liệu seed demo, an toàn). Test không ảnh hưởng (mỗi fixture `EnsureCreated` mới).
- Không đổi hợp đồng API ⇒ **frontend không cần sửa**.

## 9. Ngoài phạm vi (để Giai đoạn 2)
Suspense account, saga + reversal/compensating, outbox, reconciliation job, status inquiry, mô phỏng bank ngoài fail/timeout, trạng thái `Pending`/`Reversed`.
