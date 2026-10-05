# Spec — Core Banking Giai đoạn 2: Chuyển liên ngân hàng (Saga + Suspense + Outbox + Reconciliation)

- **Ngày:** 2026-08-01
- **Project:** Banking Basic (.NET 10, Clean Architecture, EF Core, MediatR)
- **Tiền đề:** Xây trên Giai đoạn 1 (double-entry ledger + `Account.Version` + handler idempotent 1-transaction). Xem `2026-08-01-core-banking-phase1-ledger-design.md`.
- **Trạng thái:** Người dùng yêu cầu tự quyết option + code luôn, không confirm lại.

---

## 1. Mục tiêu
Mô phỏng luồng chuyển tiền **liên ngân hàng** (như Vietcombank → NAPAS → bank khác), nơi bên nhận là **hệ thống ngoài có thể thành công / thất bại / timeout**. Phải đảm bảo: tiền **không bao giờ nằm nửa vời** — luôn hội tụ về *hoàn tất hẳn (Posted)* hoặc *hoàn tiền hẳn (Reversed)*.

Thành phần: **suspense account** (tài khoản treo), **saga + compensating (reversal)**, **transactional outbox** (chống crash/mất lệnh), **reconciliation** (đối soát khi timeout/unknown), **status inquiry** (tra cứu trạng thái).

## 2. Quyết định thiết kế (option cân nhắc & đã chọn)

### QĐ-6. Điều phối saga
- **(A) Orchestration qua Outbox + worker nền (khuyến nghị)** — ✅ **ĐÃ CHỌN**. Bước 1 ghi ý định vào `OutboxMessage` cùng transaction với bút toán; một worker nền đọc outbox, gọi bank ngoài, rồi settle/reverse. Đúng mô hình thật, chống crash, bộc lộ rõ tính bất đồng bộ.
- (B) Gọi đồng bộ ngay trong request — đơn giản nhưng nếu process chết giữa chừng là mất lệnh; không dạy được outbox/timeout. Loại (nhưng handler xử lý outbox tách riêng nên **test gọi trực tiếp được, không cần chờ worker**).
- (C) Message broker thật (Kafka/RabbitMQ) — đúng production nhưng nặng hạ tầng cho bản học. Loại.

### QĐ-7. Mô phỏng bank ngoài (`IExternalBankGateway`)
- **(A) Gateway giả lập tất định theo quy ước số tiền (khuyến nghị)** — ✅ **ĐÃ CHỌN**. Kết quả suy ra từ phần lẻ số tiền để demo/test tất định, không ngẫu nhiên:
  - số tiền lẻ `.13` (vd `100.13`) → **Failed** (bank ngoài từ chối).
  - số tiền lẻ `.99` → **Unknown/Timeout** (không rõ kết quả).
  - còn lại → **Succeeded**.
  - `QueryStatusAsync` (dùng khi reconcile): quy ước `.99` khi tra cứu lại → coi như **Failed** (để reconcile giải quyết được), các trường hợp khác → **Succeeded**.
- (B) Ngẫu nhiên — không test tất định được. Loại.
- (C) Cấu hình qua request/header — linh hoạt nhưng rối API. Loại (test tự inject gateway fake riêng khi cần).

### QĐ-8. Vòng đời `Transfer` (state machine)
- **ĐÃ CHỌN:** `Pending` (đã ghi nợ nguồn → treo, chờ bank ngoài) → `Completed`/**Posted** (bank ngoài nhận) **hoặc** `Reversed` (bank ngoài từ chối → đã hoàn tiền nguồn). Bổ sung enum `Reversed`.
- Chuyển nội bộ (Giai đoạn 1) vẫn đi thẳng `Completed` — **không đổi**.

### QĐ-9. Suspense account
- **ĐÃ CHỌN:** một `Account` well-known (`Id = 9999...9999`, tên `SUSPENSE`), seed khi khởi động. Tiền "đang bay" nằm ở đây ⇒ mọi lúc đều kiểm đếm được; bảo toàn tổng tiền toàn hệ thống (gồm suspense) = hằng số.

### QĐ-10. Kích hoạt reconciliation
- **ĐÃ CHỌN:** endpoint thủ công `POST /api/v1/admin/reconcile` (đơn giản cho bản học) quét các `Transfer` còn `Pending` → gọi `QueryStatusAsync` → settle/reverse. (Production: chạy theo lịch/cron — ngoài phạm vi.)

### QĐ-11. Idempotency của bước xử lý outbox
- **ĐÃ CHỌN:** mỗi `OutboxMessage` có `Status` (`Pending`/`Processed`/`Failed`); xử lý xong đánh `Processed` trong cùng transaction với settle/reverse ⇒ chạy lại worker không settle 2 lần. Settle/reverse còn được gác bởi kiểm tra `Transfer.Status` (chỉ tác động khi đang `Pending`).

## 3. Data model (thêm/sửa)

### 3.1 `Banking.Domain/Transfers/Transfer.cs`
- Enum `TransferStatus`: thêm `Reversed = 3`.
- Thêm `TransferKind { Internal = 0, Interbank = 1 }` + cột `Kind`.
- Thêm `string? ExternalBankName` (thông tin bên nhận ngoài, tuỳ chọn).

### 3.2 `Banking.Domain/Outbox/OutboxMessage.cs` (mới)
```
enum OutboxStatus { Pending=0, Processed=1, Failed=2 }
class OutboxMessage : BaseEntity {
    string  Type          // "SettleInterbankTransfer"
    Guid    TransferId
    OutboxStatus Status
    int     Attempts
    DateTime CreatedAt
    DateTime? ProcessedAt
    string?  LastError
}
```

### 3.3 Suspense
- `Banking.Domain/Accounts/WellKnownAccounts.cs`: hằng `SuspenseId = Guid("99999999-9999-9999-9999-999999999999")`.
- Seed trong `Program.cs` + trong test fixture.

### 3.4 DbContext
- `DbSet<OutboxMessage> OutboxMessages` (+ interface). Cấu hình khoá, index `Status`.

## 4. Luồng (saga qua outbox)

### 4.1 `InterbankTransferCommandHandler` — Bước 1 (atomic, idempotent)
Giống handler Giai đoạn 1 (1 transaction + retry + Idempotency-Key), nhưng chuyển động tiền là **nguồn → suspense** và tạo outbox:
```
- debit source (Version++), credit SUSPENSE (Version++)
- LedgerEntry(Debit source), LedgerEntry(Credit suspense)   [BalanceAfter]
- Transfer(Kind=Interbank, Status=Pending, ExternalBankName)
- OutboxMessage(Type="SettleInterbankTransfer", TransferId, Pending)
- IdempotencyRecord(Completed, response={transferId, status="Pending"})
- SaveChanges  (một commit)
Trả về: { transferId, status="Pending" }  → tiền đã rời nguồn, đang treo.
```

### 4.2 `ProcessOutboxCommandHandler` — Bước 2 (worker gọi 1 lần / message)
```
msg = outbox Pending kế tiếp; nếu không có → thôi
transfer = load(msg.TransferId)
nếu transfer.Status != Pending → đánh msg Processed (đã xử lý ở đâu đó) → return
result = gateway.SendAsync(transfer)
switch result:
  Succeeded:
     debit SUSPENSE, (tiền rời hệ thống sang bank ngoài) ; LedgerEntry(Debit suspense)
     transfer.Status = Completed(Posted)
     msg.Status = Processed
  Failed:
     debit SUSPENSE, credit SOURCE (REVERSAL - hoàn tiền)
     LedgerEntry(Debit suspense) + LedgerEntry(Credit source)
     transfer.Status = Reversed
     msg.Status = Processed
  Unknown/Timeout:
     msg.Attempts++ ; msg.LastError="timeout" ; GIỮ Pending (không đoán)
SaveChanges (một commit: ledger + số dư + transfer.Status + msg.Status)
```
Ghi chú bảo toàn: khi Succeeded, tiền rời khỏi hệ thống (sang bank ngoài) nên tổng nội bộ giảm đúng bằng amount; suspense về 0 cho giao dịch đó. Khi Reversed, tiền quay lại nguồn; suspense về 0; tổng nội bộ không đổi.

### 4.3 `ReconcileCommandHandler` — lưới an toàn
```
với mỗi Transfer Pending:
   st = gateway.QueryStatusAsync(transfer)
   Succeeded → settle (như trên); Failed → reverse; Unknown → bỏ qua lần này
```

### 4.4 Status inquiry
`GET /api/v1/transfers/{id}` → `{ id, status, kind, amount, createdAt }`.

## 5. Worker nền (API)
`OutboxProcessorHostedService : BackgroundService` — mỗi ~1s tạo scope, gọi `ProcessOutboxCommand` tới khi hết message Pending. Ở test **không** dùng worker; gọi thẳng `ProcessOutboxCommandHandler` để tất định.

## 6. API endpoints (thêm)
- `POST /api/v1/transfers/interbank` (Authorize, header `Idempotency-Key`) → bắt đầu saga, trả `Pending`.
- `GET  /api/v1/transfers/{id}` (Authorize) → trạng thái.
- `POST /api/v1/admin/reconcile` (Authorize) → chạy đối soát, trả số giao dịch đã xử lý.

## 7. Kiểm thử (xUnit, SQLite in-memory) — inject gateway fake
Fixture seed thêm SUSPENSE. Fake gateway cho phép chỉ định outcome.
- **T-Saga-Success:** interbank 100 → Pending; process outbox (gateway Succeeded) → Posted; source −100, suspense 0, outbox Processed.
- **T-Saga-Fail-Reversed:** gateway Failed → Reversed; source được hoàn đủ (net 0), suspense 0, transfer Reversed.
- **T-Saga-Timeout-then-Reconcile:** gateway Unknown → vẫn Pending sau process; reconcile (QueryStatus Succeeded) → Posted.
- **T-Outbox-Idempotent:** chạy process 2 lần → không settle 2 lần (số dư/ledger không đổi lần 2).
- **T-Conservation-With-Suspense:** tổng (tất cả Account gồm suspense) + tiền đã ra ngoài (Debit suspense khi Succeeded) khớp; với Reversed tổng nội bộ giữ nguyên.
- Giữ toàn bộ test Giai đoạn 1 xanh.

## 8. Ảnh hưởng
- `EnsureCreated()`: cần **drop & tạo lại** `banking_basic` (thêm bảng `OutboxMessages`, cột `Transfers.Kind/ExternalBankName/Reversed`, account SUSPENSE). Test tự tạo mới.
- Frontend: có thể bổ sung nút "Chuyển liên ngân hàng" sau; **không bắt buộc** cho Giai đoạn 2 (API + test là đủ chứng minh). Sẽ thêm UI ở bước riêng nếu người dùng muốn.

## 9. Ngoài phạm vi
Message broker thật, cron scheduler, TTL lease cho outbox, multi-currency, phí giao dịch.
