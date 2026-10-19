using CollectiveNarratives.Web.Hubs;
using CollectiveNarratives.Web.Sessions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QRCoder;

namespace CollectiveNarratives.Web;

public static class WebApp
{
    // Een beeld van 640×360 in JPEG is ongeveer 30–60 KB.
    public const int MaxFrameBytes = 300 * 1024;

    // configure: voor de tests (bv. een nep-klok of andere instellingen).
    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISessionEvents, HubSessionEvents>();
        services.AddSingleton<SessionStore>();
        services.AddSingleton<JoinGuard>();
        services.AddSingleton<PublicUrl>();
        services.AddSingleton<GameKey>();
        services.AddHostedService<SessionCleanupService>();
        services.AddSignalR(o => o.AddFilter<SessionExceptionFilter>());

        // Achter Caddy: https en het echte IP van de bezoeker (voor JoinGuard) komen uit de X-Forwarded-headers.
        // Alle proxies vertrouwen mag, want de container is alleen via Caddy bereikbaar (geen poort open naar buiten).
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        var app = builder.Build();
        app.Services.GetRequiredService<GameKey>(); // meteen stoppen als GAME_KEY ontbreekt

        app.UseForwardedHeaders();
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // Altijd even checken of er een nieuwe versie is (ETag), zodat een deploy meteen zichtbaar is.
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
        });

        MapPages(app);
        MapApi(app);
        app.MapHub<SessionHub>("/hubs/session");
        return app;
    }

    static void MapPages(WebApplication app)
    {
        IResult Page(string file) =>
            Results.File(Path.Combine(app.Environment.WebRootPath, file), "text/html; charset=utf-8");

        app.MapGet("/poll/{code}", (string code) => Page("poll.html"));
        app.MapGet("/screen", () => Page("screen.html"));
        app.MapGet("/screen/{code}", (string code) => Page("screen.html"));
        app.MapGet("/host", () => Page("host.html"));
    }

    static void MapApi(WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Text("OK"));

        // Bestaat deze code? Voor de code-invulpagina's (/ en /screen).
        app.MapGet("/api/sessions/{code}", (string code, HttpContext http, SessionStore store, JoinGuard guard) =>
        {
            string ip = http.Connection.RemoteIpAddress?.ToString() ?? "?";
            if (guard.IsBlocked(ip)) return Results.Json(new { status = JoinErrors.RateLimited }, statusCode: 429);
            if (!JoinGuard.IsValidCode(code))
            {
                guard.RecordFailure(ip);
                return Results.Json(new { status = JoinErrors.Invalid }, statusCode: 400);
            }
            switch (store.Status(code))
            {
                case CodeStatus.Active:
                    return Results.Json(new { status = "active" });
                case CodeStatus.Ended:
                    return Results.Json(new { status = JoinErrors.Ended }, statusCode: 410);
                default:
                    guard.RecordFailure(ip);
                    return Results.Json(new { status = JoinErrors.Unknown }, statusCode: 404);
            }
        });

        // QR naar de telefoonpagina van deze sessie.
        app.MapGet("/api/qr/{code}.png", (string code, HttpContext http, PublicUrl publicUrl) =>
        {
            if (!JoinGuard.IsValidCode(code)) return Results.BadRequest();
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode($"{publicUrl.For(http)}/poll/{code}", QRCodeGenerator.ECCLevel.M);
            return Results.File(new PngByteQRCode(data).GetGraphic(20), "image/png");
        });

        // Zicht van de speler (fase 5a): de game POST een JPEG met zijn gameToken, het grote scherm haalt het nieuwste op.
        app.MapPost("/api/sessions/{code}/frame", async (string code, HttpRequest request, SessionStore store) =>
        {
            if (!store.TryGet(code, out var session)) return Results.NotFound();
            if (!session.IsGameToken(request.Headers["X-Game-Token"].ToString())) return Results.Unauthorized();
            if (request.ContentLength is not { } length || length <= 0) return Results.StatusCode(411);
            if (length > MaxFrameBytes) return Results.StatusCode(413);

            var jpeg = new byte[length];
            await request.Body.ReadExactlyAsync(jpeg);
            if (jpeg.Length < 3 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[2] != 0xFF) return Results.BadRequest(); // geen JPEG
            return session.SetFrame(jpeg) ? Results.NoContent() : Results.StatusCode(429);
        });

        app.MapGet("/api/sessions/{code}/frame.jpg", (string code, HttpContext http, SessionStore store, JoinGuard guard) =>
        {
            string ip = http.Connection.RemoteIpAddress?.ToString() ?? "?";
            if (!store.TryGet(code, out var session))
            {
                guard.RecordFailure(ip);
                return Results.NotFound();
            }
            http.Response.Headers.CacheControl = "no-store";
            return session.GetFrame() is { } jpeg ? Results.File(jpeg, "image/jpeg") : Results.NoContent();
        });

        // Ingebouwde keuzes, voor de hostpagina (nep-game).
        app.MapGet("/api/choices", () => Choices.All.Select(c => new { id = c.Key, question = c.Value.Question, options = c.Value.Options }));
    }
}
