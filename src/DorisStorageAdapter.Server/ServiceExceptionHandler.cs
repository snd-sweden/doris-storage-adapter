using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Services.Contract.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
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

        if (httpContext.Response.HasStarted)
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

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
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