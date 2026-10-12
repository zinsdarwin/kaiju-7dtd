using KaijuMod;

/// <summary>
/// The weapons you craft from the parts found in each strip's towns and place yourself (blocks.xml:
/// Class "KaijuWeapon, KaijuMod"): the missile battery's launch control, the airstrike radio and
/// the maser cannon. What each does is in KaijuRun (UseWeapon); "take" picks it up again.
///
/// Global namespace on purpose: the game resolves a block Class "X, Assembly" to type "BlockX" in
/// that assembly (ReflectionHelpers.GetTypeWithPrefix, VERIFIED V3.3).
/// </summary>
public class BlockKaijuWeapon : Block
{

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
        // VERIFIED (V3.3): radial labels are Localization "blockcommand_" + text; icons ui_game_symbol_<icon>.
        string[] names = KaijuRun.Instance.WeaponCommands(GetBlockName());
        var commands = new BlockActivationCommand[names.Length];
        for (int i = 0; i < names.Length; i++)
            commands[i] = new BlockActivationCommand(names[i], names[i] == "take" ? "hand" : names[i] == "fuel" ? "gas" : "electric_switch", true);
        return commands;
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        if (_commandName == "take")
            return base.OnBlockActivated(_world, _blockPos, _blockValue, _player); // the base picks up a CanPickup block
        KaijuRun.Instance.UseWeapon(GetBlockName(), _commandName, _blockPos, _player);
        return true;
    }
}
