using Banking.Application.Common.Exceptions;
using Banking.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Banking.Application.Auth.Commands;

public record LoginCommand(string Username, string Password) : IRequest<LoginResult>;

public record LoginResult(string Token, string DisplayName);

public class LoginCommandHandler(IBankingDbContext db, IPasswordHasher hasher, IJwtTokenService jwt)
    : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username, ct)
                   ?? throw new InvalidCredentialsException();

        if (!hasher.Verify(request.Password, user.PasswordHash))
            throw new InvalidCredentialsException();

        return new LoginResult(jwt.CreateToken(user), user.DisplayName);
    }
}
