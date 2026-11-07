using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace BlazorWorkbench.Hybrid;

/// <summary>
/// Wraps the BlazorWebView content provider: serves the generated preview host page (<see cref="WorkbenchHybridHost.HostPageName"/>)
/// and the axe-core script (<c>_workbench/axe.min.js</c>), and everything else from the app's own content.
/// </summary>
public sealed class WorkbenchHybridFileProvider : IFileProvider
{
    internal const string AxePath = "_workbench/axe.min.js";
    private static readonly Lazy<byte[]?> s_axe = new(LoadAxe);
    private readonly IFileProvider _inner;
    private readonly WorkbenchHybridHost _host;
    private byte[]? _hostPage;

    public WorkbenchHybridFileProvider(IFileProvider inner, WorkbenchHybridHost host)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(host);
        _inner = inner;
        _host = host;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var path = (subpath ?? "").Replace('\\', '/').TrimStart('/');
        if (string.Equals(path, WorkbenchHybridHost.HostPageName, StringComparison.OrdinalIgnoreCase))
        {
            _hostPage ??= Encoding.UTF8.GetBytes(_host.BuildHostPage(file => _inner.GetFileInfo(file).Exists));
            return new MemoryFileInfo(WorkbenchHybridHost.HostPageName, _hostPage);
        }

        if (string.Equals(path, AxePath, StringComparison.OrdinalIgnoreCase))
            return s_axe.Value is { } axe ? new MemoryFileInfo("axe.min.js", axe) : new NotFoundFileInfo(path);

        return _inner.GetFileInfo(subpath!);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => _inner.GetDirectoryContents(subpath);

    public IChangeToken Watch(string filter) => _inner.Watch(filter);

    private static byte[]? LoadAxe()
    {
        using var stream = typeof(Deque.AxeCore.Commons.AxeResult).Assembly.GetManifestResourceStream("Deque.AxeCore.Commons.Resources.axe.min.js");
        if (stream is null)
            return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class MemoryFileInfo(string name, byte[] content) : IFileInfo
    {
        public bool Exists => true;
        public long Length => content.Length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(content, writable: false);
    }
}
