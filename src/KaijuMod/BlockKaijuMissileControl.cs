using KaijuMod;

/// <summary>
/// The missile battery's launch control at the army post above Cinder Bay (blocks.xml: Class
/// "KaijuMissileControl, KaijuMod"). The mod places it next to that post's part crate; press E to
/// fire a salvo at him (KaijuRun.FireMissiles).
///
/// Global namespace on purpose: the game resolves a block Class "X, Assembly" to type "BlockX" in
/// that assembly (ReflectionHelpers.GetTypeWithPrefix, VERIFIED V3.3).
/// </summary>
public class BlockKaijuMissileControl : Block
{
    // VERIFIED (V3.3): radial labels are Localization "blockcommand_" + text; icons ui_game_symbol_<icon>.
    private readonly BlockActivationCommand[] commands =
    {
        new BlockActivationCommand("fire", "electric_switch", true),
    };

    public override bool HasBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return true;
    }

    public override string GetActivationText(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return "Missile battery: " + KaijuRun.Instance.MissileStatus(_blockPos) + " (E to fire)";
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return commands;
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        if (_commandName != "fire")
            return false;
        KaijuRun.Instance.FireMissiles(_blockPos, _player);
        return true;
    }
}
