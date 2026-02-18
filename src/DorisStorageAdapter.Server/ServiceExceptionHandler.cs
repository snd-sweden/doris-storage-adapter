using DorisStorageAdapter.Services.Contract.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server;

public sealed class ServiceExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService = problemDetailsService;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not ServiceException se)
        {
            return false;
        }

        httpContext.Response.StatusCode = GetStatusCode(se);

        var problem = new ProblemDetails
        {
            Title = se.Title,
            Detail = se.Message
        };

        if (se.Errors.Count > 0)
        {
            problem.Extensions["errors"] = se.Errors;
        }

        // This writes using the registered ProblemDetails writers
        await _problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });

        return true; // handled
    }

    private static int GetStatusCode(ServiceException ex) =>
        ex switch
        {
            ConflictException => StatusCodes.Status409Conflict,
            DatasetInconsistentException => StatusCodes.Status500InternalServerError,
            DatasetStatusException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
}