
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
        var input = *f.GetPlayerInput(filter.playerCharacter->Player);
        FPVector2 velocity = filter.physicsBody2D->Velocity;
        velocity.X = input.Direction.X * 5;
        filter.physicsBody2D->Velocity = FPVector2.Lerp(velocity, filter.physicsBody2D->Velocity, f.DeltaTime * 3);
        bool isGrounded = IsGrounded(f, filter.Entity);
        if (input.Jump.WasPressed && isGrounded)
        {
            filter.physicsBody2D->Velocity.Y = 8;
        }
    }
    
    private bool IsGrounded(Frame f, EntityRef entity)
    {
        // PlayerCharacter playerCharacter = f.Get<PlayerCharacter>(entity);
        PhysicsCollider2D physicsCollider2D = f.Get<PhysicsCollider2D>(entity);
        Transform2D transform2D = f.Get<Transform2D>(entity);
            
        FP raycastLength = FP._0_10;
       
        // Note. 땅 살짝 파고 들을 수 있기 때문에 살짝 위에서 Cast해야 함.
        FPVector2 bottomCenter = transform2D.Position + physicsCollider2D.Shape.Box.Extents.Y * FPVector2.Down + raycastLength/2 * FPVector2.Up;
        FPVector2 bottomLeft = bottomCenter - physicsCollider2D.Shape.Box.Extents.X * FPVector2.Right;
        FPVector2 bottomRight = bottomCenter + physicsCollider2D.Shape.Box.Extents.X * FPVector2.Right;
            
        // check left
        var hitLeft = f.Physics2D.Raycast(bottomLeft, FPVector2.Down, raycastLength);
        Draw.Ray(bottomLeft, FPVector2.Down * raycastLength, color: ColorRGBA.Green);
        // check right
        var hitRight = f.Physics2D.Raycast(bottomRight, FPVector2.Down, raycastLength);
        Draw.Ray(bottomRight, FPVector2.Down * raycastLength, color: ColorRGBA.Green);
        
        if(hitLeft != null || hitRight != null)
        {
            return true;
        }
        return false;
    }
}