using KaijuMod;

/// <summary>
/// The weapons the mod places in the army posts (blocks.xml: Class "KaijuWeapon, KaijuMod"): the
/// missile battery's launch control above Cinder Bay, the airstrike radio above Dune Point, and the
/// maser cannon and its generators above Frostport. What each does is in KaijuRun (UseWeapon).
///
/// Global namespace on purpose: the game resolves a block Class "X, Assembly" to type "BlockX" in
/// that assembly (ReflectionHelpers.GetTypeWithPrefix, VERIFIED V3.3).
/// </summary>
public class BlockKaijuWeapon : Block
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
        return KaijuRun.Instance.WeaponText(GetBlockName(), _blockPos);
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        commands[0].text = KaijuRun.Instance.WeaponCommand(GetBlockName());
        return commands;
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        KaijuRun.Instance.UseWeapon(GetBlockName(), _blockPos, _player);
        return true;
    }
}
