using System.Text.Json.Serialization;
using Ehcp.Api;
using Ehcp.Core;
using Ehcp.Infrastructure;

// using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

// using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CosmosSettings cosmos = builder.Configuration.GetSection("Cosmos").Get<CosmosSettings>() ?? new CosmosSettings();
if (!cosmos.IsValid())
    throw new InvalidOperationException(
        "Configure Cosmos endpoint, key, database and distinct mailbox containers. For local development, set Cosmos__Key to the key from the running emulator.");
builder.Services.AddSingleton(cosmos);
builder.Services.AddSingleton(_ => new CosmosClient(cosmos.Endpoint, cosmos.Key, new CosmosClientOptions
{
    ConnectionMode = ConnectionMode.Gateway,
    UseSystemTextJsonSerializerWithOptions = CosmosDocumentStore<EmailDocument>.JsonOptions
}));
builder.Services.AddSingleton<IDocumentStore<EmailDocument>, CosmosDocumentStore<EmailDocument>>();
builder.Services.AddSingleton<IDocumentStore<CaseDocument>, CosmosDocumentStore<CaseDocument>>();

// FUTURE AUTH: deliberately disabled for local development. Restore this block
// together with middleware, controller attributes, package reference and OpenAPI security.
// var localJwt = builder.Configuration["Authentication:Mode"] == "LocalJwt";
// if (localJwt && !builder.Environment.IsDevelopment())
//     throw new InvalidOperationException("LocalJwt is restricted to Development.");
// var tenant = builder.Configuration["Entra:TenantId"];
// var audience = builder.Configuration["Entra:Audience"];
// if (!localJwt && (!Guid.TryParse(tenant, out _) || string.IsNullOrWhiteSpace(audience)))
//     throw new InvalidOperationException("Configure Entra TenantId and Audience.");
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
// {
//     options.MapInboundClaims = false;
//     if (!localJwt)
//     {
//         options.Authority = $"https://login.microsoftonline.com/{tenant}/v2.0";
//         options.Audience = audience;
//     }
//     options.TokenValidationParameters.RoleClaimType = "roles";
//     options.TokenValidationParameters.NameClaimType = "name";
//     options.TokenValidationParameters.ValidateIssuer = true;
//     options.TokenValidationParameters.ValidateAudience = true;
//     options.TokenValidationParameters.ValidateLifetime = true;
//     options.TokenValidationParameters.ValidateIssuerSigningKey = true;
//     options.Events = new JwtBearerEvents
//     {
//         OnChallenge = async context =>
//         {
//             context.HandleResponse();
//             context.Response.StatusCode = 401;
//             context.Response.Headers.WWWAuthenticate = "Bearer";
//             await context.Response.WriteAsJsonAsync(new ApiError("UNAUTHORISED", "A valid bearer token is required."));
//         },
//         OnForbidden = async context =>
//         {
//             context.Response.StatusCode = 403;
//             await context.Response.WriteAsJsonAsync(new ApiError("FORBIDDEN", "Access to this operation or mailbox is not permitted."));
//         }
//     };
// });
// builder.Services.AddAuthorization(options =>
// {
//     options.AddPolicy("Mailbox", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
//         context.Resource is HttpContext http && MailboxAuthorization.CanAccess(context.User,
//             http.Request.RouteValues["mailboxId"]?.ToString(), cosmos)));
//     options.AddPolicy("Writer", policy => policy.RequireRole("DataWriter"));
// });

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
        //         FUTURE AUTH: no bearer requirement in the local OpenAPI contract.
        //         document.Components ??= new();
        //         document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        //         document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        //         { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        //             Description = "Entra access token: StandardUser plus configured mailbox role; writes also require DataWriter." };
        //         document.Security = [new OpenApiSecurityRequirement
        //         { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
        return Task.CompletedTask;
    });
});

WebApplication app = builder.Build();
app.UseExceptionHandler();
// app.UseAuthentication();
// app.UseAuthorization();
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

if (cosmos.Initialize)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Provision production containers through deployment tooling.");
    await CosmosInitializer.InitializeAsync(app.Services.GetRequiredService<CosmosClient>(), cosmos);
}

app.Run();

public partial class Program;