using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterComparisonExtreme.Rocks
{
    /// <summary>
    /// Builds a deterministic, genuinely displaced rock mesh. The component has no
    /// Update loop: it creates a mesh once on enable and only rebuilds when a serialized
    /// generation setting changes. Scene builders can call CreateMesh directly instead.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ExtremeRockGenerator : MonoBehaviour
    {
        [SerializeField] private int seed = 1947;
        [SerializeField, Range(2, 5)] private int subdivisions = 4;
        [SerializeField] private Vector3 proportions = new Vector3(1.0f, 0.82f, 0.92f);
        [SerializeField, Range(0.25f, 1.5f)] private float ruggedness = 1.0f;

        [NonSerialized] private Mesh generatedMesh;
        [NonSerialized] private int generatedSignature;

        public int Seed
        {
            get => seed;
            set
            {
                if (seed == value)
                    return;
                seed = value;
                EnsureGenerated();
            }
        }

        public static Mesh CreateMesh(int seed, int subdivisions = 4)
        {
            return CreateMesh(seed, subdivisions, new Vector3(1.0f, 0.82f, 0.92f), 1.0f);
        }

        // Alias kept deliberately explicit for editor scene builders.
        public static Mesh GenerateMesh(int seed, int subdivisions = 4)
        {
            return CreateMesh(seed, subdivisions);
        }

        public static Mesh GenerateMesh(int seed, int subdivisions, Vector3 proportions, float ruggedness)
        {
            return CreateMesh(seed, subdivisions, proportions, ruggedness);
        }

        /// <summary>
        /// Public entry point intended for the Extreme scene builder. At subdivision 4
        /// the source icosphere contains 2,562 vertices / 5,120 triangles. Only triangles
        /// on chipped planes are split, keeping the finished mesh comfortably lightweight.
        /// </summary>
        public static Mesh CreateMesh(int seed, int subdivisions, Vector3 proportions, float ruggedness)
        {
            subdivisions = Mathf.Clamp(subdivisions, 2, 5);
            proportions = SanitizeProportions(proportions);
            ruggedness = Mathf.Clamp(ruggedness, 0.25f, 1.5f);

            var unitVertices = new List<Vector3>();
            var sourceTriangles = new List<int>();
            CreateIcosphere(subdivisions, unitVertices, sourceTriangles);

            var deformation = new DeformationContext(seed, proportions, ruggedness);
            var displacedVertices = new List<Vector3>(unitVertices.Count);
            var displacedUVs = new List<Vector2>(unitVertices.Count);
            var displacedColors = new List<Color>(unitVertices.Count);
            var cutMasks = new int[unitVertices.Count];

            for (var vertexIndex = 0; vertexIndex < unitVertices.Count; vertexIndex++)
            {
                var direction = unitVertices[vertexIndex].normalized;
                displacedVertices.Add(Deform(direction, deformation, out cutMasks[vertexIndex], out var surfaceData));
                displacedUVs.Add(ToSphericalUV(direction));
                displacedColors.Add(surfaceData);
            }

            // A shared, smoothly shaded body retains the weathered roundness. Triangles
            // entirely on a cut plane receive private vertices, producing a genuinely
            // hard plane and a crisp normal discontinuity at chipped edges.
            var finalVertices = new List<Vector3>(displacedVertices);
            var finalUVs = new List<Vector2>(displacedUVs);
            var finalColors = new List<Color>(displacedColors);
            var finalTriangles = new List<int>(sourceTriangles.Count);

            for (var triangleIndex = 0; triangleIndex < sourceTriangles.Count; triangleIndex += 3)
            {
                var indexA = sourceTriangles[triangleIndex];
                var indexB = sourceTriangles[triangleIndex + 1];
                var indexC = sourceTriangles[triangleIndex + 2];
                var commonCut = cutMasks[indexA] & cutMasks[indexB] & cutMasks[indexC];

                if (commonCut == 0)
                {
                    finalTriangles.Add(indexA);
                    finalTriangles.Add(indexB);
                    finalTriangles.Add(indexC);
                    continue;
                }

                AppendPrivateVertex(indexA, displacedVertices, displacedUVs, displacedColors,
                    finalVertices, finalUVs, finalColors, finalTriangles);
                AppendPrivateVertex(indexB, displacedVertices, displacedUVs, displacedColors,
                    finalVertices, finalUVs, finalColors, finalTriangles);
                AppendPrivateVertex(indexC, displacedVertices, displacedUVs, displacedColors,
                    finalVertices, finalUVs, finalColors, finalTriangles);
            }

            var mesh = new Mesh
            {
                name = $"Extreme Rock {seed}",
                indexFormat = finalVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(finalVertices);
            mesh.SetUVs(0, finalUVs);
            mesh.SetColors(finalColors);
            mesh.SetTriangles(finalTriangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        [ContextMenu("Regenerate extreme rock")]
        public void Regenerate()
        {
            generatedSignature = 0;
            EnsureGenerated();
        }

        public void EnsureGenerated()
        {
            subdivisions = Mathf.Clamp(subdivisions, 2, 5);
            proportions = SanitizeProportions(proportions);
            ruggedness = Mathf.Clamp(ruggedness, 0.25f, 1.5f);

            var signature = CalculateSignature(seed, subdivisions, proportions, ruggedness);
            var meshFilter = GetComponent<MeshFilter>();
            if (generatedMesh != null && generatedSignature == signature && meshFilter.sharedMesh == generatedMesh)
                return;

            DestroyGeneratedMesh(meshFilter);
            generatedMesh = CreateMesh(seed, subdivisions, proportions, ruggedness);
            generatedMesh.hideFlags = HideFlags.DontSave;
            generatedSignature = signature;
            meshFilter.sharedMesh = generatedMesh;

            var meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null)
                meshCollider.sharedMesh = generatedMesh;
        }

        private void OnEnable()
        {
            EnsureGenerated();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
                EnsureGenerated();
        }

        private void OnDestroy()
        {
            DestroyGeneratedMesh(GetComponent<MeshFilter>());
        }

        private void DestroyGeneratedMesh(MeshFilter meshFilter)
        {
            if (generatedMesh == null)
                return;

            if (meshFilter != null && meshFilter.sharedMesh == generatedMesh)
                meshFilter.sharedMesh = null;

            if (Application.isPlaying)
                Destroy(generatedMesh);
            else
                DestroyImmediate(generatedMesh);

            generatedMesh = null;
        }

        private static void AppendPrivateVertex(
            int sourceIndex,
            IReadOnlyList<Vector3> sourceVertices,
            IReadOnlyList<Vector2> sourceUVs,
            IReadOnlyList<Color> sourceColors,
            ICollection<Vector3> destinationVertices,
            ICollection<Vector2> destinationUVs,
            ICollection<Color> destinationColors,
            ICollection<int> destinationTriangles)
        {
            destinationTriangles.Add(destinationVertices.Count);
            destinationVertices.Add(sourceVertices[sourceIndex]);
            destinationUVs.Add(sourceUVs[sourceIndex]);
            destinationColors.Add(sourceColors[sourceIndex]);
        }

        private static Vector3 Deform(
            Vector3 direction,
            DeformationContext context,
            out int cutMask,
            out Color surfaceData)
        {
            var lowNoise = Fbm(direction * 1.28f + context.NoiseOffset, context.Seed + 17, 3) * 2.0f - 1.0f;
            var secondaryNoise = Fbm(direction * 2.67f - context.NoiseOffset * 0.37f, context.Seed + 101, 3) * 2.0f - 1.0f;
            var ridgeNoise = RidgedFbm(direction * 4.35f + context.RidgeOffset, context.Seed + 233, 3);
            var smallNoise = Fbm(direction * 9.7f + context.NoiseOffset * 0.19f, context.Seed + 419, 2) * 2.0f - 1.0f;

            var asymmetricBulge = Mathf.Max(Vector3.Dot(direction, context.BulgeAxis), -0.32f);
            asymmetricBulge = Mathf.Sign(asymmetricBulge) * asymmetricBulge * asymmetricBulge;

            var crackMask = CalculateGeometricCracks(direction, context);
            var sideMask = Mathf.Pow(Mathf.Clamp01(1.0f - Mathf.Abs(direction.y)), 1.35f);
            var strataPhase = direction.y * context.StrataFrequency + lowNoise * 1.45f + context.StrataPhase;
            var strataDistance = Mathf.Abs(Mathf.Sin(strataPhase));
            var strataGroove = 1.0f - SmoothStep(0.035f, 0.24f, strataDistance);

            var radius = 1.0f;
            radius += context.Ruggedness * lowNoise * 0.19f;
            radius += context.Ruggedness * secondaryNoise * 0.075f;
            radius += context.Ruggedness * (ridgeNoise - 0.48f) * 0.17f;
            radius += context.Ruggedness * smallNoise * 0.032f;
            radius += context.Ruggedness * asymmetricBulge * 0.16f;
            radius -= context.Ruggedness * crackMask * 0.075f;
            radius -= context.Ruggedness * strataGroove * sideMask * 0.025f;
            radius = Mathf.Max(radius, 0.57f);

            var position = Vector3.Scale(direction * radius, context.Proportions);

            // Weak shear prevents the top and base from sharing the same center line.
            position.x += position.y * context.Lean.x;
            position.z += position.y * context.Lean.y;

            // Horizontal terraces are intentionally geometric rather than a normal-map
            // illusion. Noise varies their strength so they do not resemble perfect rings.
            var terraceCoordinate = (position.y + context.TerraceOffset) / context.TerraceStep;
            var terracedHeight = Mathf.Round(terraceCoordinate) * context.TerraceStep - context.TerraceOffset;
            var terraceVariation = SmoothStep(0.30f, 0.82f, ridgeNoise);
            var terraceWeight = sideMask * terraceVariation * context.Ruggedness * 0.20f;
            position.y = Mathf.Lerp(position.y, terracedHeight, Mathf.Clamp01(terraceWeight));

            cutMask = 0;
            for (var cutIndex = 0; cutIndex < context.CutNormals.Length; cutIndex++)
            {
                var cutNormal = context.CutNormals[cutIndex];
                var ellipsoidSupport = Vector3.Scale(cutNormal, context.Proportions).magnitude;
                var cutDistance = ellipsoidSupport * context.CutRatios[cutIndex];
                var excess = Vector3.Dot(position, cutNormal) - cutDistance;
                if (excess <= 0.0f)
                    continue;

                position -= cutNormal * excess;
                cutMask |= 1 << cutIndex;
            }

            // A subtle broken base lets the rock sit convincingly on seabed geometry.
            var baseHeight = -context.Proportions.y * context.BaseRatio;
            if (position.y < baseHeight)
            {
                position.y = baseHeight;
                cutMask |= 1 << context.CutNormals.Length;
            }

            surfaceData = new Color(
                Mathf.InverseLerp(-context.Proportions.y, context.Proportions.y, position.y),
                strataGroove,
                crackMask,
                1.0f);
            return position;
        }

        private static float CalculateGeometricCracks(Vector3 direction, DeformationContext context)
        {
            var strongestCrack = 0.0f;
            for (var crackIndex = 0; crackIndex < context.CrackNormals.Length; crackIndex++)
            {
                var alongCrack = Vector3.Dot(direction, context.CrackTangents[crackIndex]);
                var wandering = Mathf.Sin(alongCrack * context.CrackFrequencies[crackIndex]
                    + context.CrackPhases[crackIndex]) * 0.055f;
                var distance = Mathf.Abs(Vector3.Dot(direction, context.CrackNormals[crackIndex]) + wandering);
                var narrowGroove = 1.0f - SmoothStep(0.012f, 0.052f, distance);
                var verticalGate = SmoothStep(-0.82f, -0.45f, direction.y)
                    * (1.0f - SmoothStep(0.72f, 0.94f, direction.y));
                strongestCrack = Mathf.Max(strongestCrack, narrowGroove * verticalGate);
            }
            return strongestCrack;
        }

        private static void CreateIcosphere(int subdivisions, List<Vector3> vertices, List<int> triangles)
        {
            var goldenRatio = (1.0f + Mathf.Sqrt(5.0f)) * 0.5f;
            vertices.AddRange(new[]
            {
                new Vector3(-1, goldenRatio, 0).normalized,
                new Vector3(1, goldenRatio, 0).normalized,
                new Vector3(-1, -goldenRatio, 0).normalized,
                new Vector3(1, -goldenRatio, 0).normalized,
                new Vector3(0, -1, goldenRatio).normalized,
                new Vector3(0, 1, goldenRatio).normalized,
                new Vector3(0, -1, -goldenRatio).normalized,
                new Vector3(0, 1, -goldenRatio).normalized,
                new Vector3(goldenRatio, 0, -1).normalized,
                new Vector3(goldenRatio, 0, 1).normalized,
                new Vector3(-goldenRatio, 0, -1).normalized,
                new Vector3(-goldenRatio, 0, 1).normalized
            });

            triangles.AddRange(new[]
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1
            });

            for (var level = 0; level < subdivisions; level++)
            {
                var midpointCache = new Dictionary<long, int>();
                var refinedTriangles = new List<int>(triangles.Count * 4);

                for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex += 3)
                {
                    var a = triangles[triangleIndex];
                    var b = triangles[triangleIndex + 1];
                    var c = triangles[triangleIndex + 2];
                    var ab = GetMidpoint(a, b, vertices, midpointCache);
                    var bc = GetMidpoint(b, c, vertices, midpointCache);
                    var ca = GetMidpoint(c, a, vertices, midpointCache);

                    refinedTriangles.AddRange(new[]
                    {
                        a, ab, ca,
                        b, bc, ab,
                        c, ca, bc,
                        ab, bc, ca
                    });
                }

                triangles.Clear();
                triangles.AddRange(refinedTriangles);
            }
        }

        private static int GetMidpoint(
            int indexA,
            int indexB,
            ICollection<Vector3> vertices,
            IDictionary<long, int> midpointCache)
        {
            var minimum = Mathf.Min(indexA, indexB);
            var maximum = Mathf.Max(indexA, indexB);
            var key = ((long)minimum << 32) | (uint)maximum;
            if (midpointCache.TryGetValue(key, out var midpointIndex))
                return midpointIndex;

            var vertexList = (List<Vector3>)vertices;
            var midpoint = ((vertexList[indexA] + vertexList[indexB]) * 0.5f).normalized;
            midpointIndex = vertexList.Count;
            vertexList.Add(midpoint);
            midpointCache.Add(key, midpointIndex);
            return midpointIndex;
        }

        private static Vector2 ToSphericalUV(Vector3 direction)
        {
            return new Vector2(
                Mathf.Atan2(direction.z, direction.x) / (Mathf.PI * 2.0f) + 0.5f,
                Mathf.Asin(Mathf.Clamp(direction.y, -1.0f, 1.0f)) / Mathf.PI + 0.5f);
        }

        private static float Fbm(Vector3 position, int seed, int octaves)
        {
            var value = 0.0f;
            var amplitude = 0.57f;
            var amplitudeSum = 0.0f;
            for (var octave = 0; octave < octaves; octave++)
            {
                value += ValueNoise(position, seed + octave * 131) * amplitude;
                amplitudeSum += amplitude;
                position = new Vector3(
                    position.z * 1.91f + position.x * 0.23f,
                    position.x * -1.73f + position.y * 0.31f,
                    position.y * 1.87f - position.z * 0.19f);
                amplitude *= 0.48f;
            }
            return value / Mathf.Max(amplitudeSum, 0.0001f);
        }

        private static float RidgedFbm(Vector3 position, int seed, int octaves)
        {
            var value = 0.0f;
            var amplitude = 0.60f;
            var amplitudeSum = 0.0f;
            for (var octave = 0; octave < octaves; octave++)
            {
                var noise = ValueNoise(position, seed + octave * 173) * 2.0f - 1.0f;
                var ridge = 1.0f - Mathf.Abs(noise);
                ridge *= ridge;
                value += ridge * amplitude;
                amplitudeSum += amplitude;
                position = position * 2.08f + new Vector3(1.71f, -2.13f, 0.83f);
                amplitude *= 0.46f;
            }
            return value / Mathf.Max(amplitudeSum, 0.0001f);
        }

        private static float ValueNoise(Vector3 position, int seed)
        {
            var x0 = Mathf.FloorToInt(position.x);
            var y0 = Mathf.FloorToInt(position.y);
            var z0 = Mathf.FloorToInt(position.z);
            var tx = Quintic(position.x - x0);
            var ty = Quintic(position.y - y0);
            var tz = Quintic(position.z - z0);

            var x00 = Mathf.Lerp(Hash01(x0, y0, z0, seed), Hash01(x0 + 1, y0, z0, seed), tx);
            var x10 = Mathf.Lerp(Hash01(x0, y0 + 1, z0, seed), Hash01(x0 + 1, y0 + 1, z0, seed), tx);
            var x01 = Mathf.Lerp(Hash01(x0, y0, z0 + 1, seed), Hash01(x0 + 1, y0, z0 + 1, seed), tx);
            var x11 = Mathf.Lerp(Hash01(x0, y0 + 1, z0 + 1, seed), Hash01(x0 + 1, y0 + 1, z0 + 1, seed), tx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, ty), Mathf.Lerp(x01, x11, ty), tz);
        }

        private static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                var hash = 2166136261u;
                hash = (hash ^ (uint)x) * 16777619u;
                hash = (hash ^ (uint)y) * 16777619u;
                hash = (hash ^ (uint)z) * 16777619u;
                hash = (hash ^ (uint)seed) * 16777619u;
                hash ^= hash >> 16;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                return (hash & 0x00ffffffu) / 16777215.0f;
            }
        }

        private static float Quintic(float value)
        {
            return value * value * value * (value * (value * 6.0f - 15.0f) + 10.0f);
        }

        private static float SmoothStep(float minimum, float maximum, float value)
        {
            var normalized = Mathf.Clamp01((value - minimum) / Mathf.Max(maximum - minimum, 0.00001f));
            return normalized * normalized * (3.0f - 2.0f * normalized);
        }

        private static Vector3 SanitizeProportions(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(Mathf.Abs(value.x), 0.1f),
                Mathf.Max(Mathf.Abs(value.y), 0.1f),
                Mathf.Max(Mathf.Abs(value.z), 0.1f));
        }

        private static int CalculateSignature(int seed, int subdivisions, Vector3 proportions, float ruggedness)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + seed;
                hash = hash * 31 + subdivisions;
                hash = hash * 31 + proportions.x.GetHashCode();
                hash = hash * 31 + proportions.y.GetHashCode();
                hash = hash * 31 + proportions.z.GetHashCode();
                hash = hash * 31 + ruggedness.GetHashCode();
                return hash == 0 ? 1 : hash;
            }
        }

        private sealed class DeformationContext
        {
            public readonly int Seed;
            public readonly Vector3 Proportions;
            public readonly float Ruggedness;
            public readonly Vector3 NoiseOffset;
            public readonly Vector3 RidgeOffset;
            public readonly Vector3 BulgeAxis;
            public readonly Vector2 Lean;
            public readonly float StrataFrequency;
            public readonly float StrataPhase;
            public readonly float TerraceStep;
            public readonly float TerraceOffset;
            public readonly float BaseRatio;
            public readonly Vector3[] CutNormals = new Vector3[3];
            public readonly float[] CutRatios = new float[3];
            public readonly Vector3[] CrackNormals = new Vector3[3];
            public readonly Vector3[] CrackTangents = new Vector3[3];
            public readonly float[] CrackFrequencies = new float[3];
            public readonly float[] CrackPhases = new float[3];

            public DeformationContext(int seed, Vector3 proportions, float ruggedness)
            {
                Seed = seed;
                Proportions = proportions;
                Ruggedness = ruggedness;

                var random = new DeterministicRandom(seed);
                NoiseOffset = random.NextVector3(-7.0f, 7.0f);
                RidgeOffset = random.NextVector3(-5.0f, 5.0f);
                BulgeAxis = random.NextUnitVector(-0.45f, 0.75f);
                Lean = new Vector2(random.Next(-0.075f, 0.075f), random.Next(-0.075f, 0.075f));
                StrataFrequency = random.Next(15.5f, 22.0f);
                StrataPhase = random.Next(0.0f, Mathf.PI * 2.0f);
                TerraceStep = random.Next(0.135f, 0.205f) * proportions.y;
                TerraceOffset = random.Next(-0.5f, 0.5f) * TerraceStep;
                BaseRatio = random.Next(0.73f, 0.82f);

                for (var index = 0; index < CutNormals.Length; index++)
                {
                    CutNormals[index] = random.NextUnitVector(-0.32f, 0.72f);
                    CutRatios[index] = random.Next(0.70f, 0.86f);
                }

                for (var index = 0; index < CrackNormals.Length; index++)
                {
                    var normal = random.NextUnitVector(-0.75f, 0.75f);
                    var helper = Mathf.Abs(normal.y) < 0.82f ? Vector3.up : Vector3.right;
                    CrackNormals[index] = normal;
                    CrackTangents[index] = Vector3.Cross(normal, helper).normalized;
                    CrackFrequencies[index] = random.Next(7.0f, 13.0f);
                    CrackPhases[index] = random.Next(0.0f, Mathf.PI * 2.0f);
                }
            }
        }

        private struct DeterministicRandom
        {
            private uint state;

            public DeterministicRandom(int seed)
            {
                state = unchecked((uint)seed) ^ 0x9e3779b9u;
                if (state == 0)
                    state = 0x6d2b79f5u;
            }

            public float Next(float minimum, float maximum)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var unitValue = (state & 0x00ffffffu) / 16777215.0f;
                return Mathf.Lerp(minimum, maximum, unitValue);
            }

            public Vector3 NextVector3(float minimum, float maximum)
            {
                return new Vector3(Next(minimum, maximum), Next(minimum, maximum), Next(minimum, maximum));
            }

            public Vector3 NextUnitVector(float minimumY, float maximumY)
            {
                var azimuth = Next(0.0f, Mathf.PI * 2.0f);
                var y = Next(minimumY, maximumY);
                var horizontal = Mathf.Sqrt(Mathf.Max(1.0f - y * y, 0.0f));
                return new Vector3(Mathf.Cos(azimuth) * horizontal, y, Mathf.Sin(azimuth) * horizontal).normalized;
            }
        }
    }
}
