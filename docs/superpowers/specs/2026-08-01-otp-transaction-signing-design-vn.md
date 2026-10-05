# Đặc tả — Ký Giao Dịch Bằng OTP (2FA) — Bảo vệ P23 & P24

- **Ngày:** 2026-08-01
- **Trạng thái:** Thiết kế (đã được phê duyệt để ghi lại; chưa triển khai).
- **Bổ sung quy tắc bảo vệ:** `P23` (yếu tố thứ hai OTP) và `P24` (ràng buộc thử thách OTP / dùng một lần / hết hạn / khóa) vào `docs/PROTECTIONS.md`.
- **Mục tiêu:** chuyển đổi *OTP giả phía client* hiện tại (`Dashboard.tsx` chấp nhận `123456`) thành **yếu tố thứ hai được xác minh thực sự phía server** kiểm soát việc chuyển tiền, mà không phá vỡ các bảo vệ idempotency/sổ cái hiện có.

---

## 1. Các lựa chọn đã xem xét & đã chọn

### QĐ‑OTP‑1. OTP được xác minh ở đâu?
- **(A) Phía server, hai endpoint: `initiate` (phát hành) + `confirm` (xác minh rồi thực thi) — ✅ ĐÃ CHỌN.** Tiền chỉ di chuyển bên trong `confirm`, sau khi mã được xác minh phía server.
- (B) Kiểm tra phía client — ❌ stub hiện tại; dễ dàng bị bỏ qua. Bị loại.

### QĐ‑OTP‑2. Ràng buộc (chống replay)
- **(A) Ràng buộc OTP với `(Idempotency‑Key, FromAccountId, ToAccountId, Amount)` — ✅ ĐÃ CHỌN.** Mã được phát hành cho "100 đến Bob" không thể xác nhận "1000 đến Mallory". Dùng một lần + TTL ngắn + khóa sau số lần thử.
- (B) Chỉ ràng buộc với user/session — yếu hơn; mã bị đánh cắp có thể ký một giao dịch khác. Bị loại.

### QĐ‑OTP‑3. Vòng đời Idempotency‑Key
- **ĐÃ CHỌN:** khóa được tạo một lần tại bước **Confirm** (đã đúng trong UI, P1), gửi đến `initiate`, lưu trên `OtpChallenge`, và **tái sử dụng** bởi `confirm`. Vì vậy OTP, challenge, và `TransferMoneyCommand` cuối cùng đều chia sẻ một khóa → các lần thử lại đều an toàn từ đầu đến cuối.

### QĐ‑OTP‑4. Gửi mã
- **ĐÃ CHỌN (học tập):** dev `IOtpSender` ghi log / trả về mã (không có SMS gateway). Interface cho phép nhà cung cấp SMS/push thực sự thay thế sau. Mã là 6 chữ số.

### QĐ‑OTP‑5. Mã khi lưu trữ
- **ĐÃ CHỌN:** chỉ lưu **hash PBKDF2** của mã (tái sử dụng `Pbkdf2PasswordHasher`, P21). Không bao giờ lưu OTP dạng plaintext.

---

## 2. Mô hình dữ liệu (kế hoạch)

`src/Banking.Domain/Otp/OtpChallenge.cs`
```
class OtpChallenge : BaseEntity {
    string  IdempotencyKey     // ràng buộc challenge ↔ transfer (P1/P24)
    Guid    FromAccountId
    Guid    ToAccountId
    decimal Amount             // tham số ràng buộc (P24)
    string  CodeHash           // PBKDF2 (P21) — không bao giờ plaintext (P24)
    DateTime ExpiresAt         // TTL ngắn, ví dụ 2 phút (P24)
    int      Attempts          // đếm số lần nhập sai (P24)
    bool     Consumed          // dùng một lần (P24)
    DateTime CreatedAt
}
```
`BankingDbContext`: thêm `DbSet<OtpChallenge>`, index trên `IdempotencyKey` (unique).

## 3. Backend (kế hoạch)

- `IOtpSender` (Application) + `DevOtpSender` (Infrastructure) — "gửi" mã.
- `InitiateTransferCommand(IdempotencyKey, From, To, Amount)` → xác thực tài khoản tồn tại (P20 upstream), kiểm tra số dư **sơ bộ** (P6), tạo mã 6 chữ số, lưu `OtpChallenge` đã hash ràng buộc với các tham số (P23/P24), gửi mã. **Không chuyển tiền.** Trả về `{ challengeSent: true }` (+ mã dev).
- `ConfirmTransferCommand(IdempotencyKey, Otp)`:
  1. Tải challenge theo `IdempotencyKey`; **xác minh** chưa `Consumed`, chưa hết hạn, `Attempts < max`, `CodeHash` khớp (P24). Nếu thất bại: `Attempts++`, lưu, trả về `401/422`.
  2. Đánh dấu `Consumed = true` (dùng một lần, P24).
  3. Thực thi `TransferMoneyCommand` hiện có với **cùng** `IdempotencyKey` → kích hoạt P1/P2/P3/P5/P6/P8.
  4. Cùng commit ghi dấu bản ghi idempotency (P2).
- `TransfersController`: `POST /api/v1/transfers/initiate`, `POST /api/v1/transfers/confirm` (cả hai đều `[Authorize]`, P20).

## 4. UI (kế hoạch)

`frontend/src/pages/Dashboard.tsx`:
- Nút bước **Confirm** → gọi `initiateTransfer(key, from, to, amount)` (trước đây: chỉ chuyển sang màn hình OTP). Key đã được tạo trong `goConfirm` (P1).
- **Bước OTP** → ô nhập 6 ký tự thực (đã xây dựng) nay gọi `confirmTransfer(key, otp)` thay vì so sánh với `123456`. Hiển thị đồng hồ đếm ngược hết hạn + số lần thử còn lại (P24).
- **Bước kết quả** không thay đổi (P18).

`frontend/src/api.ts`: thêm `initiateTransfer` và `confirmTransfer`.

---

## 5. Luồng — "Xác nhận Nạp tiền" → OTP → xác nhận lại
(Quy tắc bảo vệ nào được kích hoạt ở mỗi bước. Tiền chỉ di chuyển TẠI bước 6.)

| # | Hành động | Nơi thực hiện | Bảo vệ được kích hoạt |
|---|-----------|---------------|-----------------------|
| 0 | Người dùng đã đăng nhập; mọi lệnh gọi đều mang JWT | interceptor `api.ts` | **P20** (JWT), **P21** (hash khi đăng nhập), **P22** (401→đăng xuất) |
| 1 | Điền from/to/amount, nhấn **Tiếp tục** | input `Dashboard.tsx` | xác thực client + **P6** sơ bộ |
| 2 | Màn hình xác nhận hiển thị; client tạo **một** Idempotency‑Key | `Dashboard.tsx goConfirm` | **P1** (key được tạo & ràng buộc) |
| 3 | Nhấn **Xác nhận / Confirm Deposit** → `POST /transfers/initiate` | `TransfersController.initiate` → `InitiateTransferCommand` | **P4** (key bắt buộc), **P6** (kiểm tra số dư sơ bộ), **P23** (phát hành OTP), **P24** (challenge ràng buộc với key+from+to+amount, hash **P21**, TTL, Attempts=0, Consumed=false). **Không chuyển tiền.** |
| 4 | OTP được gửi ngoài kênh (dev: hiển thị/ghi log) | `DevOtpSender` | **P23** gửi mã |
| 5 | Người dùng nhập mã 6 chữ số, nhấn xác nhận → `POST /transfers/confirm` | `TransfersController.confirm` → `ConfirmTransferCommand` | **P20** (JWT), **P24** xác minh: khớp hash / chưa hết hạn / `Attempts<max` / chưa `Consumed`; sai → `Attempts++` + từ chối; đúng → đánh dấu `Consumed` (dùng một lần) |
| 6 | OTP hợp lệ, cùng handler chạy `TransferMoneyCommand` (cùng key) | `ConfirmTransferCommand` → `TransferMoneyCommandHandler` | **P1** (INSERT‑first UNIQUE), **P2** (một commit nguyên tử), **P3** (cache nếu confirm thử lại), **P5** (race số dư + retry), **P6** (kiểm tra số dư cuối), **P7** (decimal), **P8/P9** (sổ cái kép + BalanceAfter). **Tiền di chuyển ở đây.** |
| 7 | Màn hình kết quả hiển thị trạng thái + txn id | `Dashboard.tsx` result / `GET /transfers/{id}` | **P18** (truy vấn trạng thái) |
| 7b | (Chỉ liên ngân hàng) worker thanh toán / đối soát | `ProcessOutbox`, `AdminController.reconcile` | **P10–P17** (suspense, saga, outbox, reconcile, state machine) |

### Tại sao thứ tự quan trọng
- OTP được xác minh (P24) **trước** lệnh chuyển tiền (bước 6), nên một JWT bị đánh cắp không thể chuyển tiền một mình (P23).
- **Cùng Idempotency‑Key** trải dài từ initiate → confirm → transfer, nên nhấn đúp "confirm" được P1/P3 hấp thụ (chỉ tính phí một lần), và OTP chính nó là dùng một lần (P24) — hai lớp chống replay độc lập.
- Tiền di chuyển trong **một commit** tại bước 6 (P2), được bảo vệ bởi đồng thời (P5) và sổ cái (P8) — lớp OTP bổ sung xác thực mà không làm yếu bất kỳ đảm bảo hiện có nào.

---

## 6. Ngoài phạm vi
Nhà cung cấp SMS/push thực, giới hạn tốc độ theo user/IP, điều tiết gửi lại OTP, quyền sở hữu tài khoản↔người dùng (khoảng trống riêng), ghi nhớ thiết bị tin cậy.
