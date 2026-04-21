using DorisStorageAdapter.Services.Contract.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace DorisStorageAdapter.Server.Controllers.Models.Responses;

public sealed class ErrorProblemDetails : ProblemDetails
{
    public required IReadOnlyList<ErrorItem> Errors { get; init; }
}
