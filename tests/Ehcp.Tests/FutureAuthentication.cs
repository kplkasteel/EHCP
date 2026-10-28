// FUTURE AUTH: reference implementation and tests, deliberately not compiled.
//     [Fact] public async Task MissingTokenIsUnauthorized()
//     {
//         await using var factory = new ApiFactory();
//         var response = await factory.CreateClient().GetAsync("/api/mailboxes/ehcp-team-shared/emails/example");
//         Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
//     }
//     [Fact] public async Task WrongMailboxIsForbidden()
//     {
//         await using var factory = new ApiFactory();
//         using var client = factory.Client("StandardUser", "OtherMailbox");
//         Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mailboxes/ehcp-team-shared/emails/example")).StatusCode);
//     }
//     [Fact] public async Task ReadRoleCannotWrite()
//     {
//         await using var factory = new ApiFactory();
//         using var client = factory.Client("StandardUser", "Mailbox.EhcpTeam");
//         Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/mailboxes/ehcp-team-shared/emails", Example, CosmosDocumentStore<EmailDocument>.JsonOptions)).StatusCode);
//     }
//
// internal sealed class ApiFactory : WebApplicationFactory<Program>
// {
//     private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-only-signing-key-at-least-thirty-two-bytes-long"));
//     protected override void ConfigureWebHost(IWebHostBuilder builder)
//     {
//         builder.UseEnvironment("Development");
//         builder.UseSetting("Cosmos:Key", "test-not-used");
//         builder.UseSetting("Cosmos:Initialize", "false");
//         builder.UseSetting("Authentication:Mode", "LocalJwt");
//         builder.ConfigureServices(services =>
//         {
//             services.RemoveAll<IDocumentStore<EmailDocument>>();
//             services.AddSingleton<IDocumentStore<EmailDocument>, MemoryEmails>();
//             services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
//             {
//                 options.TokenValidationParameters.ValidIssuer = "test";
//                 options.TokenValidationParameters.ValidAudience = "test";
//                 options.TokenValidationParameters.IssuerSigningKey = Key;
//             });
//         });
//     }
//     public HttpClient Client(params string[] roles)
//     {
//         var client = CreateClient();
//         var token = new JwtSecurityToken("test", "test", roles.Select(r => new Claim("roles", r)),
//             expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
//         client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
//         return client;
//     }
// }
//
//
