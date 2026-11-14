using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Oddhatter.ResizePlayer
{
    /// <summary>
    /// The particle effects of one equipped item instance (FrostFire flames, BloodLightning sparks, Berzerkr swirls,
    /// torch fire). Most use Local scaling mode, which sizes a system by its own transform scale only and ignores the
    /// item and bone scale above it, so they stayed vanilla size on a resized player. Each Local-mode system gets its
    /// own scale times the item's size; whatever hangs below it divides that back out (see BodyParts.EffectScale).
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

            public Node(Transform t, int scaledAbove, bool local)
            {
                T = t;
                Scale = t.localScale;
                Position = t.localPosition;
                ScaledAbove = scaledAbove;
                Local = local;
            }
        }

        private readonly Transform _item;
        private readonly Transform _joint;
        private readonly List<Node> _nodes;
        private readonly string _label;
        private float _applied = 1f;
        private float _reportAt = -1f;

        private ItemEffects(Transform item, Transform joint, List<Node> nodes, string label)
        {
            _item = item;
            _joint = joint;
            _nodes = nodes;
            _label = label;
        }

        /// <summary>How many transforms this resizes (for the log).</summary>
        public int Count => _nodes.Count;

        /// <summary>Gone with its item (unequipped, swapped).</summary>
        public bool Destroyed => _item == null || _joint == null;

        /// <summary>The joint whose size the item has (the hand for a weapon, even a skinned one).</summary>
        public Transform Joint => _joint;

        /// <summary>The effects under an item just attached, or null when it has no Local-mode particle system.</summary>
        public static ItemEffects Capture(Transform item, Transform joint, string label)
        {
            var nodes = new List<Node>();
            for (int i = 0; i < item.childCount; i++)
            {
                Collect(item.GetChild(i), 0, nodes);
            }
            return nodes.Count > 0 ? new ItemEffects(item, joint, nodes, label) : null;
        }

        private static void Collect(Transform t, int scaledAbove, List<Node> nodes)
        {
            ParticleSystem ps = t.GetComponent<ParticleSystem>();
            bool local = ps != null && ps.main.scalingMode == ParticleSystemScalingMode.Local;
            if (local || scaledAbove > 0)
            {
                nodes.Add(new Node(t, scaledAbove, local));
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
            }
            _applied = itemSize;
            _reportAt = Time.time + 2f;
        }

        // DIAGNOSTIC (temporary): giant particle effects looked too big. Logs, 2 s after each size change, every particle
        // system under the item with its world bounds next to the item's mesh bounds, so the ratio can be compared
        // across sizes.
        public void ReportIfDue()
        {
            if (_reportAt < 0f || Time.time < _reportAt || _item == null)
            {
                return;
            }
            _reportAt = -1f;
            float mesh = 0f;
            foreach (Renderer r in _item.GetComponentsInChildren<Renderer>())
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    mesh = Mathf.Max(mesh, r.bounds.size.magnitude);
                }
            }
            var sb = new StringBuilder();
            sb.Append($"[diag] Item effects {_label}: size {_applied:0.###}, mesh bounds {mesh:0.###} m");
            foreach (ParticleSystem ps in _item.GetComponentsInChildren<ParticleSystem>())
            {
                Transform t = ps.transform;
                float ratio = 0f;
                foreach (Node n in _nodes)
                {
                    if (n.T == t && n.Scale.x != 0f)
                    {
                        ratio = t.localScale.x / n.Scale.x;
                    }
                }
                ParticleSystem.MainModule main = ps.main;
                ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
                float bounds = pr != null ? pr.bounds.size.magnitude : 0f;
                sb.Append($"\n  {Path(t)}: scaling={main.scalingMode} sim={main.simulationSpace} shape={ps.shape.shapeType}" +
                          $" render={(pr != null ? pr.renderMode.ToString() : "-")} scaleX/orig={ratio:0.###} lossy={t.lossyScale.x:0.###}" +
                          $" particles={ps.particleCount} bounds={bounds:0.###} m ({(mesh > 0f ? bounds / mesh : 0f):0.###} x mesh)");
                AppendGeometry(sb, pr);
                if (main.simulationSpace == ParticleSystemSimulationSpace.World && pr != null && pr.renderMode == ParticleSystemRenderMode.Stretch)
                {
                    AppendProbes(sb, ps);
                }
            }
            ResizePlayerPlugin.Log.LogInfo(sb.ToString());
        }

        // The rendered particle quads, in the item's own frame (so divided by its size: proportional effects give the
        // same numbers at every size). Extent = spread of all vertices along the item's x/y/z; quad = mean long and
        // short edge of one particle.
        private void AppendGeometry(StringBuilder sb, ParticleSystemRenderer pr)
        {
            string g = Geometry(pr);
            if (g != null)
            {
                sb.Append("\n      bake: ").Append(g);
            }
            ParticleSystem ps = pr != null ? pr.GetComponent<ParticleSystem>() : null;
            if (ps != null)
            {
                sb.Append("\n      particles: ").Append(Particles(ps));
            }
        }

        // Baked quads in the item's frame (the item is scaled with the player, so proportional = same at every size).
        private string Geometry(ParticleSystemRenderer pr)
        {
            Camera cam = Camera.main;
            if (pr == null || cam == null || pr.renderMode == ParticleSystemRenderMode.None)
            {
                return null;
            }
            var baked = new Mesh();
            pr.BakeMesh(baked, cam, ParticleSystemBakeMeshOptions.Default);
            Vector3[] v = baked.vertices;
            Object.DestroyImmediate(baked);
            if (v.Length < 4)
            {
                return $"{v.Length} vertices";
            }
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = _item.InverseTransformPoint(v[i]);
                min = Vector3.Min(min, v[i]);
                max = Vector3.Max(max, v[i]);
            }
            float longSum = 0f, shortSum = 0f;
            int quads = v.Length / 4;
            for (int q = 0; q < quads; q++)
            {
                float e1 = (v[q * 4 + 1] - v[q * 4]).magnitude, e2 = (v[q * 4 + 2] - v[q * 4 + 1]).magnitude;
                longSum += Mathf.Max(e1, e2);
                shortSum += Mathf.Min(e1, e2);
            }
            Vector3 ext = max - min;
            return $"extent {ext.x:0.###} x {ext.y:0.###} x {ext.z:0.###}, {quads} quads mean {longSum / quads:0.###} x {shortSum / quads:0.###}";
        }

        // Particle centres and speeds, per item size: centre extent in the item's frame, speeds in world m/s / size.
        private string Particles(ParticleSystem ps)
        {
            var parts = new ParticleSystem.Particle[ps.particleCount];
            int count = ps.GetParticles(parts);
            if (count == 0)
            {
                return "none";
            }
            bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            float vel = 0f, total = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 w = world ? parts[i].position : ps.transform.TransformPoint(parts[i].position);
                Vector3 c = _item.InverseTransformPoint(w);
                min = Vector3.Min(min, c);
                max = Vector3.Max(max, c);
                vel += parts[i].velocity.magnitude;
                total += parts[i].totalVelocity.magnitude;
            }
            Vector3 ext = max - min;
            float k = 1f / (count * _applied);
            return $"n={count} centres {ext.x:0.###} x {ext.y:0.###} x {ext.z:0.###} |v|={vel * k:0.###} |total|={total * k:0.###}";
        }

        // Hidden copies of a world-space Stretch system, same modules, different renderer settings, run 0.6 s from rest
        // and baked: the variant whose quads stay the same size per item size names the stretch term that scales wrong.
        private void AppendProbes(StringBuilder sb, ParticleSystem src)
        {
            float k = _applied;
            Probe(sb, src, "as is", r => { });
            Probe(sb, src, "billboard", r => r.renderMode = ParticleSystemRenderMode.Billboard);
            Probe(sb, src, "length/s", r => r.lengthScale /= k);
            Probe(sb, src, "length/s vel/s", r => { r.lengthScale /= k; r.velocityScale /= k; });
            Probe(sb, src, "length/s^2", r => r.lengthScale /= k * k);
            Probe(sb, src, "length/s^2 vel/s", r => { r.lengthScale /= k * k; r.velocityScale /= k; });
        }

        private void Probe(StringBuilder sb, ParticleSystem src, string name, System.Action<ParticleSystemRenderer> tweak)
        {
            ParticleSystem ps = Object.Instantiate(src, src.transform.parent, false);
            Transform t = ps.transform;
            t.localPosition = src.transform.localPosition;
            t.localRotation = src.transform.localRotation;
            t.localScale = src.transform.localScale;
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            tweak(r);
            ps.Simulate(0.6f, false, true, true);
            sb.Append($"\n      probe {name}: ").Append(Particles(ps)).Append(" | bake: ").Append(Geometry(r) ?? "none");
            Object.DestroyImmediate(ps.gameObject);
        }

        private string Path(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != _item; p = p.parent)
            {
                path = p.name + "/" + path;
            }
            return path;
        }
    }
}
