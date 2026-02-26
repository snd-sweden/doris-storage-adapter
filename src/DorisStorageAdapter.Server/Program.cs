using DorisStorageAdapter.Server;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers;
using DorisStorageAdapter.Server.Controllers.Attributes;
using Invio.Extensions.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using NetDevPack.Security.Jwt.Core.Jwa;
using NetDevPack.Security.JwtExtensions;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

DorisStorageAdapter.Services.Bootstrapper.SetupServices(builder.Services, builder.Configuration);

builder.Services.AddOptionsWithValidateOnStart<GeneralConfiguration>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations();

builder.Services.AddOptionsWithValidateOnStart<AuthorizationConfiguration>()
    .Bind(builder.Configuration.GetSection(AuthorizationConfiguration.ConfigurationSection))
    .ValidateDataAnnotations();

static void SetupJsonSerializer(JsonSerializerOptions options)
{
    options.Converters.Add(new JsonStringEnumConverter());
}

builder.Services.AddControllers().AddJsonOptions(options => SetupJsonSerializer(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options => SetupJsonSerializer(options.SerializerOptions));

builder.Services.AddProblemDetails(o =>
{
    o.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance = ctx.HttpContext.Request.Path;

        ctx.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id
            ?? ctx.HttpContext.TraceIdentifier;
    };
});
// Map ServiceExceptions to problem details response
builder.Services.AddExceptionHandler<ServiceExceptionHandler>();

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
            options.IncludeErrorDetails = true;
            // Disable HTTPS requirement to enable using regular HTTP in development
            options.RequireHttpsMetadata = false;
        }
        else
        {
            options.IncludeErrorDetails = false;
            // MUST be true in production for security reasons!
            options.RequireHttpsMetadata = true; 
        }

        // SetJwkOptions sets ValidIssuer and ValidAudience
        options.SetJwksOptions(
            new(
                jwksUri: authorizationConfiguration.JwksUri.AbsoluteUri,
                audience: generalConfiguration.PublicUrl.AbsoluteUri
            ));
    
        // Limiting the valid algorithms to only the one used by Doris hardens security by
        // making algorithm confusion attacks impossible, but also means that it's harder
        // for SND to change the signing algorithm.
        options.TokenValidationParameters.ValidAlgorithms = ["ES256"];
        // Default clock skew is 5 minutes (!), set to zero
        options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
    })
    // This enables sending the bearer token as an URI query parameter.
    // Needs to be enabled to support downloading files via <a href> in a browser, which
    // does not support setting HTTP headers.
    // The recommended way of setting the bearer token is via the Authorization header
    // when possible, and only use the URI query parameter when absolutely necessary.
    // See https://github.com/invio/Invio.Extensions.Authentication.JwtBearer.
    .AddJwtBearerQueryStringAuthentication();

// Set up CORS policys per endpoint
builder.Services.AddCors(options =>
{
    options.AddPolicy(UploadsController.uploadCorsPolicyName, policy =>
    {
        policy
            .WithOrigins([.. authorizationConfiguration.CorsAllowedOrigins])
            .WithHeaders(
                HeaderNames.Authorization, 
                HeaderNames.ContentLength, 
                HeaderNames.ContentType)
            .WithMethods(HttpMethods.Put);
    });
    options.AddPolicy(UploadsController.deleteCorsPolicyName, policy =>
    {
        policy
            .WithOrigins([.. authorizationConfiguration.CorsAllowedOrigins])
            .WithHeaders(HeaderNames.Authorization)
            .WithMethods(HttpMethods.Delete);
    });
    options.AddPolicy(DownloadsController.downloadPublicFileCorsPolicyName, policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .WithMethods(HttpMethods.Get, HttpMethods.Head)
            .WithExposedHeaders(
                HeaderNames.AcceptRanges, 
                HeaderNames.ContentDisposition, 
                HeaderNames.ContentRange);
    });
    options.AddPolicy(DownloadsController.downloadPublicFilesAsZipCorsPolicyName, policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .WithMethods(HttpMethods.Get)
            .WithExposedHeaders(HeaderNames.ContentDisposition);
    });
});

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
