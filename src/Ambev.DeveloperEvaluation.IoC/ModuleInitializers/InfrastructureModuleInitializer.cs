using Ambev.DeveloperEvaluation.Domain.Events;
using Ambev.DeveloperEvaluation.Domain.Repositories;
using Ambev.DeveloperEvaluation.Domain.Services;
using Ambev.DeveloperEvaluation.ORM;
using Ambev.DeveloperEvaluation.ORM.Events;
using Ambev.DeveloperEvaluation.ORM.Mongo;
using Ambev.DeveloperEvaluation.ORM.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ambev.DeveloperEvaluation.Common.Caching;

namespace Ambev.DeveloperEvaluation.IoC.ModuleInitializers;

/// <summary>
/// Registers the infrastructure services: repositories, the discount policy and the
/// domain event publishers.
/// </summary>
public class InfrastructureModuleInitializer : IModuleInitializer
{
    /// <summary>
    /// Registers infrastructure dependencies into the application's container.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    public void Initialize(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<DbContext>(provider => provider.GetRequiredService<DefaultContext>());
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<ISaleRepository, SaleRepository>();

        // The discount rules are stateless, so a single instance serves every
        // request. Registered here rather than being newed up inside a handler so
        // that changing the pricing scheme is a one-line change in composition.
        builder.Services.AddSingleton<IDiscountPolicy, QuantityTierDiscountPolicy>();

        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
            });
            builder.Services.AddSingleton<ICacheService, DistributedCacheService>();
        }
        else
        {
            builder.Services.AddSingleton<ICacheService, NullCacheService>();
        }

        RegisterDomainEventPublishing(builder);
    }

    /// <summary>
    /// Registers the domain event publishers and the composite that fans out to them.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <remarks>
    /// The individual publishers are registered as their concrete types and the
    /// composite is registered as the <see cref="IDomainEventPublisher"/> everything
    /// else resolves. Registering the publishers as the interface instead would make
    /// the composite resolve itself and recurse.
    ///
    /// Which sinks are active is decided here and nowhere else, which is what lets
    /// MongoDB be switched on or off by configuration without any other file
    /// changing.
    /// </remarks>
    private static void RegisterDomainEventPublishing(WebApplicationBuilder builder)
    {
        builder.Services.Configure<MongoSettings>(
            builder.Configuration.GetSection(MongoSettings.SectionName));

        builder.Services.AddSingleton<LoggingDomainEventPublisher>();
        builder.Services.AddSingleton<MongoDomainEventStore>();

        builder.Services.AddSingleton<IDomainEventPublisher>(provider =>
        {
            var publishers = new IDomainEventPublisher[]
            {
                // Always on: this is what satisfies the brief's requirement that the
                // four sale events be announced somewhere observable.
                provider.GetRequiredService<LoggingDomainEventPublisher>(),

                // Reads Mongo:Enabled itself and returns immediately when off, so it
                // stays registered and costs nothing when MongoDB is not running.
                provider.GetRequiredService<MongoDomainEventStore>()
            };

            return new CompositeDomainEventPublisher(
                publishers,
                provider.GetRequiredService<ILogger<CompositeDomainEventPublisher>>());
        });
    }
}
