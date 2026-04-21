using DorisStorageAdapter.Services.Contract.Exceptions;
using System.Collections.Generic;

namespace DorisStorageAdapter.Server.Controllers.Models.Responses;

public sealed record ConsistencyCheckResult(
    IReadOnlyList<ErrorItem> Errors);
