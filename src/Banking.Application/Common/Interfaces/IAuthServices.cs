using Banking.Domain.Users;

namespace Banking.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string CreateToken(User user);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}
