using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Security.Application.Services;
using Security.Domain.Interfaces;

namespace Security.Application;

[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register MediatR
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly())
        );

        services.AddSingleton<IMapper>(serviceProvider =>
            new MapperConfiguration(
                cfg => cfg.AddProfile<Mapping.UserContactMappingProfile>(),
                serviceProvider.GetRequiredService<ILoggerFactory>())
                .CreateMapper());

        // Register all validators from the Application assembly
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddScoped<IGrpcValidationService, GrpcValidationService>();

        return services;
    }
}
