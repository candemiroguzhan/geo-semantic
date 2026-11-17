# Development

Use Windows PowerShell from the directory containing geo-semantic.csproj and geo-semantic.sln.

~~~powershell
dotnet --version
dotnet restore geo-semantic.sln
dotnet build geo-semantic.sln --no-restore
dotnet test geo-semantic.sln --no-restore
dotnet run --project .\geo-semantic.csproj --launch-profile http
~~~

Expected SDK: 10.0.401. Visit http://localhost:5287/explorer. The root route redirects to the workspace. /counter and /weather preserve existing template functionality.

Package versions are pinned centrally in Directory.Packages.props. Nullable/implicit usings and framework defaults are in Directory.Build.props. The root web project excludes nested src/tests from its default compile/content globs, avoiding duplicate compilation.

MapLibre 5.24.0 runtime/CSS/license live under wwwroot/lib/maplibre-gl. General UI uses MudBlazor; do not put GIS business logic or external-service calls in Razor. Use Application contracts for new features and Infrastructure implementations for I/O.

## Tests

Three xUnit projects cover domain transitions/identity/scores, text generation/search validation/indexing fakes, and parsing/geometry/vector mapping/embedding and option validation. Normal tests do not need external services.

Explicit live verification:

~~~powershell
$env:GEOSEMANTIC_LIVE_TESTS = '1'
dotnet test .\tests\geo-semantic.Infrastructure.Tests\geo-semantic.Infrastructure.Tests.csproj --filter 'Category=Integration' --logger 'console;verbosity=normal'
Remove-Item Env:GEOSEMANTIC_LIVE_TESTS
~~~

The integration test uses the existing Ollama and Qdrant. It creates a uniquely named test collection and a workspace-owned artifacts directory, then cleans up its own records and collection. It checks repeat indexing count and actual bilingual retrieval.

Browser verification uses Playwright .NET with the existing Microsoft Edge browser; no Node installation or new browser download is required. Start the app with fresh isolated storage:

~~~powershell
$env:Storage__DataDirectory = 'artifacts/browser-data'
dotnet run --project .\geo-semantic.csproj --no-launch-profile --urls http://localhost:5198
~~~

In a second shell:

~~~powershell
$env:GEOSEMANTIC_BROWSER_TESTS = '1'
dotnet test .\tests\geo-semantic.Browser.Tests\geo-semantic.Browser.Tests.csproj --filter 'Category=Browser'
Remove-Item Env:GEOSEMANTIC_BROWSER_TESTS
~~~

Optional GEOSEMANTIC_BROWSER_URL overrides the default http://localhost:5198/explorer. The test assumes a fresh data directory and available live services, imports the fictional sample, indexes, searches, selects, downloads and cleans up through the UI.

## Local security

Default launch profiles bind to localhost. No authentication is included by design. Keep data outside wwwroot and do not bind this trusted-user MVP publicly. Environment settings use Section__Property syntax. Do not commit secrets or AppData. A remote deployment needs authentication/authorization and appropriate data isolation.

## Operational limits

50 MB / 10,000 features per upload by default. The server parses a bounded full dataset; browser map state is capped at three layers. This is intended for thousands, not millions, of features. Re-indexing is destructive for the dataset's previous index and jobs are in-process. Configuration/model changes require explicit re-indexing. No Docker Compose is required.

