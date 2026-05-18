using DorisStorageAdapter.Services.Contract.Models;
using System;

namespace DorisStorageAdapter.Server.Controllers.Models.Requests;

public sealed record PublishRequest(
    AccessRight AccessRight,
    string CanonicalDoi,
    string Doi,
    DateTime PublishedDate);