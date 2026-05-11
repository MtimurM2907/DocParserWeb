using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DocParseLab.Server.Swagger;

/// <summary>
/// Fix Swashbuckle schema for IFormFile [FromForm] uploads.
/// </summary>
public sealed class FileUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasFile = context.ApiDescription.ParameterDescriptions
            .Any(p => p.Type == typeof(IFormFile));

        if (!hasFile) return;

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content =
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties =
                        {
                            ["file"] = new OpenApiSchema { Type = "string", Format = "binary" }
                        },
                        Required = new HashSet<string> { "file" }
                    }
                }
            }
        };
    }
}

