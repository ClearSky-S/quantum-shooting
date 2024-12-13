using System;
using System.Collections;
using System.Collections.Generic;
using Quantum;
using UnityEngine;

public class ProjectileView : QuantumCallbacks
{
    [SerializeField] private GameObject hitEffect;
    private EntityView _entityView;

    private void Awake()
    {
        _entityView = GetComponent<EntityView>();
    }

    private void Start()
    {
        QuantumEvent.Subscribe<EventOnProjectileHit>(this, HitEffect);
    }
    
    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }
    private void HitEffect(EventOnProjectileHit e)
    {
        if (e.Projectile.Equals(_entityView.EntityRef))
        {
            Instantiate(hitEffect, transform.position, Quaternion.identity);
        }
    }
}
