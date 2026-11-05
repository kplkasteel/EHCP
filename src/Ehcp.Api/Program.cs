using System.Text.Json.Serialization;
using Ehcp.Api;
using Ehcp.Core;
using Ehcp.Infrastructure;

using Microsoft.AspNetCore.Mvc;

using Microsoft.OpenApi;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddEhcpInfrastructure(builder.Configuration);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.JsonSerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory =
    _ => new BadRequestObjectResult(new ApiError("VALIDATION_ERROR", "The request body or parameters are invalid.")));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "EHCP Application API";
        document.Info.Version = "1.0.0";
        return Task.CompletedTask;
    });
});

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/", () => Results.Ok(new
{
    name = "EHCP API",
    status = "ok",
    openApi = app.Environment.IsDevelopment() ? "/openapi/v1.json" : null,
    documentation = app.Environment.IsDevelopment() ? "/scalar/v1" : null
}));
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.Title = "EHCP API");
}

await app.Services.InitializeEhcpInfrastructureAsync(app.Environment.IsDevelopment());

app.Run();

public partial class Program;