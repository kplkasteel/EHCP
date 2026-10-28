// FUTURE AUTH: retained for re-enablement; not compiled in the local-only build.
// using System.Security.Claims;
// using Ehcp.Infrastructure;
//
// namespace Ehcp.Api;
//
// public static class MailboxAuthorization
// {
//     public static bool CanAccess(ClaimsPrincipal user, string? mailboxId, CosmosSettings settings) =>
//         user.Identity?.IsAuthenticated == true && user.IsInRole("StandardUser")
//         && mailboxId is not null && settings.Mailboxes.TryGetValue(mailboxId, out var mailbox)
//         && user.IsInRole(mailbox.Role);
// }
//
