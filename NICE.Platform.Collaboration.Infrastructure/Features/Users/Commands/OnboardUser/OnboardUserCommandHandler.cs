namespace NICE.Platform.Collaboration.Infrastructure.Features.Users.Commands.OnboardUser;

using MediatR;
using NICE.Platform.Collaboration.Application.Features.Users.Commands.OnboardUser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Core.Responses;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class OnboardUserCommandHandler(
    CollaborationDbContext db,
    ILogger<OnboardUserCommandHandler> logger)
    : IRequestHandler<OnboardUserCommand, SessionResponse>
{
    public async Task<SessionResponse> Handle(
        OnboardUserCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "OnboardUser: externalId={ExternalId} app={AppId} role={Role}",
            request.ExternalId, request.ApplicationId, request.Role);

        var now = DateTime.UtcNow;

        // Upsert user by ExternalId
        var nameParts = request.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : request.Name;
        var lastName  = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.ExternalUserId == request.ExternalId, cancellationToken);

        if (user is null)
        {
            user = new CollaborationUser
            {
                Id             = Guid.NewGuid(),
                ExternalUserId = request.ExternalId,
                FirstName      = firstName,
                LastName       = lastName,
                Email          = request.Email,
                IsActive       = true,
                CreatedAt      = now
            };
            await db.Users.AddAsync(user, cancellationToken);
        }
        else
        {
            user.FirstName = firstName;
            user.LastName  = lastName;
            user.Email     = request.Email;
            user.IsActive  = true;
            db.Users.Update(user);
        }

        // Upsert application membership
        var appUser = await db.ApplicationUsers.FirstOrDefaultAsync(
            au => au.UserId == user.Id && au.ApplicationId == request.ApplicationId,
            cancellationToken);

        if (appUser is null)
        {
            await db.ApplicationUsers.AddAsync(new CollaborationApplicationUser
            {
                ApplicationId = request.ApplicationId,
                UserId        = user.Id,
                Role          = request.Role,
                IsActive      = true,
                AddedAt       = now
            }, cancellationToken);
        }
        else
        {
            appUser.Role     = request.Role;
            appUser.IsActive = true;
            db.ApplicationUsers.Update(appUser);
        }

        await db.SaveChangesAsync(cancellationToken);

        // SessionResponse here is a confirmation; the actual JWT is issued by AuthController.
        return new SessionResponse
        {
            SessionId     = Guid.NewGuid(),
            Token         = string.Empty,   // JWT issued by AuthController, not here
            Role          = request.Role,
            UserType      = request.Role,
            ApplicationId = request.ApplicationId
        };
    }
}
