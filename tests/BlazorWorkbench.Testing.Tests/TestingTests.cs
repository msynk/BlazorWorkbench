using System.IO.Compression;
using System.Xml.Linq;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing;
using BlazorWorkbench.Testing.Imaging;

namespace BlazorWorkbench.Testing.Tests;

public class PngTests
{
    private static PngImage Gradient(int w, int h)
    {
        var image = new PngImage(w, h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var o = (y * w + x) * 4;
            image.Pixels[o] = (byte)(x * 255 / Math.Max(1, w - 1));
            image.Pixels[o + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
            image.Pixels[o + 2] = 128;
            image.Pixels[o + 3] = 255;
        }

        return image;
    }

    [Fact]
    public void Encode_decode_round_trip()
    {
        var image = Gradient(37, 19);
        var decoded = PngImage.Decode(image.Encode());
        Assert.Equal(37, decoded.Width);
        Assert.Equal(19, decoded.Height);
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Decodes_every_filter_type(byte filter)
    {
        // Build an RGB PNG by hand with the given filter so all reconstruction paths are exercised.
        const int w = 5, h = 4;
        var raw = new List<byte>();
        var previous = new byte[w * 3];
        var expected = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var row = new byte[w * 3];
            for (var i = 0; i < row.Length; i++)
                row[i] = (byte)((y * 31 + i * 17) % 256);
            for (var x = 0; x < w; x++)
            {
                expected[(y * w + x) * 4] = row[x * 3];
                expected[(y * w + x) * 4 + 1] = row[x * 3 + 1];
                expected[(y * w + x) * 4 + 2] = row[x * 3 + 2];
                expected[(y * w + x) * 4 + 3] = 255;
            }

            raw.Add(filter);
            for (var i = 0; i < row.Length; i++)
            {
                int a = i >= 3 ? row[i - 3] : 0, b = previous[i], c = i >= 3 ? previous[i - 3] : 0;
                var predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                raw.Add((byte)(row[i] - predictor));
            }

            previous = row;
        }

        var decoded = PngImage.Decode(BuildPng(w, h, 2, raw.ToArray()));
        Assert.Equal(expected, decoded.Pixels);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] BuildPng(int w, int h, byte colorType, byte[] raw)
    {
        // Reuse the encoder's chunk writer indirectly: encode a dummy image and splice our IHDR/IDAT.
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw);
        var chunks = new List<(string, byte[])>
        {
            ("IHDR", [.. BigEndian(w), .. BigEndian(h), 8, colorType, 0, 0, 0]),
            ("IDAT", compressed.ToArray()),
            ("IEND", []),
        };
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        foreach (var (type, data) in chunks)
        {
            output.Write(BigEndian(data.Length));
            output.Write(System.Text.Encoding.ASCII.GetBytes(type));
            output.Write(data);
            output.Write([0, 0, 0, 0]); // CRC is not verified by the decoder
        }

        return output.ToArray();
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    [Fact]
    public void Rejects_non_png_data() => Assert.Throws<InvalidDataException>(() => PngImage.Decode([1, 2, 3, 4, 5, 6, 7, 8, 9]));

    [Fact]
    public void Identical_images_have_no_difference()
    {
        var result = ImageComparer.Compare(Gradient(20, 20), Gradient(20, 20));
        Assert.Equal(0, result.DiffPixels);
        Assert.False(result.SizeMismatch);
    }

    [Fact]
    public void Detects_changed_pixels_and_ignores_noise_below_threshold()
    {
        var baseline = Gradient(20, 20);
        var actual = Gradient(20, 20);
        actual.Pixels[0] = (byte)(actual.Pixels[0] + 2); // imperceptible
        for (var i = 0; i < 4; i++)
        {
            var o = (10 * 20 + i) * 4;
            actual.Pixels[o] = 255;
            actual.Pixels[o + 1] = 0;
            actual.Pixels[o + 2] = 0;
        }

        var result = ImageComparer.Compare(baseline, actual, 0.1);
        Assert.Equal(4, result.DiffPixels);
        Assert.Equal(255, result.Diff.Pixels[(10 * 20) * 4]);
        Assert.Equal(0.01, result.DiffRatio, 3);
    }

    [Fact]
    public void Reports_size_mismatch()
    {
        var result = ImageComparer.Compare(new PngImage(10, 10), new PngImage(10, 12));
        Assert.True(result.SizeMismatch);
        Assert.Equal(20, result.DiffPixels);
    }
}

public class BaselineAndReportTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("wb-tests");

    public void Dispose() => _root.Delete(true);

    private TestRunReport ReportWithScreenshot(CheckStatus status)
    {
        var output = Path.Combine(_root.FullName, "out");
        new PngImage(4, 3).Save(Path.Combine(output, "screenshots", "c--s", "laptop-light.png"));
        var report = new TestRunReport
        {
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow.AddSeconds(2),
            Environment = new RunEnvironment { Browser = "chromium", BrowserVersion = "1.0" },
        };
        var scenario = new ScenarioResult { ScenarioId = "c--s", ComponentId = "c", Name = "C / S" };
        scenario.Checks.Add(new CheckResult
        {
            Kind = CheckKind.Visual,
            Name = "visual",
            Variant = "laptop-light",
            Status = status,
            Message = "No approved baseline",
            Visual = new VisualComparison { Variant = "laptop-light", Viewport = "laptop", Theme = "light", ActualPath = "screenshots/c--s/laptop-light.png", BaselinePath = "c--s/laptop-light.png" },
        });
        scenario.Checks.Add(new CheckResult { Kind = CheckKind.Interaction, Name = "clicks", Status = CheckStatus.Failed, Message = "Step 1 failed <&>", Steps = [new(0, "Click role=button", CheckStatus.Failed, "timeout", 5)] });
        scenario.Conclude();
        report.Scenarios.Add(scenario);
        report.Scenarios.Add(new ScenarioResult { ScenarioId = "c--skipped", ComponentId = "c", Status = CheckStatus.Skipped, Message = "skip" });
        report.Summarize();
        return report;
    }

    [Fact]
    public void Approve_copies_screenshots_and_writes_manifest()
    {
        var report = ReportWithScreenshot(CheckStatus.Missing);
        var store = new BaselineStore(Path.Combine(_root.FullName, "baselines"));

        var none = store.Approve(report, Path.Combine(_root.FullName, "out"), id => id == "other");
        Assert.Empty(none);

        var approved = Assert.Single(store.Approve(report, Path.Combine(_root.FullName, "out"), _ => true));
        Assert.Equal($"{BaselineStore.CurrentPlatform}/c--s/laptop-light.png", approved.File);
        Assert.NotNull(store.TryLoad("c--s", "laptop-light"));
        var manifest = store.LoadManifest();
        Assert.Equal("chromium", manifest.Entries[approved.File].Browser);
        Assert.Equal(64, manifest.Entries[approved.File].Sha256.Length);
    }

    [Fact]
    public void Approve_ignores_passed_checks_unless_requested()
    {
        var report = ReportWithScreenshot(CheckStatus.Passed);
        var store = new BaselineStore(Path.Combine(_root.FullName, "baselines"));
        Assert.Empty(store.Approve(report, Path.Combine(_root.FullName, "out"), _ => true));
        Assert.Single(store.Approve(report, Path.Combine(_root.FullName, "out"), _ => true, includePassed: true));
    }

    [Fact]
    public void Approve_stores_screenshots_under_the_platform_that_captured_them()
    {
        var source = ReportWithScreenshot(CheckStatus.Missing);
        var report = new TestRunReport { StartedAt = source.StartedAt, Environment = new RunEnvironment { Platform = "linux", Browser = "chromium" } };
        report.Scenarios.Add(source.Scenarios[0]);
        var approved = Assert.Single(new BaselineStore(Path.Combine(_root.FullName, "baselines"), "windows").Approve(report, Path.Combine(_root.FullName, "out"), _ => true));
        Assert.StartsWith("linux/", approved.File, StringComparison.Ordinal);
        Assert.NotNull(new BaselineStore(Path.Combine(_root.FullName, "baselines"), "linux").TryLoad("c--s", "laptop-light"));
    }

    [Fact]
    public void Prune_removes_baselines_of_deleted_scenarios()
    {
        var store = new BaselineStore(Path.Combine(_root.FullName, "baselines"));
        new PngImage(1, 1).Save(store.FullPath("gone--x", "laptop-light"));
        new PngImage(1, 1).Save(store.FullPath("kept--x", "laptop-light"));
        var removed = store.Prune(new HashSet<string> { "kept--x" });
        Assert.Equal([$"{BaselineStore.CurrentPlatform}/gone--x/laptop-light.png"], removed);
        Assert.NotNull(store.TryLoad("kept--x", "laptop-light"));
    }

    [Fact]
    public void Narrowed_runs_are_recorded_as_subsets()
    {
        Assert.False(new RunSettings().IsSubset);
        Assert.True(new RunSettings { Filter = "inputs-*" }.IsSubset);
        Assert.True(new RunSettings { Partial = true }.IsSubset);

        var dir = Path.Combine(_root.FullName, "partial");
        ReportWriter.WriteAll(new TestRunReport { Settings = new RunSettings { Partial = true } }, dir);
        Assert.True(TestRunReport.Load(Path.Combine(dir, "report.json"))!.Settings.IsSubset);
    }

    [Fact]
    public void Publish_refuses_unrelated_folders()
    {
        var docs = Path.Combine(_root.FullName, "docs");
        Directory.CreateDirectory(Path.Combine(docs, "components"));
        File.WriteAllText(Path.Combine(docs, "guide.md"), "keep");

        Assert.Throws<WorkbenchInfrastructureException>(() => CatalogPublisher.PrepareOutput(docs));
        Assert.True(File.Exists(Path.Combine(docs, "guide.md")));
        Assert.True(Directory.Exists(Path.Combine(docs, "components")));
    }

    [Fact]
    public void Republishing_replaces_only_catalog_files()
    {
        var output = Path.Combine(_root.FullName, "catalog");
        CatalogPublisher.PrepareOutput(output);
        File.WriteAllText(Path.Combine(output, "index.html"), "old");
        File.WriteAllText(Path.Combine(output, "components", "removed.html"), "old");
        File.WriteAllText(Path.Combine(output, "CNAME"), "catalog.example.com");

        CatalogPublisher.PrepareOutput(output);
        Assert.False(File.Exists(Path.Combine(output, "index.html")));
        Assert.False(File.Exists(Path.Combine(output, "components", "removed.html")));
        Assert.True(File.Exists(Path.Combine(output, "CNAME")));
        Assert.True(File.Exists(Path.Combine(output, CatalogPublisher.MarkerFile)));
    }

    [Fact]
    public void Summary_distinguishes_failures_skips_and_infrastructure_errors()
    {
        var report = ReportWithScreenshot(CheckStatus.Missing);
        report.InfrastructureErrors.Add("browser crashed");
        report.Summarize();
        Assert.Equal(2, report.Summary.Total);
        Assert.Equal(1, report.Summary.Failed);
        Assert.Equal(1, report.Summary.Skipped);
        Assert.Equal(1, report.Summary.Errors);
    }

    [Fact]
    public void Writes_valid_junit_html_and_json()
    {
        var report = ReportWithScreenshot(CheckStatus.Failed);
        var dir = Path.Combine(_root.FullName, "report");
        ReportWriter.WriteAll(report, dir);

        var junit = XDocument.Load(Path.Combine(dir, "junit.xml"));
        var cases = junit.Descendants("testcase").ToList();
        Assert.Equal(3, cases.Count);
        Assert.Equal(2, cases.Count(c => c.Element("failure") is not null));
        Assert.Single(cases, c => c.Element("skipped") is not null);

        var html = File.ReadAllText(Path.Combine(dir, "index.html"));
        Assert.Contains("Step 1 failed &lt;&amp;&gt;", html);
        Assert.DoesNotContain("<&>", html);

        var loaded = TestRunReport.Load(Path.Combine(dir, "report.json"))!;
        Assert.Equal(report.Scenarios.Count, loaded.Scenarios.Count);
        Assert.Equal(CheckStatus.Failed, loaded.Scenarios[0].Status);
    }

    [Fact]
    public void Scenario_conclusion_prioritizes_errors_then_failures()
    {
        var result = new ScenarioResult { ScenarioId = "x" };
        result.Checks.Add(new CheckResult { Kind = CheckKind.Render, Name = "r", Status = CheckStatus.Passed });
        result.Checks.Add(new CheckResult { Kind = CheckKind.Visual, Name = "v", Status = CheckStatus.Missing, Message = "missing" });
        result.Conclude();
        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal("missing", result.Message);

        result.Checks.Add(new CheckResult { Kind = CheckKind.Render, Name = "i", Status = CheckStatus.Error });
        result.Message = null;
        result.Conclude();
        Assert.Equal(CheckStatus.Error, result.Status);
    }

    [Fact]
    public async Task Host_start_reports_missing_project_as_infrastructure_error() =>
        await Assert.ThrowsAsync<WorkbenchInfrastructureException>(() => WorkbenchHost.StartAsync(Path.Combine(_root.FullName, "missing.csproj")));
}

public class RunnerSelectionTests
{
    private static CatalogDto Catalog() => new()
    {
        Components =
        [
            new ComponentDto
            {
                Id = "inputs-button", Title = "Inputs/Button", Name = "Button", FullName = "Button", AssemblyName = "A", Tags = ["inputs"],
                Scenarios =
                [
                    new ScenarioDto { Id = "inputs-button--primary", ComponentId = "inputs-button", Key = "primary", Name = "Primary" },
                    new ScenarioDto { Id = "inputs-button--danger", ComponentId = "inputs-button", Key = "danger", Name = "Danger", Tags = ["slow"] },
                ],
            },
            new ComponentDto
            {
                Id = "data-table", Title = "Data/Table", Name = "Table", FullName = "Table", AssemblyName = "A",
                Scenarios = [new ScenarioDto { Id = "data-table--empty", ComponentId = "data-table", Key = "empty", Name = "Empty" }],
            },
        ],
    };

    private static IReadOnlyList<string> Select(string? filter, IReadOnlySet<string>? only = null) =>
        new WorkbenchTestRunner(null!, new TestRunOptions { Filter = filter, OnlyScenarios = only }).Select(Catalog()).Select(x => x.Scenario.Id).ToList();

    [Fact]
    public void Selects_all_without_filter() => Assert.Equal(3, Select(null).Count);

    [Fact]
    public void Filters_by_glob_title_and_tag()
    {
        Assert.Equal(["inputs-button--primary", "inputs-button--danger"], Select("inputs-*"));
        Assert.Equal(["data-table--empty"], Select("Data/*"));
        Assert.Equal(["inputs-button--primary", "inputs-button--danger"], Select("tag:inputs"));
        Assert.Equal(["inputs-button--danger"], Select("tag:slow"));
        Assert.Equal(["inputs-button--primary", "data-table--empty"], Select("*--primary,*--empty"));
    }

    [Fact]
    public void Restricts_to_previous_failures() =>
        Assert.Equal(["data-table--empty"], Select(null, new HashSet<string> { "data-table--empty" }));
}

public class HostInfoTests
{
    [Fact]
    public void Health_of_a_hybrid_host_describes_the_WebView()
    {
        var info = WorkbenchHostInfo.Parse("""{"status":"ok","hostingModel":"Hybrid","version":"0.1.0","appOrigin":"https://0.0.0.1/","devToolsEndpoint":"http://127.0.0.1:9222/"}""");
        Assert.True(info.IsHybrid);
        Assert.Equal(new Uri("https://0.0.0.1/"), info.AppOrigin);
        Assert.Equal(new Uri("http://127.0.0.1:9222/"), info.DevToolsEndpoint);
    }

    [Fact]
    public void Health_of_a_web_host_has_no_WebView()
    {
        var info = WorkbenchHostInfo.Parse("""{"status":"ok","hostingModel":"WebAssembly"}""");
        Assert.False(info.IsHybrid);
        Assert.Null(info.DevToolsEndpoint);
        Assert.Equal("Server", WorkbenchHostInfo.Parse("{}").HostingModel);
    }

    [Theory]
    [InlineData("Server", "")]
    [InlineData("WebAssembly", "-webassembly")]
    [InlineData("Hybrid", "-hybrid")]
    [InlineData(null, "")]
    public void Baselines_are_kept_per_hosting_model(string? hostingModel, string suffix) =>
        Assert.Equal(BaselineStore.CurrentPlatform + suffix, BaselineStore.PlatformFor(hostingModel));
}
