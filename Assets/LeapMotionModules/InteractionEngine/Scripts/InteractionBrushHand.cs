﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Leap;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Leap.Unity
{
  /** Collision brushes */
  public class InteractionBrushHand : IHandModel
  {
    private const int N_FINGERS = 5;
    private const int N_ACTIVE_BONES = 3;

    private Rigidbody[] _capsuleBodies;
    private Hand hand_;

    public override ModelType HandModelType
    {
      get { return ModelType.Physics; }
    }

    [SerializeField]
    private Chirality handedness;
    public override Chirality Handedness
    {
      get { return handedness; }
    }

    [SerializeField]
    private float _perBoneMass = 1.0f;

    [SerializeField]
    private CollisionDetectionMode _collisionDetection = CollisionDetectionMode.ContinuousDynamic;

    [SerializeField]
    private PhysicMaterial _material = null;


    public override Hand GetLeapHand() { return hand_; }
    public override void SetLeapHand(Hand hand) { hand_ = hand; }

    public override void InitHand()
    {
#if UNITY_EDITOR
      if (!EditorApplication.isPlaying)
        return;

      // We also require a material for friction to be able to work.
      if (_material == null || _material.bounciness != 0.0f || _material.bounceCombine != PhysicMaterialCombine.Minimum)
      {
        UnityEditor.EditorUtility.DisplayDialog("Collision Error!",
                                                "An InteractionBrushHand must have a material with 0 bounciness "
                                                + "and a bounceCombine of Minimum.  Name:" + gameObject.name,
                                                "Ok");
        Debug.Break();
      }
#endif

      _capsuleBodies = new Rigidbody[N_FINGERS * N_ACTIVE_BONES];

      for (int fingerIndex = 0; fingerIndex < N_FINGERS; fingerIndex++)
      {
        for (int jointIndex = 0; jointIndex < N_ACTIVE_BONES; jointIndex++)
        {
          Bone bone = hand_.Fingers[fingerIndex].Bone((Bone.BoneType)(jointIndex + 1)); // +1 to skip first bone.

          int boneArrayIndex = fingerIndex * N_ACTIVE_BONES + jointIndex;
          GameObject capsuleGameObject = new GameObject(gameObject.name, typeof(Rigidbody), typeof(CapsuleCollider));
          capsuleGameObject.layer = gameObject.layer;
#if UNITY_EDITOR
          // This is a debug facility that warns developers of issues.
          capsuleGameObject.AddComponent<InteractionBrushBone>();
#endif

          Transform capsuleTransform = capsuleGameObject.GetComponent<Transform>();
          capsuleTransform.parent = transform;
          capsuleTransform.localScale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z);

          CapsuleCollider capsule = capsuleGameObject.GetComponent<CapsuleCollider>();
          capsule.direction = 2;
          capsule.radius = bone.Width * 0.55f;
          capsule.height = bone.Length + (capsule.radius * 2.0f);
          capsule.material = _material;

          Rigidbody body = capsuleGameObject.GetComponent<Rigidbody>();
          _capsuleBodies[boneArrayIndex] = body;
          body.position = bone.Center.ToVector3();
          body.rotation = bone.Rotation.ToQuaternion();
          body.freezeRotation = true;
          body.constraints = RigidbodyConstraints.FreezeRotation; // again for fun.
          body.useGravity = false;

          body.mass = _perBoneMass;
          body.collisionDetectionMode = _collisionDetection;
        }
      }
    }

    public override void UpdateHand()
    {
#if UNITY_EDITOR
      if (!EditorApplication.isPlaying)
        return;
#endif

      for (int fingerIndex = 0; fingerIndex < N_FINGERS; fingerIndex++)
      {
        for (int jointIndex = 0; jointIndex < N_ACTIVE_BONES; jointIndex++)
        {
          Bone bone = hand_.Fingers[fingerIndex].Bone((Bone.BoneType)(jointIndex + 1));

          int boneArrayIndex = fingerIndex * N_ACTIVE_BONES + jointIndex;
          Rigidbody body = _capsuleBodies[boneArrayIndex];

          body.velocity = (bone.Center.ToVector3() - body.position) / Time.fixedDeltaTime;
          body.MoveRotation(bone.Rotation.ToQuaternion()); // body.rotation has less friction
        }
      }
    }
  }
}
