using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers.Attributes;

internal sealed class BinaryResponseBodyTransformer : IOpenApiOperationTransformer
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

        var attributes = cad.MethodInfo
            .GetCustomAttributes<BinaryResponseBodyAttribute>(false)
            .ToList();

        if (attributes.Count == 0)
        {
            return Task.CompletedTask;
        }

        operation.Responses ??= [];

        foreach (var attribute in attributes)
        {
            operation.Responses[attribute.StatusCode.ToString(CultureInfo.InvariantCulture)] = new OpenApiResponse
            {
                Description = "Binary content",
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
        }

        return Task.CompletedTask;
    }
}
