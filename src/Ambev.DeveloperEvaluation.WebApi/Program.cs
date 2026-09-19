using Ambev.DeveloperEvaluation.Application;
using Ambev.DeveloperEvaluation.Common.Caching;
using Ambev.DeveloperEvaluation.Common.HealthChecks;
using Ambev.DeveloperEvaluation.Common.Logging;
using Ambev.DeveloperEvaluation.Common.Security;
using Ambev.DeveloperEvaluation.Common.Validation;
using Ambev.DeveloperEvaluation.IoC;
using Ambev.DeveloperEvaluation.ORM;
using Ambev.DeveloperEvaluation.WebApi.Common;
using Ambev.DeveloperEvaluation.WebApi.Middleware;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Reflection;
using System.Text.Json.Serialization;

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

            builder.Services
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    // Serialize and accept enums by name, not by ordinal.
                    //
                    // .doc/users-api.md specifies them as names - "status": "Active",
                    // "role": "Customer" - but System.Text.Json defaults to integers,
                    // so the API neither produced nor accepted the documented form: a
                    // request sending "Active" failed model binding with a 400 before
                    // reaching any handler.
                    //
                    // Names are also the better contract. An ordinal silently changes
                    // meaning if someone inserts a value into the middle of an enum.
                    options.JsonSerializerOptions.Converters.Add(
                        new JsonStringEnumConverter());
                });

            // Route model-binding failures through the documented error contract.
            //
            // [ApiController] short-circuits an invalid ModelState before the action
            // runs and answers with RFC 7807 ProblemDetails, whose "type" is a
            // specification URL. That is a different shape from the
            // { type, error, detail } body every other failure uses, so a client had
            // to understand two error formats depending on whether the request failed
            // at binding or at validation.
            builder.Services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var detail = string.Join(" ", context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .SelectMany(entry => entry.Value!.Errors)
                        .Select(error => error.ErrorMessage)
                        .Where(message => !string.IsNullOrWhiteSpace(message)));

                    return new BadRequestObjectResult(new ApiErrorResponse
                    {
                        Type = "ValidationError",
                        Error = "Invalid input data",
                        Detail = string.IsNullOrWhiteSpace(detail)
                            ? "The request body could not be read."
                            : detail
                    });
                };
            });

            builder.Services.AddEndpointsApiExplorer();

            builder.AddBasicHealthChecks();

            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "DeveloperStore Sales API",
                    Version = "v1",
                    Description =
                        "Sales records for the DeveloperStore team.\n\n" +
                        "Customers, branches and products are referenced with the External Identities " +
                        "pattern: each carries its identifier in the owning domain plus a denormalized " +
                        "description captured at the time of sale.\n\n" +
                        "Quantity discounts: 1-3 items none, 4-9 items 10%, 10-20 items 20%. " +
                        "More than 20 identical items cannot be sold."
                });

                // Feeds the XML documentation comments into Swagger UI so the endpoint
                // descriptions, parameter notes and response codes documented on the
                // controllers are what a reader actually sees.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

                if (File.Exists(xmlPath))
                    options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            });

            builder.Services.AddDbContext<DefaultContext>(options =>
                options.UseNpgsql(
                    builder.Configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("Ambev.DeveloperEvaluation.ORM")
                )
            );

            builder.Services.AddJwtAuthentication(builder.Configuration);

            // Registers Redis when Cache:Enabled is set, and a no-op cache otherwise,
            // so the application runs identically with or without Redis available.
            builder.AddReadModelCache();

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
