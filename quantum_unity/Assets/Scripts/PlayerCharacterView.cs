using System;
using System.Collections;
using System.Collections.Generic;
using Quantum;
using UnityEngine;

public class PlayerCharacterView : QuantumCallbacks
{
    [SerializeField] private GameObject model;
    [SerializeField] private GameObject fireEffect;
    private EntityView _entityView;

    private void Awake()
    {
        _entityView = GetComponent<EntityView>();
    }

    private void Start()
    {
        QuantumEvent.Subscribe<EventOnWeaponShoot>(this, ShootEffect);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    public override void OnUpdateView(QuantumGame game)
    {
        base.OnUpdateView(game);
        model.transform.localScale = new Vector3(game.Frames.Predicted.Get<PlayerCharacter>(_entityView.EntityRef).IsFacingRight ? 1 : -1, 1, 1);
    }

    private void ShootEffect(EventOnWeaponShoot e)
    {
        if (e.PlayerCharacter.Equals(_entityView.EntityRef))
        {
            var effect = Instantiate(fireEffect, transform.position, Quaternion.identity, transform);
            PlayerCharacter playerCharacter = e.Game.Frames.Predicted.Get<PlayerCharacter>(_entityView.EntityRef);
            effect.transform.localScale = new Vector3(playerCharacter.IsFacingRight ? 1 : -1, 1, 1);
        }
    }
}
