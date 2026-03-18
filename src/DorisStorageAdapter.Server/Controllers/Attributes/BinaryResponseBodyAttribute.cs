using System;

namespace DorisStorageAdapter.Server.Controllers.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class BinaryResponseBodyAttribute(int statusCode, string contentType) : Attribute
{
    public string ContentType { get; } = contentType;
    public int StatusCode { get; } = statusCode;
}
