
using Photon.Deterministic;
using Quantum;


public unsafe class MovementSystem : SystemMainThreadFilter<MovementSystem.Filter>
{
    public struct Filter
    {
        public EntityRef Entity;
        public PlayerCharacter* playerCharacter;
        public PhysicsBody2D* physicsBody2D;
    }

    public override void Update(Frame f, ref Filter filter)
    {
        // note: pointer property access via -> instead of .
        var input = *f.GetPlayerInput(0);
        FPVector2 velocity = filter.physicsBody2D->Velocity;
        velocity.X = input.Direction.X * 5;
        filter.physicsBody2D->Velocity = FPVector2.Lerp(velocity, filter.physicsBody2D->Velocity, f.DeltaTime * 3);
        if (input.Jump.WasPressed)
        {
            filter.physicsBody2D->Velocity.Y = 8;
        }
    }
}