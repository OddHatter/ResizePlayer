using System.Collections.Generic;
using UnityEngine;

namespace Oddhatter.ResizePlayer
{
    /// <summary>
    /// Sizes one player. Overall scales the player's root transform, the way the game itself scales enemies for the
    /// "enemy size" world setting, so collider, hitbox and eye point follow. Body parts scale humanoid bones after
    /// animation (visual only). Every scale is computed from cached originals, so nothing accumulates, and turning the
    /// mod off restores the originals.
    /// </summary>
    internal sealed class PlayerScaler : MonoBehaviour
    {
        /// <summary>Overall size of the local player in effect right now (1 when disabled or there is no local player).</summary>
        internal static float LocalOverall = 1f;

        private static readonly (HumanBodyBones bone, Part part)[] BoneMap =
        {
            (HumanBodyBones.Hips, Part.Hips),
            (HumanBodyBones.Spine, Part.Belly),
            (HumanBodyBones.Chest, Part.Chest),
            (HumanBodyBones.UpperChest, Part.Chest),
            (HumanBodyBones.Neck, Part.Neck),
            (HumanBodyBones.Head, Part.Head),
            (HumanBodyBones.LeftUpperArm, Part.Arms),
            (HumanBodyBones.RightUpperArm, Part.Arms),
            (HumanBodyBones.LeftLowerArm, Part.Arms),
            (HumanBodyBones.RightLowerArm, Part.Arms),
            (HumanBodyBones.LeftHand, Part.Hands),
            (HumanBodyBones.RightHand, Part.Hands),
            (HumanBodyBones.LeftUpperLeg, Part.Legs),
            (HumanBodyBones.RightUpperLeg, Part.Legs),
            (HumanBodyBones.LeftLowerLeg, Part.Legs),
            (HumanBodyBones.RightLowerLeg, Part.Legs),
            (HumanBodyBones.LeftFoot, Part.Feet),
            (HumanBodyBones.RightFoot, Part.Feet),
            (HumanBodyBones.LeftToes, Part.Feet),
            (HumanBodyBones.RightToes, Part.Feet),
        };

        /// <summary>Messages already logged (missing bones etc.), so each is reported once per game session.</summary>
        private static readonly HashSet<string> s_logged = new HashSet<string>();

        private sealed class Bone
        {
            public Transform T;
            public Part Part;
            public Part? ParentPart; // part of the nearest scaled ancestor bone; null = none
            public Vector3 Original;
        }

        private readonly List<Bone> _bones = new List<Bone>();
        /// <summary>Unscaled localScale of every bone ever cached. Survives a recache, so a scaled bone is never taken as an original.</summary>
        private readonly Dictionary<Transform, Vector3> _originals = new Dictionary<Transform, Vector3>();
        private readonly PartValues _localTarget = PartValues.Identity();

        private Player _player;
        private ZNetView _nview;
        private Animator _cachedAnimator;
        private bool _rootOriginalKnown;
        private Vector3 _rootOriginal = Vector3.one;

        /// <summary>Hip-to-ankle length in model units (unscaled); -1 until measured.</summary>
        private float _restLegLength = -1f;
        private Transform _armature;
        private Vector3 _armatureOriginal;
        private bool _armatureMoved;

        /// <summary>True while any scale of ours is on this player (so disabling restores once).</summary>
        private bool _applied;
        private bool _wasLocal;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _nview = GetComponent<ZNetView>();
        }

        private void LateUpdate()
        {
            if (_player == null || _nview == null || !_nview.IsValid())
            {
                return;
            }
            if (!_rootOriginalKnown)
            {
                CaptureRootOriginal();
            }
            if (!EnsureBones())
            {
                return;
            }

            if (_player == Player.m_localPlayer)
            {
                _wasLocal = true;
                PartValues target = ResizePlayerPlugin.Enabled.Value ? LocalTarget() : null;
                Apply(target);
                LocalOverall = target != null ? target[Part.Overall] : 1f;
                LogSyncFlagsOnce();
            }
        }

        private void OnDestroy()
        {
            if (_wasLocal)
            {
                LocalOverall = 1f;
            }
        }

        /// <summary>The Player prefab's own scale; the live object's may already have been changed.</summary>
        private void CaptureRootOriginal()
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
            _rootOriginal = prefab != null ? prefab.transform.localScale : Vector3.one;
            _rootOriginalKnown = true;
        }

        /// <summary>Caches the mapped bones (again if the animator or a bone was replaced). False if there are none.</summary>
        private bool EnsureBones()
        {
            Animator animator = _player.m_animator;
            if (animator == null || !animator.isHuman)
            {
                LogOnce("no-humanoid", "Player has no humanoid animator; body parts can't be resized.");
                return false;
            }
            if (animator == _cachedAnimator && _bones.Count > 0 && AllBonesAlive())
            {
                return true;
            }

            _cachedAnimator = animator;
            _bones.Clear();
            var partOf = new Dictionary<Transform, Part>();
            foreach ((HumanBodyBones boneId, Part part) in BoneMap)
            {
                Transform t = animator.GetBoneTransform(boneId);
                if (t == null)
                {
                    LogOnce("bone-" + boneId, $"Bone {boneId} not found on the player; it is skipped ({part}).");
                    continue;
                }
                if (!_originals.TryGetValue(t, out Vector3 original))
                {
                    original = t.localScale;
                    _originals[t] = original;
                }
                partOf[t] = part;
                _bones.Add(new Bone { T = t, Part = part, Original = original });
            }
            foreach (Bone bone in _bones)
            {
                for (Transform p = bone.T.parent; p != null && p != animator.transform; p = p.parent)
                {
                    if (partOf.TryGetValue(p, out Part parentPart))
                    {
                        bone.ParentPart = parentPart;
                        break;
                    }
                }
            }
            CacheLegData(animator);
            return _bones.Count > 0;
        }

        private bool AllBonesAlive()
        {
            foreach (Bone bone in _bones)
            {
                if (bone.T == null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Leg bones are scaled after animation and foot IK, so longer legs would push the feet into the ground and
        /// shorter legs would leave them floating. Apply moves the skeleton (the hips' parent) up or down to match;
        /// this measures the unscaled leg once and finds that parent.
        /// </summary>
        private void CacheLegData(Animator animator)
        {
            if (_restLegLength < 0f && !_applied)
            {
                Transform upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Transform lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                float rootScale = transform.lossyScale.y;
                if (upper != null && lower != null && foot != null && rootScale > 0f)
                {
                    _restLegLength = (Vector3.Distance(upper.position, lower.position) +
                                      Vector3.Distance(lower.position, foot.position)) / rootScale;
                }
                else
                {
                    LogOnce("leg-length", "Couldn't measure the leg; feet may sink or float when Legs isn't 1.");
                    _restLegLength = 0f;
                }
            }

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform armature = hips != null ? hips.parent : null;
            if (armature == null || armature == animator.transform || armature.parent == null)
            {
                LogOnce("armature", "No separate skeleton root above the hips; feet may sink or float when Legs isn't 1.");
                armature = null;
            }
            if (armature != _armature)
            {
                _armature = armature;
                _armatureMoved = false;
                if (armature != null)
                {
                    _armatureOriginal = armature.localPosition;
                }
            }
        }

        private PartValues LocalTarget()
        {
            foreach (Part part in BodyParts.All)
            {
                _localTarget[part] = BodyParts.Clamp(ResizePlayerPlugin.Sizes[part].Value);
            }
            return _localTarget;
        }

        /// <summary>Sizes the player to target, or restores vanilla once when target is null.</summary>
        private void Apply(PartValues target)
        {
            if (target == null)
            {
                if (_applied)
                {
                    Restore();
                }
                return;
            }

            foreach (Bone bone in _bones)
            {
                float parent = bone.ParentPart.HasValue ? target[bone.ParentPart.Value] : 1f;
                bone.T.localScale = bone.Original * BodyParts.LocalFactor(target[bone.Part], parent);
            }

            Vector3 rootScale = _rootOriginal * target[Part.Overall];
            if (transform.localScale != rootScale)
            {
                transform.localScale = rootScale;
            }

            float legDelta = target[Part.Legs] - 1f;
            if (_armature != null && (legDelta != 0f || _armatureMoved))
            {
                Vector3 worldOffset = transform.up * (legDelta * _restLegLength * transform.lossyScale.y);
                _armature.localPosition = _armatureOriginal + _armature.parent.InverseTransformVector(worldOffset);
                _armatureMoved = legDelta != 0f;
            }

            _applied = true;
        }

        private void Restore()
        {
            foreach (Bone bone in _bones)
            {
                if (bone.T != null)
                {
                    bone.T.localScale = bone.Original;
                }
            }
            transform.localScale = _rootOriginal;
            if (_armature != null)
            {
                _armature.localPosition = _armatureOriginal;
            }
            _armatureMoved = false;
            _applied = false;
        }

        /// <summary>Whether vanilla already syncs the player's scale (then players without the mod see Overall too).</summary>
        private void LogSyncFlagsOnce()
        {
            if (!s_logged.Add("sync-flags"))
            {
                return;
            }
            ZSyncTransform sync = GetComponent<ZSyncTransform>();
            string syncScale = sync != null ? sync.m_syncScale.ToString() : "no ZSyncTransform";
            ResizePlayerPlugin.Log.LogInfo(
                $"Player scale sync in vanilla: ZSyncTransform.m_syncScale={syncScale}, ZNetView.m_syncInitialScale={_nview.m_syncInitialScale}");
        }

        private static void LogOnce(string key, string message)
        {
            if (s_logged.Add(key))
            {
                ResizePlayerPlugin.Log.LogWarning(message);
            }
        }
    }
}
