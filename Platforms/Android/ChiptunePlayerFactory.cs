using MIDIRift.Modules;
using MIDIRift.Modules.Audio;

namespace MIDIRift;

/// <summary>
/// Stage C compatibility facade. Existing callers may still select by
/// ChiptuneEngineKind, but construction is resolved through the module registry.
/// </summary>
public static partial class ChiptunePlayerFactory
{
    private static readonly ModuleContext EngineModuleContext = new();
    private static readonly ModuleRegistry EngineModuleRegistry = new();
    private static readonly ModuleManager EngineModuleManager =
        new(EngineModuleRegistry, EngineModuleContext);

    static ChiptunePlayerFactory()
    {
        EngineModuleManager.Register(new LegacyChiptuneEngineFactory());
        EngineModuleManager.Register(new LyraChiptuneEngineFactory());
        EngineModuleManager.Register(new DsnLikeChiptuneEngineFactory());
        EngineModuleManager.InitializeAll();
    }

    public static ChiptuneEngineBuild Create(ChiptuneEngineInput input) =>
        Create(input, ChiptuneEngineSelection.DefaultEngine);

    public static ChiptuneEngineBuild Create(
        ChiptuneEngineInput input,
        ChiptuneEngineKind engineKind)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Resolve(engineKind).Create(input);
    }

    public static ChiptuneEngineBuild Create(
        ChiptuneEngineInput input,
        string engineModuleId)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineModuleId);

        var engine = EngineModuleRegistry.Get<IAudioEngineModule>(engineModuleId)
            ?? throw new KeyNotFoundException(
                $"No audio engine module is registered as '{engineModuleId}'.");

        return engine.Create(input);
    }

    public static string GetEngineId(ChiptuneEngineKind engineKind) =>
        Resolve(engineKind).EngineId;

    public static string GetEngineModuleId(ChiptuneEngineKind engineKind) =>
        Resolve(engineKind).Descriptor.Id;

    private static IChiptuneEngineFactory Resolve(ChiptuneEngineKind engineKind)
    {
        string moduleId = engineKind switch
        {
            ChiptuneEngineKind.Lyra => LyraChiptuneEngineFactory.ModuleId,
            ChiptuneEngineKind.DsnLike => DsnLikeChiptuneEngineFactory.ModuleId,
            _ => LegacyChiptuneEngineFactory.ModuleId,
        };

        return EngineModuleRegistry.Get<IChiptuneEngineFactory>(moduleId)
            ?? throw new InvalidOperationException(
                $"Built-in MIDIRift engine module '{moduleId}' is not registered.");
    }
}
