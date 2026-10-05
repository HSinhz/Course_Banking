namespace Banking.Application.Common.Exceptions;

/// <summary>Request chuyển tiền thiếu Idempotency-Key → 400.</summary>
public sealed class MissingIdempotencyKeyException()
    : Exception("Idempotency-Key là bắt buộc cho lệnh chuyển tiền.");

/// <summary>Đã có request cùng key đang xử lý (IN_PROGRESS) → 409.</summary>
public sealed class ConcurrentRequestException()
    : Exception("Một request cùng Idempotency-Key đang được xử lý.");

/// <summary>Số dư không đủ → 422.</summary>
public sealed class InsufficientFundsException()
    : Exception("Số dư không đủ để thực hiện chuyển tiền.");

/// <summary>Không tìm thấy tài khoản → 404.</summary>
public sealed class AccountNotFoundException(Guid id)
    : Exception($"Không tìm thấy tài khoản {id}.");

/// <summary>Sai tài khoản/mật khẩu → 401.</summary>
public sealed class InvalidCredentialsException()
    : Exception("Tên đăng nhập hoặc mật khẩu không đúng.");
