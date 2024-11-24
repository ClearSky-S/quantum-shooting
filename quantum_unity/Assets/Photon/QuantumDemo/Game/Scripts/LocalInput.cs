using System;
using Photon.Deterministic;
using Quantum;
using UnityEngine;

public class LocalInput : MonoBehaviour {
    
  private void OnEnable() {
    QuantumCallback.Subscribe(this, (CallbackPollInput callback) => PollInput(callback));
  }

  public void PollInput(CallbackPollInput callback) {
    Quantum.Input i = new Quantum.Input();
    i.Direction = new FPVector2(UnityEngine.Input.GetAxis("Horizontal").ToFP(), UnityEngine.Input.GetAxis("Vertical").ToFP());
    i.Jump = UnityEngine.Input.GetButton("Jump");
    callback.SetInput(i, DeterministicInputFlags.Repeatable);
  }
}
