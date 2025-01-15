using DorisStorageAdapter.Configuration;
using DorisStorageAdapter.Controllers;
using DorisStorageAdapter.Controllers.Attributes;
using DorisStorageAdapter.Services;
using DorisStorageAdapter.Services.Exceptions;
using DorisStorageAdapter.Services.Lock;
using DorisStorageAdapter.Services.Storage;
using Invio.Extensions.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using NetDevPack.Security.Jwt.Core.Jwa;
using NetDevPack.Security.JwtExtensions;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptionsWithValidateOnStart<GeneralConfiguration>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations();

builder.Services.AddOptionsWithValidateOnStart<AuthorizationConfiguration>()
    .Bind(builder.Configuration.GetSection(AuthorizationConfiguration.ConfigurationSection))
    .ValidateDataAnnotations();

builder.Services.AddOptionsWithValidateOnStart<StorageConfiguration>()
    .Bind(builder.Configuration.GetSection(StorageConfiguration.ConfigurationSection))
    .ValidateDataAnnotations();

builder.Services.AddOptionsWithValidateOnStart<StorageLimitsDatasetVersionPayloadConfiguration>()
    .Bind(builder.Configuration.GetSection(StorageLimitsDatasetVersionPayloadConfiguration.ConfigurationSection))
    .ValidateDataAnnotations();

static void SetupJsonSerializer(JsonSerializerOptions options)
{
    options.Converters.Add(new JsonStringEnumConverter());
}

builder.Services.AddControllers().AddJsonOptions(options => SetupJsonSerializer(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options => SetupJsonSerializer(options.SerializerOptions));

// Map ApiExceptions to problem details response
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        var exception = ctx.HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        if (exception != null && exception is ApiException apiException)
        {
            ctx.ProblemDetails.Detail = apiException.Message;
            ctx.ProblemDetails.Status = apiException.StatusCode;
            ctx.HttpContext.Response.StatusCode = apiException.StatusCode;
        }
    };
});

builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.SupportNonNullableReferenceTypes();
    options.OperationFilter<BinaryRequestBodyFilter>();

    options.AddSecurityDefinition("Bearer", new()
    {
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Name = "JWT Authentication",
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme
    }
    );
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = JwtBearerDefaults.AuthenticationScheme
                }
            },
            []
        }
    });
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddMemoryCache(); // Needed for AddJwksManager()
    builder.Services.AddJwksManager(o =>
    {
        o.Jws = Algorithm.Create(AlgorithmType.ECDsa, JwtType.Jws);
    })
    .PersistKeysInMemory();
}

var authorizationConfiguration = builder.Configuration
    .GetSection(AuthorizationConfiguration.ConfigurationSection)
    .Get<AuthorizationConfiguration>()!;

var generalConfiguration = builder.Configuration.Get<GeneralConfiguration>()!;

// Set up JWT authentication/authorization
builder.Services
    .AddAuthentication()
    .AddJwtBearer(options =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Disable HTTPS requirement to enable using regular HTTP in development
            options.RequireHttpsMetadata = false;
        }
        else
        {
            // MUST be true in production for security reasons!
            options.RequireHttpsMetadata = true; 
        }
    
        // SetJwkOptions sets ValidIssuer and ValidAudience
        options.SetJwksOptions(
            new(
                jwksUri: authorizationConfiguration.JwksUri,
                audience: generalConfiguration.PublicUrl.ToString()
            ));
    
        // Limiting the valid algorithms to only the one used by Doris hardens security by
        // making algorithm confusion attacks impossible, but also means that it's harder
        // for SND to change the signing algorithm.
        options.TokenValidationParameters.ValidAlgorithms = ["ES256"];
        // Default clock skew is 5 minutes (!), set to zero
        options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
    })
    // This enables sending the bearer token as an URI query parameter.
    // We only include this to enable downloading files via browser, which
    // does not support setting HTTP headers.
    // The recommended way of setting the bearer token is via the Authorization header
    // when possible, and only use the URI query parameter when absolutely necessary.
    // See https://github.com/invio/Invio.Extensions.Authentication.JwtBearer.
    .AddJwtBearerQueryStringAuthentication();

// Set up CORS policys per endpoint
builder.Services.AddCors(options =>
{
    options.AddPolicy(nameof(FileController.StoreFile), policy =>
    {
        policy.WithOrigins(authorizationConfiguration.CorsAllowedOrigins);
        policy.WithHeaders(HeaderNames.Authorization, HeaderNames.ContentLength, HeaderNames.ContentType);
        policy.WithMethods(HttpMethods.Put);
    });
    options.AddPolicy(nameof(FileController.DeleteFile), policy =>
    {
        policy.WithOrigins(authorizationConfiguration.CorsAllowedOrigins);
        policy.WithHeaders(HeaderNames.Authorization);
        policy.WithMethods(HttpMethods.Delete);
    });
});

builder.Services.AddSingleton<ILockService, InProcessLockService>();
builder.Services.AddTransient<ServiceImplementation>();

// Setup storage service based on configuration
void SetupStorageService()
{
    var configSection = builder.Configuration.GetSection(StorageConfiguration.ConfigurationSection);
    var storageConfiguration = configSection.Get<StorageConfiguration>()!;
    string storageService = storageConfiguration.ActiveStorageService;

    var types = typeof(Program).Assembly.GetTypes()
        .Where(t =>
            t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IStorageServiceConfigurer<>) &&
                i.GenericTypeArguments[0].Name == storageService))
        .ToList();

    if (types.Count == 0)
    {
        throw new StorageServiceConfigurationException($"No implementation of '{storageService}' found.");
    }
    else if (types.Count > 1)
    {
        throw new StorageServiceConfigurationException($"Multiple implementations of '{storageService}' found.");
    }

    var configurer = Activator.CreateInstance(types[0]) as IStorageServiceConfigurerBase;
    configurer!.Configure(builder.Services, configSection.GetSection(storageService));
}

SetupStorageService();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseAuthentication();
// This enables a middleware that redacts bearer token coming in via URI query string.
// See https://github.com/invio/Invio.Extensions.Authentication.JwtBearer.
app.UseJwtBearerQueryString();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseJwksDiscovery();
}

app.Run();
