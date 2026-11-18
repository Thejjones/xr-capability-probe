using System;
using System.Collections.Generic;
using UnityEngine;

namespace XrProbe
{
    // A generated placeholder for a character, used only to load the GPU and CPU for the soak: a skinned mesh made of tubes around a humanoid skeleton of 55
    // bones, with the triangle count and the number of materials of a typical avatar. It is built in code from primitives: no model, no texture, nothing
    // third-party. It idles (the bones sway) so skinning and animation cost something.
    public sealed class SyntheticFigure
    {
        public GameObject Root { get; private set; }
        public int Triangles { get; private set; }
        public int Vertices { get; private set; }
        public int Bones { get; private set; }
        public int Materials { get; private set; }

        struct BoneDef { public string Name; public int Parent; public Vector3 Offset; public int Group; }

        readonly List<Transform> bones = new List<Transform>();
        readonly List<Quaternion> baseRot = new List<Quaternion>();

        public string Describe()
        {
            return "a generated figure of " + Triangles + " triangles, " + Bones + " bones and " + Materials + " materials (synthetic load, not a real character)";
        }

        // radial: sides of each tube; rings: rings along it. 16 x 18 over 55 bones is about 31,700 triangles.
        public static SyntheticFigure Build(Material[] materials, int radial = 16, int rings = 18)
        {
            var f = new SyntheticFigure();
            f.Construct(materials, radial, rings);
            return f;
        }

        static List<BoneDef> Skeleton()
        {
            var l = new List<BoneDef>();
            Func<string, int, Vector3, int, int> add = (n, p, o, g) => { l.Add(new BoneDef { Name = n, Parent = p, Offset = o, Group = g }); return l.Count - 1; };
            int hips = add("hips", -1, new Vector3(0f, 0.95f, 0f), 0);
            int spine = add("spine", hips, new Vector3(0f, 0.12f, 0f), 1);
            int chest = add("chest", spine, new Vector3(0f, 0.14f, 0f), 1);
            int upper = add("upperChest", chest, new Vector3(0f, 0.12f, 0f), 1);
            int neck = add("neck", upper, new Vector3(0f, 0.14f, 0f), 3);
            int head = add("head", neck, new Vector3(0f, 0.08f, 0f), 3);
            add("jaw", head, new Vector3(0f, 0.04f, 0.05f), 3);
            add("eyeL", head, new Vector3(-0.03f, 0.08f, 0.08f), 3);
            add("eyeR", head, new Vector3(0.03f, 0.08f, 0.08f), 3);
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                string s = side == 0 ? "L" : "R";
                int ul = add("upperLeg" + s, hips, new Vector3(0.09f * sx, -0.04f, 0f), 0);
                int ll = add("lowerLeg" + s, ul, new Vector3(0f, -0.42f, 0f), 0);
                int ft = add("foot" + s, ll, new Vector3(0f, -0.42f, 0f), 0);
                add("toes" + s, ft, new Vector3(0f, -0.04f, 0.12f), 0);
                int sh = add("shoulder" + s, upper, new Vector3(0.05f * sx, 0.1f, 0f), 2);
                int ua = add("upperArm" + s, sh, new Vector3(0.12f * sx, 0f, 0f), 2);
                int la = add("lowerArm" + s, ua, new Vector3(0.27f * sx, 0f, 0f), 2);
                int hd = add("hand" + s, la, new Vector3(0.25f * sx, 0f, 0f), 3);
                for (int fi = 0; fi < 5; fi++)
                {
                    float z = (fi - 2) * 0.018f;
                    int p = add("finger" + fi + "a" + s, hd, new Vector3(0.05f * sx, 0f, z), 3);
                    p = add("finger" + fi + "b" + s, p, new Vector3(0.03f * sx, 0f, 0f), 3);
                    add("finger" + fi + "c" + s, p, new Vector3(0.025f * sx, 0f, 0f), 3);
                }
            }
            return l;
        }

        void Construct(Material[] materials, int radial, int rings)
        {
            var defs = Skeleton();
            Root = new GameObject("SyntheticFigure");
            for (int i = 0; i < defs.Count; i++)
            {
                var t = new GameObject(defs[i].Name).transform;
                t.SetParent(defs[i].Parent < 0 ? Root.transform : bones[defs[i].Parent], false);
                t.localPosition = defs[i].Offset;
                bones.Add(t); baseRot.Add(t.localRotation);
            }
            Bones = bones.Count;

            int groups = 4;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var weights = new List<BoneWeight>();
            var tris = new List<int>[groups];
            for (int g = 0; g < groups; g++) tris[g] = new List<int>();

            // One tube per bone, from the bone to the first child (or a short stub for the last bone), rigid to the bone with the first ring shared with the parent.
            for (int i = 0; i < defs.Count; i++)
            {
                Vector3 a = Root.transform.InverseTransformPoint(bones[i].position);
                int child = defs.FindIndex(d => d.Parent == i);
                Vector3 b = child >= 0 ? Root.transform.InverseTransformPoint(bones[child].position) : a + new Vector3(0f, -0.04f, 0.02f);
                Vector3 axis = b - a; float len = Mathf.Max(0.02f, axis.magnitude);
                Vector3 dir = axis.sqrMagnitude < 1e-8f ? Vector3.down : axis.normalized;
                Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up).normalized, v = Vector3.Cross(dir, u);
                float radius = defs[i].Name.StartsWith("finger") ? 0.007f : defs[i].Name == "head" ? 0.1f : defs[i].Name.Contains("Leg") ? 0.06f : defs[i].Name == "hips" || defs[i].Name == "chest" || defs[i].Name == "upperChest" ? 0.13f : 0.04f;
                int first = verts.Count;
                int parent = defs[i].Parent < 0 ? i : defs[i].Parent;
                for (int r = 0; r < rings; r++)
                {
                    float t = r / (float)(rings - 1);
                    float taper = 0.6f + 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 0.9f + 0.05f));
                    for (int k = 0; k < radial; k++)
                    {
                        float ang = k / (float)radial * Mathf.PI * 2f;
                        Vector3 n = Mathf.Cos(ang) * u + Mathf.Sin(ang) * v;
                        verts.Add(a + dir * (t * len) + n * (radius * taper));
                        norms.Add(n);
                        var w = new BoneWeight();
                        if (r == 0 && parent != i) { w.boneIndex0 = i; w.weight0 = 0.5f; w.boneIndex1 = parent; w.weight1 = 0.5f; }
                        else { w.boneIndex0 = i; w.weight0 = 1f; }
                        weights.Add(w);
                    }
                }
                var list = tris[defs[i].Group];
                for (int r = 0; r < rings - 1; r++)
                    for (int k = 0; k < radial; k++)
                    {
                        int i0 = first + r * radial + k, i1 = first + r * radial + (k + 1) % radial, i2 = i0 + radial, i3 = i1 + radial;
                        list.Add(i0); list.Add(i2); list.Add(i1); list.Add(i1); list.Add(i2); list.Add(i3);
                    }
                // caps: a fan at each end
                foreach (int ring in new[] { 0, rings - 1 })
                {
                    int center = verts.Count;
                    Vector3 c = a + dir * ((ring == 0 ? 0f : 1f) * len);
                    verts.Add(c); norms.Add(ring == 0 ? -dir : dir);
                    var cw = new BoneWeight { boneIndex0 = i, weight0 = 1f }; weights.Add(cw);
                    for (int k = 0; k < radial; k++)
                    {
                        int p0 = first + ring * radial + k, p1 = first + ring * radial + (k + 1) % radial;
                        if (ring == 0) { list.Add(center); list.Add(p1); list.Add(p0); } else { list.Add(center); list.Add(p0); list.Add(p1); }
                    }
                }
            }

            var mesh = new Mesh { name = "SyntheticFigureMesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetNormals(norms);
            mesh.boneWeights = weights.ToArray();
            mesh.subMeshCount = groups;
            int total = 0;
            for (int g = 0; g < groups; g++) { mesh.SetTriangles(tris[g], g); total += tris[g].Count / 3; }
            var bind = new Matrix4x4[bones.Count];
            for (int i = 0; i < bones.Count; i++) bind[i] = bones[i].worldToLocalMatrix * Root.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            Triangles = total; Vertices = verts.Count;

            var go = new GameObject("Skin");
            go.transform.SetParent(Root.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones.ToArray();
            smr.rootBone = bones[0];
            smr.updateWhenOffscreen = true;
            var mats = new Material[groups];
            for (int g = 0; g < groups; g++) mats[g] = materials != null && g < materials.Length ? materials[g] : null;
            smr.sharedMaterials = mats;
            Materials = groups;
        }

        // The idle sway: every bone turns a little, at its own pace.
        public void Animate(float time)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                float a = Mathf.Sin(time * (0.6f + (i % 7) * 0.13f) + i) * 6f;
                bones[i].localRotation = baseRot[i] * Quaternion.Euler(a * 0.5f, a, a * 0.3f);
            }
        }

        static void Kill(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        public void Destroy()
        {
            if (Root != null)
            {
                var smr = Root.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null) Kill(smr.sharedMesh);
                Kill(Root);
            }
        }
    }
}
