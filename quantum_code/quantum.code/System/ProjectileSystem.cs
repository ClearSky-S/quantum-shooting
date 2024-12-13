
using Photon.Deterministic;
using Quantum;
using Quantum.Physics2D;

public unsafe class ProjectileSystem : SystemMainThreadFilter<ProjectileSystem.Filter>
{
    public struct Filter
    {
        public EntityRef Entity;
        public Projectile* Projectile;
        public Transform2D* Transform;
    }

    private Shape2D circle = Shape2D.CreateCircle(FP._0_10);


    public override void Update(Frame f, ref Filter filter)
    {
        // Note. Raycast는 충돌 대상의 내부 검사 X, 각도가 180도 넘어가는 표면 충돌 X
        
        // 충돌 처리
        FPVector2 delta = filter.Projectile->Velocity * f.DeltaTime;
        var hits = f.Physics2D.RaycastAll(filter.Transform->Position, delta.Normalized, delta.Magnitude);
        
        for (int i = 0; i < hits.Count; i++)
        {
            Hit hit = hits[i];

            if(hit.Entity == filter.Projectile->Owner)
            {
                continue;
            }
            
            // 충돌한 대상이 적인지 확인
            if(f.Has<PlayerCharacter>(hit.Entity))
            {
                f.Events.OnProjectileHit(filter.Entity);
                PhysicsBody2D* physicsBody2D = f.Unsafe.GetPointer<PhysicsBody2D>(hit.Entity);
                physicsBody2D->Velocity = 15 * (filter.Projectile->Velocity.X > 0 ? FPVector2.Right : FPVector2.Left);
            }

            filter.Transform->Position = hit.Point;
            f.Destroy(filter.Entity);
            return;
        }
        
        
        // 이동
        filter.Transform->Position += delta;
    }
}