using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FabGames.Web.Tests;

public sealed class PageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PageTests(WebApplicationFactory<Program> factory) =>
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpResponseMessage> Get(string url, string? acceptLanguage = null, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (acceptLanguage is not null) request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        if (cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return await _client.SendAsync(request);
    }

    // ── Culture routing ───────────────────────────────────────

    [Theory]
    [InlineData("en-US,en;q=0.9", "/en")]
    [InlineData("es-AR,es;q=0.9,en;q=0.8", "/es")]
    [InlineData("fr-FR,fr;q=0.9,en;q=0.5", "/en")]
    [InlineData("de-DE", "/es")]
    [InlineData(null, "/es")]
    public async Task Root_RedirectsByAcceptLanguage_FallingBackToSpanish(string? header, string expected)
    {
        var response = await Get("/", header);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(expected);
    }

    [Fact]
    public async Task Root_PrefersTheSavedLanguageChoice()
    {
        var response = await Get("/", "es-ES", "fab.culture=en");
        response.Headers.Location!.OriginalString.Should().Be("/en");
    }

    [Fact]
    public async Task Pages_AreLocalizedFromTheUrl()
    {
        var es = WebUtility.HtmlDecode(await (await Get("/es")).Content.ReadAsStringAsync());
        var en = WebUtility.HtmlDecode(await (await Get("/en")).Content.ReadAsStringAsync());

        es.Should().Contain("<html lang=\"es\"").And.Contain(">Crear sala<").And.Contain("Unirse con código");
        en.Should().Contain("<html lang=\"en\"").And.Contain(">Create room<").And.Contain("Join with code");
        es.Should().NotContain(">home.btn.create<", "an untranslated key would show up as raw text");
        en.Should().NotContain(">home.btn.create<");
    }

    [Fact]
    public async Task ClientStrings_ArePassedAsADataBlock_InBothLanguages()
    {
        foreach (var (culture, expected) in new[] { ("es", "Tu turno"), ("en", "Your turn") })
        {
            var html = await (await Get($"/{culture}")).Content.ReadAsStringAsync();
            var block = Regex.Match(html, "<script type=\"application/json\" id=\"i18n\">(.*?)</script>", RegexOptions.Singleline);
            block.Success.Should().BeTrue();
            block.Groups[1].Value.Should().Contain($"\"game.turn.yours\":\"{expected}\"");
        }
    }

    [Fact]
    public async Task UnsupportedCulture_IsNotRouted()
    {
        (await Get("/fr")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Locale switch ─────────────────────────────────────────

    [Fact]
    public async Task LocaleSwitch_KeepsThePage_AndRemembersTheChoice()
    {
        var response = await Get("/locale/en?returnUrl=%2Fes%2Fplay%2FABC234%3Fx%3D1");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/en/play/ABC234?x=1");
        response.Headers.GetValues("Set-Cookie").Should().ContainSingle(c => c.StartsWith("fab.culture=en", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData("https://evil.example/")]
    [InlineData("/\\evil.example")]
    [InlineData("/elsewhere/page")]
    public async Task LocaleSwitch_NeverRedirectsOffSite(string returnUrl)
    {
        var response = await Get($"/locale/en?returnUrl={Uri.EscapeDataString(returnUrl)}");
        response.Headers.Location!.OriginalString.Should().Be("/en");
    }

    // ── Room pages ────────────────────────────────────────────

    [Theory]
    [InlineData("/es/play/ABC234", "ABC234", "false")]
    [InlineData("/es/play/abc234", "ABC234", "false")]
    [InlineData("/en/play/new", "new", "false")]
    [InlineData("/en/play/OOOOOO", "", "true")]
    [InlineData("/en/play/x", "", "true")]
    public async Task RoomPage_NormalizesTheCode_AndFlagsInvalidOnes(string url, string code, string invalid)
    {
        var response = await Get(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"data-room-code=\"{code}\"").And.Contain($"data-invalid=\"{invalid}\"");
    }

    // ── Security and hygiene ──────────────────────────────────

    [Theory]
    [InlineData("/es")]
    [InlineData("/en/play/ABC234")]
    public async Task Pages_CarryAStrictContentSecurityPolicy(string url)
    {
        var response = await Get(url);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.Should().Contain("script-src 'self'").And.Contain("style-src 'self'")
            .And.Contain("frame-ancestors 'none'").And.NotContain("unsafe-inline").And.NotContain("unsafe-eval");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [Theory]
    [InlineData("/es")]
    [InlineData("/en/play/ABC234")]
    public async Task Pages_HaveNoInlineScriptsOrStyles(string url)
    {
        var html = await (await Get(url)).Content.ReadAsStringAsync();

        var inlineScripts = Regex.Matches(html, "<script(?<attrs>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline)
            .Where(m => !m.Groups["attrs"].Value.Contains("src=", StringComparison.Ordinal)
                        && !m.Groups["attrs"].Value.Contains("application/json", StringComparison.Ordinal));
        inlineScripts.Should().BeEmpty("the CSP forbids inline scripts");
        html.Should().NotContain("<style").And.NotMatchRegex("\\sstyle=\"");
        html.Should().NotMatchRegex("\\son(click|load|error|submit)=");
    }

    [Theory]
    [InlineData("/fonts/archivo-latin.woff2", "font/woff2")]
    [InlineData("/fonts/hanken-latin.woff2", "font/woff2")]
    [InlineData("/fonts/geistmono-latin.woff2", "font/woff2")]
    [InlineData("/img/brand/shield.webp", "image/webp")]
    [InlineData("/img/brand/wordmark.webp", "image/webp")]
    [InlineData("/img/covers/chess.webp", "image/webp")]
    [InlineData("/img/covers/checkers.webp", "image/webp")]
    [InlineData("/img/avatars/a01.svg", "image/svg+xml")]
    [InlineData("/img/avatars/a12.svg", "image/svg+xml")]
    [InlineData("/img/icons.svg", "image/svg+xml")]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/lib/signalr/signalr.min.js", "text/javascript")]
    [InlineData("/js/home/index.js", "text/javascript")]
    [InlineData("/js/play/room.js", "text/javascript")]
    [InlineData("/css/core/base.css", "text/css")]
    public async Task StaticAssets_AreServedSelfHosted(string path, string contentType)
    {
        var response = await Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
    }

    [Fact]
    public async Task NoPage_ReferencesAThirdPartyHost()
    {
        foreach (var url in new[] { "/es", "/en", "/en/play/ABC234" })
        {
            var html = await (await Get(url)).Content.ReadAsStringAsync();
            Regex.Matches(html, "(?:src|href)=\"(https?:)?//[^\"]+\"").Should().BeEmpty(url);
        }
    }
}
