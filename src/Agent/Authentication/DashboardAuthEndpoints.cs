using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Agent.Authentication;

public static class DashboardAuthEndpoints
{
    public static IEndpointRouteBuilder MapDashboardAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/login",
            (HttpContext context, string? returnUrl, IDashboardPasswordVerifier verifier) =>
                Results.Content(
                    RenderLoginPage(returnUrl, null, !verifier.IsConfigured),
                    "text/html; charset=utf-8"));

        endpoints.MapPost(
            "/login",
            async (HttpContext context, IDashboardPasswordVerifier verifier) =>
            {
                var form = await context.Request.ReadFormAsync();
                var password = form["password"].ToString();
                var returnUrl = form["returnUrl"].ToString();

                if (!verifier.IsConfigured)
                {
                    return Results.Content(
                        RenderLoginPage(returnUrl, "Dashboard password is not configured.", true),
                        "text/html; charset=utf-8",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (!verifier.Verify(password))
                {
                    return Results.Content(
                        RenderLoginPage(returnUrl, "Incorrect password.", false),
                        "text/html; charset=utf-8",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "local-owner"),
                    new Claim(ClaimTypes.Name, "Local owner")
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                var properties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                };

                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);

                return Results.Redirect(GetSafeReturnUrl(returnUrl));
            });

        endpoints.MapPost(
            "/logout",
            async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                return Results.Redirect("/login");
            });

        endpoints.MapGet(
            "/logout",
            async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                return Results.Redirect("/login");
            });

        return endpoints;
    }

    public static bool AllowsAnonymousAccess(PathString path)
    {
        return path.StartsWithSegments("/login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/logout", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderLoginPage(string? returnUrl, string? error, bool isConfigurationMissing)
    {
        var safeReturnUrl = WebUtility.HtmlEncode(GetSafeReturnUrl(returnUrl));
        var errorMarkup = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"""<p class="error">{WebUtility.HtmlEncode(error)}</p>""";
        var disabled = isConfigurationMissing ? " disabled" : string.Empty;

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>MainAgent Login</title>
              <style>
                :root { color-scheme: dark; font-family: Inter, "Segoe UI", system-ui, sans-serif; }
                * { box-sizing: border-box; }
                body { min-height: 100vh; margin: 0; display: grid; place-items: center; background: #0c0e12; color: #e2e2e8; padding: 20px; }
                main { width: min(360px, 100%); border: 1px solid #404753; background: #111317; padding: 20px; }
                h1 { margin: 0 0 8px; font-size: 22px; font-weight: 700; letter-spacing: 0; }
                p { margin: 0 0 18px; color: #c0c7d5; line-height: 1.45; }
                label { display: grid; gap: 7px; color: #c0c7d5; font-size: 13px; }
                input { width: 100%; min-height: 38px; border: 1px solid #404753; background: #1a1c20; color: #e2e2e8; padding: 8px 10px; font: inherit; }
                button { width: 100%; min-height: 38px; margin-top: 14px; border: 1px solid #2e90fa; background: rgba(46, 144, 250, 0.16); color: #e2e2e8; font: inherit; cursor: pointer; }
                button:disabled { cursor: not-allowed; opacity: 0.52; }
                .error { border: 1px solid rgba(255, 90, 102, 0.6); background: rgba(255, 90, 102, 0.1); color: #ffd6da; padding: 10px; }
                code { color: #e2e2e8; }
              </style>
            </head>
            <body>
              <main>
                <h1>MainAgent</h1>
                <p>Sign in to use the local dashboard.</p>
                {{errorMarkup}}
                <form method="post" action="/login">
                  <input type="hidden" name="returnUrl" value="{{safeReturnUrl}}">
                  <label>
                    Password
                    <input name="password" type="password" autocomplete="current-password" autofocus{{disabled}}>
                  </label>
                  <button type="submit"{{disabled}}>Sign in</button>
                </form>
              </main>
            </body>
            </html>
            """;
    }

    private static string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !returnUrl.StartsWith("/", StringComparison.Ordinal)
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.Contains('\\', StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }
}
