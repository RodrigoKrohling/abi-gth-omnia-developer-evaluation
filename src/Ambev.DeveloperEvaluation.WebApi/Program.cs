using Ambev.DeveloperEvaluation.Application;
using Ambev.DeveloperEvaluation.Common.HealthChecks;
using Ambev.DeveloperEvaluation.Common.Logging;
using Ambev.DeveloperEvaluation.Common.Security;
using Ambev.DeveloperEvaluation.Common.Validation;
using Ambev.DeveloperEvaluation.IoC;
using Ambev.DeveloperEvaluation.ORM;
using Ambev.DeveloperEvaluation.WebApi.Middleware;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Ambev.DeveloperEvaluation.WebApi;

public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            Log.Information("Starting web application");

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.AddDefaultLogging();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();

            builder.AddBasicHealthChecks();
            builder.Services.AddSwaggerGen();

            builder.Services.AddDbContext<DefaultContext>(options =>
                options.UseNpgsql(
                    builder.Configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("Ambev.DeveloperEvaluation.ORM")
                )
            );

            builder.Services.AddJwtAuthentication(builder.Configuration);

            builder.RegisterDependencies();

            // Scans both assemblies for AutoMapper Profile classes: the WebApi layer
            // maps Request -> Command and Result -> Response, the Application layer
            // maps Command -> Entity and Entity -> Result.
            //
            // AutoMapper 15 removed the AddAutoMapper(params Assembly[]) overload in
            // favour of configuring the expression explicitly.
            builder.Services.AddAutoMapper(cfg => cfg.AddMaps(
                typeof(Program).Assembly,
                typeof(ApplicationLayer).Assembly));

            builder.Services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblies(
                    typeof(ApplicationLayer).Assembly,
                    typeof(Program).Assembly
                );
            });

            builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

            var app = builder.Build();

            // Registered first so that it wraps every other middleware and endpoint:
            // anything thrown downstream is converted into the documented
            // { type, error, detail } body instead of an HTML error page.
            app.UseMiddleware<ExceptionHandlingMiddleware>();

            // -- Schema ---------------------------------------------------------
            // Bring the database up to the latest migration on startup.
            //
            // Without this a freshly created container starts against an empty
            // database and every request fails on a missing table, which makes the
            // documented "docker compose up" flow unusable out of the box.
            //
            // Restricted to Development on purpose: applying migrations automatically
            // is convenient for a reviewer running the project, but in production
            // schema changes belong to a controlled deployment step, not to
            // application startup.
            if (app.Environment.IsDevelopment())
            {
                using var scope = app.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<DefaultContext>();
                context.Database.Migrate();
                Log.Information("Database migrations applied");
            }

            // -- API documentation ----------------------------------------------
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // -- Transport ------------------------------------------------------
            // HTTPS redirection is applied outside Development only. The compose
            // container publishes plain HTTP on 8080 and holds no developer
            // certificate, so redirecting there would bounce every request to a port
            // that refuses connections. In a real deployment TLS is terminated
            // upstream and this middleware enforces the upgrade.
            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseBasicHealthChecks();

            app.MapControllers();

            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
