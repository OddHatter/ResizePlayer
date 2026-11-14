using System.Collections.Generic;
using UnityEngine;

namespace Oddhatter.ResizePlayer
{
    /// <summary>
    /// The particle effects of one equipped item instance (FrostFire flames, BloodLightning sparks, Berzerkr swirls,
    /// torch fire). Most use Local scaling mode, which sizes a system by its own transform scale only and ignores the
    /// item and bone scale above it, so they stayed vanilla size on a resized player. Each Local-mode system gets its
    /// own scale times the item's size; whatever hangs below it divides that back out (see BodyParts.EffectScale).
    /// A world-simulated streak (Stretch render) system also gets its streak length corrected (see BodyParts.StretchLength).
    /// Values are always written from the originals, so size 1 restores vanilla exactly.
    /// </summary>
    internal sealed class ItemEffects
    {
        private readonly struct Node
        {
            public readonly Transform T;
            public readonly Vector3 Scale;
            public readonly Vector3 Position;
            public readonly int ScaledAbove;
            public readonly bool Local;
            public readonly ParticleSystemRenderer Stretch;
            public readonly float Length;
            public readonly bool World;

            public Node(Transform t, int scaledAbove, ParticleSystem local)
            {
                T = t;
                Scale = t.localScale;
                Position = t.localPosition;
                ScaledAbove = scaledAbove;
                Local = local != null;
                ParticleSystemRenderer r = local != null ? t.GetComponent<ParticleSystemRenderer>() : null;
                Stretch = r != null && r.renderMode == ParticleSystemRenderMode.Stretch ? r : null;
                Length = Stretch != null ? Stretch.lengthScale : 0f;
                World = local != null && local.main.simulationSpace == ParticleSystemSimulationSpace.World;
            }
        }

        private readonly Transform _item;
        private readonly Transform _joint;
        private readonly List<Node> _nodes;
        private float _applied = 1f;

        private ItemEffects(Transform item, Transform joint, List<Node> nodes)
        {
            _item = item;
            _joint = joint;
            _nodes = nodes;
        }

        /// <summary>How many transforms this resizes (for the log).</summary>
        public int Count => _nodes.Count;

        /// <summary>Gone with its item (unequipped, swapped).</summary>
        public bool Destroyed => _item == null || _joint == null;

        /// <summary>The joint whose size the item has (the hand for a weapon, even a skinned one).</summary>
        public Transform Joint => _joint;

        /// <summary>The effects under an item just attached, or null when it has no Local-mode particle system.</summary>
        public static ItemEffects Capture(Transform item, Transform joint)
        {
            var nodes = new List<Node>();
            for (int i = 0; i < item.childCount; i++)
            {
                Collect(item.GetChild(i), 0, nodes);
            }
            return nodes.Count > 0 ? new ItemEffects(item, joint, nodes) : null;
        }

        private static void Collect(Transform t, int scaledAbove, List<Node> nodes)
        {
            ParticleSystem ps = t.GetComponent<ParticleSystem>();
            bool local = ps != null && ps.main.scalingMode == ParticleSystemScalingMode.Local;
            if (local || scaledAbove > 0)
            {
                nodes.Add(new Node(t, scaledAbove, local ? ps : null));
            }
            int below = local ? scaledAbove + 1 : 0;
            for (int i = 0; i < t.childCount; i++)
            {
                Collect(t.GetChild(i), below, nodes);
            }
        }

        /// <summary>Sizes the effects for an item itemSize x vanilla; does nothing if that is already applied.</summary>
        public void Apply(float itemSize)
        {
            if (Mathf.Abs(itemSize - _applied) < 1e-4f)
            {
                return;
            }
            foreach (Node n in _nodes)
            {
                if (n.T != null)
                {
                    n.T.localScale = n.Scale * BodyParts.EffectScale(itemSize, n.ScaledAbove, n.Local);
                    n.T.localPosition = n.Position * BodyParts.EffectOffset(itemSize, n.ScaledAbove);
                }
                if (n.Stretch != null)
                {
                    n.Stretch.lengthScale = BodyParts.StretchLength(n.Length, itemSize, n.World);
                }
            }
            _applied = itemSize;
        }
    }
}
