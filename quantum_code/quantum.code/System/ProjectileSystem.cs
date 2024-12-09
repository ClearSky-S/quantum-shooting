
using Quantum;

public unsafe class ProjectileSystem : SystemMainThreadFilter<ProjectileSystem.Filter>
{
    public struct Filter
    {
        public EntityRef Entity;
        public Projectile* Projectile;
        public Transform2D* Transform;
    }


    public override void Update(Frame f, ref Filter filter)
    {
        // 충돌 처리
        var hits = f.Physics2D.RaycastAll(filter.Transform->Position, filter.Projectile->Velocity * f.DeltaTime, 1);
        for (int i = 0; i < hits.Count; i++)
        {
            if(hits[i].Entity == filter.Entity)
            {
                continue;
            }
            
            // 충돌한 대상이 적인지 확인
            if(f.Has<PlayerCharacter>(hits[i].Entity))
            {
                // 적을 죽이고, 자신도 죽인다.
                f.Destroy(hits[i].Entity);
                f.Destroy(filter.Entity);
            }
            else
            {
                // 적이 아닌 대상과 충돌했을 경우, 자신을 제거한다.
                f.Destroy(filter.Entity);
            }
        }
        
        
        // 이동
        filter.Transform->Position += filter.Projectile-> Velocity * f.DeltaTime;
    }
}