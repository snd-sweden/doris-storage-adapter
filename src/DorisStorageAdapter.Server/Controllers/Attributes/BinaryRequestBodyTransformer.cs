using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers.Attributes;

internal sealed class BinaryRequestBodyTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor cad)
        {
            return Task.CompletedTask;
        }

        var attribute = cad.MethodInfo
            .GetCustomAttributes<BinaryRequestBodyAttribute>(false)
            .FirstOrDefault();

        if (attribute == null)
        {
            return Task.CompletedTask;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Description = "Binary content",
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [attribute.ContentType] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "binary"
                    }
                }
            }
        };

        return Task.CompletedTask;
    }
}
