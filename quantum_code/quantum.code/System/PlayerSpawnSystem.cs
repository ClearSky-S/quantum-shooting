using Quantum;

public unsafe class PlayerSpawnSystem : SystemSignalsOnly, ISignalOnPlayerDataSet
{
    public void OnPlayerDataSet(Frame f, PlayerRef player)
    {
        var data = f.GetPlayerData(player);

        // resolve the reference to the prototpye.
        var prototype = f.FindAsset<EntityPrototype>(data.CharacterPrototype.Id);

        // Create a new entity for the player based on the prototype.
        var entity = f.Create(prototype);

        // Create a PlayerLink component. Initialize it with the player. Add the component to the player entity.
        var playerLink = f.Unsafe.GetPointer<PlayerCharacter>(entity);
        playerLink->Player = player;

        // Offset the instantiated object in the world, based in its ID.
        if (f.Unsafe.TryGetPointer<Transform2D>(entity, out var transform))
        {
            transform->Position.X = 0 + player;
        }
    }
}