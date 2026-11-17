using Microsoft.Playwright;

namespace GeoSemantic.Browser.Tests;

public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute() {
        if (Environment.GetEnvironmentVariable("GEOSEMANTIC_BROWSER_TESTS") != "1")
            Skip = "Set GEOSEMANTIC_BROWSER_TESTS=1 and start the application to run browser verification.";
    }
}
public sealed class BrowserTests(Xunit.Abstractions.ITestOutputHelper outputLog)
{
    [BrowserFact, Trait("Category", "Browser")]
    public async Task MixedGeometriesRemainUsableWithOfflineBasemap()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1600, Height = 1000 } });
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("https://tiles.openfreemap.org/**", route => route.AbortAsync());
        await page.GotoAsync(Environment.GetEnvironmentVariable("GEOSEMANTIC_BROWSER_URL") ?? "http://localhost:5198/explorer");
        await page.Locator(".geo-map[data-map-ready=true]").WaitForAsync(new() { Timeout = 60000 });
        await Assertions.Expect(page.Locator(".map-warning")).ToContainTextAsync("Basemap unavailable");
        const string data = """
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{"name":"Fictional point"},"geometry":{"type":"Point","coordinates":[32.83,39.92]}},
          {"type":"Feature","properties":{"name":"Fictional multipoint"},"geometry":{"type":"MultiPoint","coordinates":[[32.84,39.92],[32.84,39.93]]}},
          {"type":"Feature","properties":{"name":"Fictional line"},"geometry":{"type":"LineString","coordinates":[[32.85,39.92],[32.85,39.94]]}},
          {"type":"Feature","properties":{"name":"Fictional multiline"},"geometry":{"type":"MultiLineString","coordinates":[[[32.86,39.92],[32.87,39.94]],[[32.87,39.92],[32.86,39.94]]]}},
          {"type":"Feature","properties":{"name":"Fictional polygon"},"geometry":{"type":"Polygon","coordinates":[[[32.83,39.94],[32.84,39.94],[32.84,39.95],[32.83,39.94]]]}},
          {"type":"Feature","properties":{"name":"Fictional multipolygon"},"geometry":{"type":"MultiPolygon","coordinates":[[[[32.85,39.95],[32.86,39.95],[32.86,39.96],[32.85,39.95]]],[[[32.87,39.95],[32.88,39.95],[32.88,39.96],[32.87,39.95]]]]}}
        ]}
        """;
        await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload {
            Name = "mixed-geometries.geojson", MimeType = "application/geo+json", Buffer = System.Text.Encoding.UTF8.GetBytes(data)
        });
        var card = page.Locator(".dataset-card");
        await Assertions.Expect(card).ToContainTextAsync("6 features");
        await page.Locator(".geo-map[data-map-idle=true]").WaitForAsync(new() { Timeout = 30000 });
        var checkbox = card.GetByRole(AriaRole.Checkbox, new() { Name = "Visible" });
        await checkbox.UncheckAsync();
        await Assertions.Expect(checkbox).Not.ToBeCheckedAsync();
        await checkbox.CheckAsync();
        await Assertions.Expect(checkbox).ToBeCheckedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Inspector", Exact = true }).ClickAsync();
        var root = FindRoot();
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(root, "artifacts", "mixed-geometries.png") });
        Assert.Empty(errors);
        await card.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete permanently", Exact = true }).ClickAsync();
        await Assertions.Expect(card).ToHaveCountAsync(0);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "geo-semantic.csproj"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [BrowserFact, Trait("Category", "Browser")]
    public async Task WorkbenchImportIndexSearchSelectionAndExport()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "geo-semantic.csproj"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        var errors = new List<string>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() {
            Channel = "msedge", Headless = true, Args = ["--enable-webgl"]
        });
        outputLog.WriteLine("Edge " + browser.Version);
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1600, Height = 1000 }, AcceptDownloads = true });
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(Environment.GetEnvironmentVariable("GEOSEMANTIC_BROWSER_URL") ?? "http://localhost:5198/explorer");
        await page.Locator(".geo-map[data-map-ready=true]").WaitForAsync(new() { Timeout = 60000 });
        outputLog.WriteLine("Map initialized");
        var output = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(output);
        await page.Locator(".service-status").GetByText("Ready", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 60000 });
        if (await page.Locator(".dataset-card").CountAsync() == 0)
            await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(root, "samples", "sample-pois.geojson"));
        await page.Locator(".dataset-card").First.WaitForAsync();
        await Assertions.Expect(page.Locator(".dataset-card").First).ToContainTextAsync("30 features");
        var build = page.GetByRole(AriaRole.Button, new() { Name = "Build semantic index", Exact = true });
        if (await build.CountAsync() == 0) build = page.GetByRole(AriaRole.Button, new() { Name = "Rebuild semantic index", Exact = true });
        await Assertions.Expect(build).ToBeEnabledAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "before-index.png") });
        try { await build.ClickAsync(); }
        catch {
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, "browser-failure.png") });
            await File.WriteAllTextAsync(Path.Combine(output, "browser-failure.html"), await page.ContentAsync());
            await File.WriteAllLinesAsync(Path.Combine(output, "browser-errors.txt"), errors);
            throw;
        }
        await page.GetByRole(AriaRole.Button, new() { Name = "Rebuild semantic index", Exact = true }).WaitForAsync(new() { Timeout = 480000 });
        await Assertions.Expect(page.Locator(".progress-block")).ToContainTextAsync("Indexed", new() { Timeout = 480000 });
        await Assertions.Expect(page.Locator(".dataset-card .status")).ToHaveTextAsync("Indexed", new() { Timeout = 480000 });
        outputLog.WriteLine("Dataset indexed");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Natural language search" }).FillAsync("Find green recreational spaces for walking.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await page.Locator(".result-item").First.WaitForAsync(new() { Timeout = 180000 });
        await Assertions.Expect(page.Locator(".result-item").First).ToContainTextAsync("Park");
        await page.Locator(".result-item").First.ClickAsync();
        await Assertions.Expect(page.Locator(".feature-title")).ToContainTextAsync("Kurgusal");
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Export GeoJSON ↓" }).ClickAsync());
        Assert.Equal("geosemantic-results.geojson", download.SuggestedFilename);
        await download.SaveAsAsync(Path.Combine(output, "browser-results.geojson"));
        await page.Locator(".geo-map[data-map-idle=true]").WaitForAsync(new() { Timeout = 60000 });
        var canvas = page.Locator(".maplibregl-canvas");
        var box = await canvas.BoundingBoxAsync() ?? throw new InvalidOperationException("Map canvas not visible.");
        await canvas.ClickAsync(new() { Position = new() { X = box.Width / 2, Y = box.Height / 2 } });
        await Assertions.Expect(page.Locator(".maplibregl-popup")).ToContainTextAsync("Kurgusal");
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "explorer.png"), FullPage = true });
        Assert.Empty(errors);
        // Clean this test's own dataset through the same application flow.
        await page.Locator(".dataset-card").First.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete permanently", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".dataset-card")).ToHaveCountAsync(0);
    }
}
