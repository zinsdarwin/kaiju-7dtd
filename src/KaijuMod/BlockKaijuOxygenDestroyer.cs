using KaijuMod;

/// <summary>
/// The Oxygen Destroyer block (blocks.xml: Class "KaijuOxygenDestroyer, KaijuMod"). Use it to arm
/// or disarm; take it back while disarmed. The armed state lives in KaijuRun (saved with the run),
/// which checks it when Godzilla attacks Ashmouth.
///
/// Global namespace on purpose: the game resolves a block Class "X, Assembly" to type "BlockX" in
/// that assembly (ReflectionHelpers.GetTypeWithPrefix, VERIFIED V3.3; same pattern as SCore's blocks).
/// </summary>
public class BlockKaijuOxygenDestroyer : Block
{
    // VERIFIED (V3.3): radial labels are Localization "blockcommand_" + text; icons ui_game_symbol_<icon>.
    private readonly BlockActivationCommand[] commands =
    {
        new BlockActivationCommand("arm", "electric_switch", true),
        new BlockActivationCommand("take", "hand", true),
    };

    public override bool HasBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return true;
    }

    public override string GetActivationText(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return KaijuRun.Instance.IsArmed(_blockPos)
            ? "Oxygen Destroyer: ARMED"
            : "Oxygen Destroyer: disarmed";
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        bool armed = KaijuRun.Instance.IsArmed(_blockPos);
        commands[0].text = armed ? "disarm" : "arm";
        commands[0].enabled = true;
        commands[1].enabled = !armed;
        return commands;
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        switch (_commandName)
        {
            case "arm":
                KaijuRun.Instance.SetArmed(_blockPos, true, _player);
                return true;
            case "disarm":
                KaijuRun.Instance.SetArmed(_blockPos, false, _player);
                return true;
            case "take":
                // The base handles picking up a CanPickup block into the inventory.
                return base.OnBlockActivated(_world, _blockPos, _blockValue, _player);
        }
        return false;
    }

    public override void OnBlockRemoved(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue)
    {
        base.OnBlockRemoved(_world, _chunk, _blockPos, _blockValue);
        KaijuRun.Instance.DeviceRemoved(_blockPos);
    }
}
