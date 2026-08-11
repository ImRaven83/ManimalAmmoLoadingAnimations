using System.Reflection;
using System.Threading;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace LoadAmmoAnimMod;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = BuildInfo.ModGuid;
    public string Name { get; init; } = "LoadAmmoAnim";
    public string Author { get; init; } = "Manimal";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(BuildInfo.Version);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "";
    public string License { get; init; } = "MIT";
}

// TypePriority mirrors WTT-ServerCommonLib's own composite loader (OnLoadOrder.Preload),
// so our custom-item registration runs alongside WTT's, well before anything that reads
// the item DB (trader registration, handbook, presets, ragfair).
[Injectable(TypePriority = OnLoadOrder.Preload + 2)]
public class LoadAmmoAnimServer(
    WTTServerCommonLib.WTTServerCommonLib wttCommon) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        await wttCommon.CustomItemParentService.CreateCustomParents(assembly);
        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);

    }
}
