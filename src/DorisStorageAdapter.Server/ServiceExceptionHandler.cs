using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Services.Contract.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server;

internal sealed class ServiceExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
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

        var problem = new ErrorProblemDetails
        {
            Title = se.Title,
            Detail = se.Message,
            Errors = se.Errors
        };

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
            DatasetIntegrityException => StatusCodes.Status500InternalServerError,
            DatasetStatusException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
}