using Microsoft.Build.Locator;

namespace SharpSeek.Engine;

/// <summary>
/// Handles one-time registration of an installed .NET SDK toolset with MSBuildLocator.
/// </summary>
/// <remarks>
/// Registration must happen before any MSBuild or Roslyn workspace type is loaded by the
/// runtime. For that reason this type only references <see cref="MSBuildLocator"/> and nothing
/// from MSBuild or the Roslyn workspace layer, and callers must invoke
/// <see cref="EnsureRegistered"/> from a method that does not itself touch those types.
/// </remarks>
public static class MSBuildRegistration
{
    private static readonly object Gate = new();
    private static VisualStudioInstance? _instance;

    /// <summary>
    /// Registers the newest installed .NET SDK with MSBuildLocator. Safe and cheap to call
    /// repeatedly; the actual registration only happens on the first call.
    /// </summary>
    /// <returns>The SDK instance that was (or had previously been) registered.</returns>
    public static VisualStudioInstance EnsureRegistered()
    {
        lock (Gate)
        {
            if (_instance is not null)
            {
                return _instance;
            }

            VisualStudioInstance instance = MSBuildLocator
                .QueryVisualStudioInstances()
                .Where(candidate => candidate.DiscoveryType == DiscoveryType.DotNetSdk)
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "No .NET SDK installation was found for MSBuildLocator to register.");

            MSBuildLocator.RegisterInstance(instance);
            _instance = instance;
            return instance;
        }
    }
}
