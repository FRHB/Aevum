using System.Reflection;

namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Captures the immutable startup identity of the running web process so the browser
/// can detect server restarts or new deployments and recover navigation cleanly.
/// </summary>
public sealed class AppBootInfo
{
    public string BootId { get; } = Guid.NewGuid().ToString("N");
    public string InformationalVersion { get; }
    public string BuildHash { get; }

    public AppBootInfo()
    {
        InformationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        BuildHash = InformationalVersion.Contains('+', StringComparison.Ordinal)
            ? InformationalVersion.Split('+', 2)[1]
            : InformationalVersion;
    }
}
