namespace NICE.Platform.Collaboration.Application;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using NICE.Platform.Collaboration.Application.Common.Behaviours;
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
        services.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        return services;
    }
}
