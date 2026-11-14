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

        /// <summary>Movement speed multiplier of the local player in effect right now (1 when disabled).</summary>
        internal static float LocalStride = 1f;

        /// <summary>The local player's animation sync, whose speed inputs are divided by LocalStride (see Patches).</summary>
        internal static ZSyncAnimation LocalZAnim;

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
            public Transform ParentT; // that ancestor bone; null = none
            public Vector3 Original;
        }

        private readonly List<Bone> _bones = new List<Bone>();
        /// <summary>Unscaled localScale of every bone ever cached. Survives a recache, so a scaled bone is never taken as an original.</summary>
        private readonly Dictionary<Transform, Vector3> _originals = new Dictionary<Transform, Vector3>();
        private readonly PartValues _localTarget = PartValues.Identity();

        /// <summary>What a player with the mod disabled publishes: normal size.</summary>
        private static readonly PartValues s_identity = PartValues.Identity();
        /// <summary>ZDO key hash per Part (indexed by (int)Part), so per-frame reads don't rebuild strings.</summary>
        private static readonly int[] s_zdoHashes = System.Array.ConvertAll(BodyParts.All, p => BodyParts.ZdoKey(p).GetStableHashCode());
        /// <summary>Sizes last written to our ZDO; null until the first write, so the first frame always publishes.</summary>
        private PartValues _lastPublished;
        /// <summary>Another player's sizes as last read from their ZDO.</summary>
        private readonly PartValues _remote = PartValues.Identity();
        private bool _haveRemote;
        private uint _lastRevision;

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

        /// <summary>Distance from each bone to its scaled ancestor bone, unscaled; measured once, survives a recache.</summary>
        private readonly Dictionary<Transform, float> _restSegments = new Dictionary<Transform, float>();
        /// <summary>Torso segments from the hips up to the head: rest length and the part that stretches it.</summary>
        private readonly List<(float length, Part parentPart)> _headPath = new List<(float length, Part parentPart)>();
        /// <summary>The eye point (camera pivot, reach origin); raised or lowered with the head when body parts change height.</summary>
        private Transform _eye;
        private Vector3 _eyeOriginal;
        private bool _eyeMoved;

        /// <summary>True while any scale of ours is on this player (so disabling restores once).</summary>
        private bool _applied;
        private bool _wasLocal;

        /// <summary>Vanilla reach and swim depth of this player, captured once.</summary>
        private bool _reachOriginalsKnown;
        private float _interactOriginal;
        private float _placeOriginal;
        private float _swimDepthOriginal;
        private float _walkSpeedOriginal;
        private float _jogSpeedOriginal;
        private float _runSpeedOriginal;
        private float _crouchSpeedOriginal;
        private float _swimSpeedOriginal;
        private float _accelerationOriginal;

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
            if (!_reachOriginalsKnown)
            {
                _interactOriginal = _player.m_maxInteractDistance;
                _placeOriginal = _player.m_maxPlaceDistance;
                _swimDepthOriginal = _player.m_swimDepth;
                _walkSpeedOriginal = _player.m_walkSpeed;
                _jogSpeedOriginal = _player.m_speed;
                _runSpeedOriginal = _player.m_runSpeed;
                _crouchSpeedOriginal = _player.m_crouchSpeed;
                _swimSpeedOriginal = _player.m_swimSpeed;
                _accelerationOriginal = _player.m_acceleration;
                _reachOriginalsKnown = true;
            }
            if (!EnsureBones())
            {
                return;
            }

            bool enabled = ResizePlayerPlugin.Enabled.Value;
            if (_player == Player.m_localPlayer)
            {
                _wasLocal = true;
                PartValues target = enabled ? LocalTarget() : null;
                Apply(target);
                LocalOverall = target != null ? target[Part.Overall] : 1f;
                ApplyReach(LocalOverall);
                LocalStride = target != null ? BodyParts.StrideFactor(target[Part.Overall], target[Part.Legs]) : 1f;
                ApplySpeed(LocalStride);
                LocalZAnim = _player.m_zanim;
                LogSyncFlagsOnce();
                if (_nview.IsOwner())
                {
                    Publish(_nview.GetZDO(), target ?? s_identity);
                }
            }
            else
            {
                if (enabled)
                {
                    RefreshRemote(_nview.GetZDO());
                }
                Apply(enabled ? _remote : null);
            }
        }

        /// <summary>Writes the sizes other players should see to our ZDO, only the parts that changed.</summary>
        private void Publish(ZDO zdo, PartValues values)
        {
            if (values.ApproximatelyEquals(_lastPublished))
            {
                return;
            }
            foreach (Part part in BodyParts.All)
            {
                if (_lastPublished == null || Mathf.Abs(_lastPublished[part] - values[part]) > 1e-4f)
                {
                    zdo.Set(s_zdoHashes[(int)part], values[part]);
                }
            }
            if (_lastPublished == null)
            {
                _lastPublished = PartValues.Identity();
            }
            _lastPublished.CopyFrom(values);
        }

        /// <summary>Reads another player's sizes from their ZDO when it has changed (or never been read). Missing keys = 1.</summary>
        private void RefreshRemote(ZDO zdo)
        {
            if (_haveRemote && zdo.DataRevision == _lastRevision)
            {
                return;
            }
            foreach (Part part in BodyParts.All)
            {
                _remote[part] = BodyParts.Clamp(zdo.GetFloat(s_zdoHashes[(int)part], 1f));
            }
            _lastRevision = zdo.DataRevision;
            _haveRemote = true;
        }

        private void OnDestroy()
        {
            if (_wasLocal)
            {
                LocalOverall = 1f;
                LocalStride = 1f;
                LocalZAnim = null;
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
                        bone.ParentT = p;
                        break;
                    }
                }
            }
            CacheLegData(animator);
            CacheHeadPath(animator);
            return _bones.Count > 0;
        }

        /// <summary>
        /// Measures (once, while unscaled) the torso segments between the hips and the head, and finds the eye point, so
        /// Apply can keep the eye at head height when legs or torso parts change how tall the body is.
        /// </summary>
        private void CacheHeadPath(Animator animator)
        {
            if (_eye == null && _player.m_eye != null && !_applied)
            {
                _eye = _player.m_eye;
                _eyeOriginal = _eye.localPosition;
                _eyeMoved = false;
            }

            float rootScale = transform.lossyScale.y;
            var byTransform = new Dictionary<Transform, Bone>();
            foreach (Bone bone in _bones)
            {
                byTransform[bone.T] = bone;
                if (bone.ParentT != null && !_restSegments.ContainsKey(bone.T) && !_applied && rootScale > 0f)
                {
                    _restSegments[bone.T] = Vector3.Distance(bone.T.position, bone.ParentT.position) / rootScale;
                }
            }

            _headPath.Clear();
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            for (Transform t = head; t != null && byTransform.TryGetValue(t, out Bone b) && b.ParentT != null; t = b.ParentT)
            {
                if (_restSegments.TryGetValue(t, out float length))
                {
                    _headPath.Add((length, b.ParentPart.Value));
                }
            }
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

            if (_eye != null)
            {
                float lift = BodyParts.HeadLift(target, Mathf.Max(0f, _restLegLength), _headPath);
                if (Mathf.Abs(lift) > 1e-4f || _eyeMoved)
                {
                    Vector3 worldLift = transform.up * (lift * transform.lossyScale.y);
                    _eye.localPosition = _eyeOriginal + _eye.parent.InverseTransformVector(worldLift);
                    _eyeMoved = Mathf.Abs(lift) > 1e-4f;
                }
            }

            _applied = true;
        }

        /// <summary>
        /// Reach (interact, build) grows with a giant so it can still use things it towers over, but a tiny player keeps
        /// vanilla reach. Swim depth follows size both ways: a giant wades, a tiny player swims in shallow water.
        /// </summary>
        private void ApplyReach(float overall)
        {
            float reach = BodyParts.ReachFactor(overall);
            _player.m_maxInteractDistance = _interactOriginal * reach;
            _player.m_maxPlaceDistance = _placeOriginal * reach;
            _player.m_swimDepth = _swimDepthOriginal * overall;
        }

        /// <summary>
        /// Longer legs take longer strides: every movement speed (and the acceleration that reaches it) is multiplied by
        /// the stride factor. The animation keeps its normal step rate because Patches divides its speed input by the
        /// same factor, so the bigger body's longer steps match the ground covered.
        /// </summary>
        private void ApplySpeed(float stride)
        {
            _player.m_walkSpeed = _walkSpeedOriginal * stride;
            _player.m_speed = _jogSpeedOriginal * stride;
            _player.m_runSpeed = _runSpeedOriginal * stride;
            _player.m_crouchSpeed = _crouchSpeedOriginal * stride;
            _player.m_swimSpeed = _swimSpeedOriginal * stride;
            _player.m_acceleration = _accelerationOriginal * stride;
        }

        /// <summary>
        /// How much bigger than vanilla this mod currently makes the given transform: the product, from it up to the
        /// player root, of each scaled transform's current scale over its original. Measured from the transforms
        /// themselves, so it is right whenever it is called.
        /// </summary>
        internal float ScaleFactorAt(Transform t)
        {
            float factor = 1f;
            for (Transform p = t; p != null && p != transform; p = p.parent)
            {
                if (_originals.TryGetValue(p, out Vector3 original) && original.x != 0f)
                {
                    factor *= p.localScale.x / original.x;
                }
            }
            if (_rootOriginalKnown && _rootOriginal.x != 0f)
            {
                factor *= transform.localScale.x / _rootOriginal.x;
            }
            return factor;
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
            if (_eye != null)
            {
                _eye.localPosition = _eyeOriginal;
            }
            _eyeMoved = false;
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
