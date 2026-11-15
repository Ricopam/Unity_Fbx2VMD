using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        // 공동 투영의 제약·버퍼·로컬 함수 상태를 한 곳에 둔 세션.
        private sealed class ProjectionSession
        {
            private readonly IReadOnlyList<Vector3> baseline;
            private readonly NativeSkinningSurfaceContract contract;
            private readonly IReadOnlyList<Vector3> seed;
            private readonly LocalWorkset workset;
            private readonly double maxStrain;
            private readonly double minLength;
            private readonly double maxLength;
            private readonly double minCosine;
            private readonly double maxDisplacementSquared;

            private LocalEdge[] edges;
            private NativeSkinningFacePair[] pairs;
            private List<(int First, int Second, int Third, double3 Reference)> faces;
            private int[] vertices;
            private int[] local;
            private JointProjectionRow[] rows;
            private double[] multipliers;
            private double[] search;
            private double3[] point;
            private double3[] proposal;
            private double3[] trial;
            private double3[] columnSums;

            internal ProjectionSession(
                IReadOnlyList<Vector3> baseline,
                NativeSkinningSurfaceContract contract,
                IReadOnlyList<Vector3> seed,
                LocalWorkset workset)
            {
                this.baseline = baseline;
                this.contract = contract;
                this.seed = seed;
                this.workset = workset;
                maxStrain = StrongConfiguration.MaximumEdgeLengthStrain - EdgeLengthProjectionMargin;
                minLength = 1 - maxStrain;
                maxLength = 1 + maxStrain;
                minCosine = Math.Cos((SharpFoldAngleDegrees - JointProjectionAngleMargin) * Math.PI / 180);
                double maxDisplacement = contract.ArmChainLength *
                    StrongConfiguration.MaximumTotalDisplacementToArmLengthRatio +
                    QualityComparisonTolerance;
                maxDisplacementSquared = maxDisplacement * maxDisplacement;
            }

            // 제약 대상 정점·엣지·쌍·면을 수집한다. 대상이 없으면 false.
            internal bool TrySetup()
            {
                edges = workset.Edges.Where(edge => edge.BaselineLength > 1e-7f).ToArray();
                pairs = workset.FacePairIndices
                    .Where(pair => IsSharpFoldCorrectionEligible(baseline, contract, pair))
                    .Select(pair => contract.FacePairs[pair]).ToArray();
                var active = CollectActiveConstraintVertices();
                vertices = workset.ConstrainedVertices.Where(active.Contains).ToArray();
                if (vertices.Length == 0)
                {
                    return false;
                }
                local = new int[baseline.Count];
                Array.Fill(local, -1);
                for (int index = 0; index < vertices.Length; index++)
                {
                    local[vertices[index]] = index;
                }
                edges = edges.Where(edge => local[edge.FirstVertex] >= 0 ||
                    local[edge.SecondVertex] >= 0).ToArray();
                pairs = pairs.Where(pair => local[pair.FirstOpposite] >= 0 ||
                    local[pair.SecondOpposite] >= 0 ||
                    local[pair.FirstEdge] >= 0 || local[pair.SecondEdge] >= 0).ToArray();
                faces = CollectFaces();
                AllocateBuffers();
                return true;
            }

            // 신장·폴드 위반 정점을 모아 링 이웃까지 확장한다.
            private HashSet<int> CollectActiveConstraintVertices()
            {
                var active = new HashSet<int>();
                foreach (LocalEdge edge in edges)
                {
                    double length = math.distance(ToDouble(seed[edge.FirstVertex]), ToDouble(seed[edge.SecondVertex]));
                    if (Math.Abs(length / edge.BaselineLength - 1) > maxStrain)
                    {
                        active.Add(edge.FirstVertex);
                        active.Add(edge.SecondVertex); } }
                CollectFoldViolations(active);
                for (int ring = 0; ring < JointProjectionRingCount; ring++)
                {
                    var frontier = new HashSet<int>(active);
                    foreach (LocalEdge edge in edges)
                    {
                        if (frontier.Contains(edge.FirstVertex) || frontier.Contains(edge.SecondVertex))
                        {
                            active.Add(edge.FirstVertex);
                            active.Add(edge.SecondVertex); } } }
                return active; }

            // 폴드 코사인이 임계 아래인 면 쌍의 네 정점을 활성 집합에 추가한다.
            void CollectFoldViolations(HashSet<int> active)
            {
                foreach (NativeSkinningFacePair pair in pairs)
                {
                    double3 first = ToDouble(seed[pair.FirstOpposite]);
                    double3 second = ToDouble(seed[pair.SecondOpposite]);
                    double3 edge0 = ToDouble(seed[pair.FirstEdge]);
                    double3 edge1 = ToDouble(seed[pair.SecondEdge]);
                    if (MeasureFoldCosine(first, second, edge0, edge1, out _, out _) < minCosine)
                    {
                        active.Add(pair.FirstOpposite);
                        active.Add(pair.SecondOpposite);
                        active.Add(pair.FirstEdge);
                        active.Add(pair.SecondEdge); } }
            }

            private List<(int First, int Second, int Third, double3 Reference)> CollectFaces()
            {
                var collected = new List<(int First, int Second, int Third, double3 Reference)>();
                foreach (int face in workset.FaceIndices)
                {
                    int offset = face * 3;
                    int first = contract.Triangles[offset];
                    int second = contract.Triangles[offset + 1];
                    int third = contract.Triangles[offset + 2];
                    if (local[first] < 0 && local[second] < 0 && local[third] < 0)
                    {
                        continue;
                    }
                    double3 normal = FaceNormal(
                        ToDouble(baseline[first]), ToDouble(baseline[second]), ToDouble(baseline[third]));
                    double lengthSquared = math.lengthsq(normal);
                    if (lengthSquared >= MinimumNormalSquaredMagnitude)
                    {
                        collected.Add((first, second, third, normal / lengthSquared));
                    }
                }
                return collected;
            }

            private void AllocateBuffers()
            {
                rows = Enumerable.Range(0, 2 * edges.Length + pairs.Length + faces.Count + vertices.Length)
                    .Select(_ => new JointProjectionRow()).ToArray();
                multipliers = new double[rows.Length];
                search = new double[rows.Length];
                point = new double3[vertices.Length];
                proposal = new double3[vertices.Length];
                trial = new double3[vertices.Length];
                columnSums = new double3[vertices.Length];
            }

            // 각도와 길이 한도를 같은 선형 제약으로 풀어 한쪽 투영이 다른 쪽의 개선을 되돌리지 않게 함.
            internal void Solve()
            {
                for (int round = 0; round < JointProjectionOuterIterations; round++)
                {
                    double violation = Measure(point, linearize: true);
                    if (!double.IsFinite(violation) || violation >= -JointProjectionTolerance ||
                        !ProjectLinearConstraints(rows, multipliers, search, proposal, columnSums))
                    {
                        break;
                    }
                    bool accepted = false;
                    double fraction = 1;
                    for (int step = 0; step < JointProjectionLineSearchSteps; step++, fraction *= 0.5)
                    {
                        for (int index = 0; index < trial.Length; index++)
                        {
                            trial[index] = math.lerp(point[index], proposal[index], fraction);
                        }
                        if (Measure(trial, linearize: false) > violation)
                        {
                            (point, trial) = (trial, point);
                            accepted = true;
                            break;
                        } }
                    if (!accepted)
                    {
                        break; } } }

            internal Vector3[] BuildCorrectedVertices()
            {
                Vector3[] corrected = seed.ToArray();
                for (int index = 0; index < vertices.Length; index++)
                {
                    int vertex = vertices[index];
                    double3 value = Position(vertex, point);
                    corrected[vertex] = new Vector3((float)value.x, (float)value.y, (float)value.z);
                }
                return corrected;
            }

            private double3 Position(int vertex, double3[] movement)
            {
                int index = local[vertex];
                double3 origin = ToDouble(seed[vertex]);
                return index >= 0 ? origin + movement[index] * JointProjectionPositionScale : origin;
            }

            private double Measure(double3[] movement, bool linearize)
            {
                int rowIndex = 0;
                double minimum = double.PositiveInfinity;
                MeasureEdgeBounds(movement, linearize, ref rowIndex, ref minimum);
                MeasurePairAngles(movement, linearize, ref rowIndex, ref minimum);
                MeasureFaceAreas(movement, linearize, ref rowIndex, ref minimum);
                MeasureDisplacementBounds(movement, linearize, ref rowIndex, ref minimum);
                return minimum;
            }

            private JointProjectionRow BeginRow(double value, double3[] movement, bool linearize,
                ref int rowIndex, ref double minimum)
            {
                minimum = Math.Min(minimum, value);
                JointProjectionRow row = rows[rowIndex++];
                if (linearize)
                {
                    row.Clear();
                }
                return row;
            }

            private void MeasureEdgeBounds(double3[] movement, bool linearize,
                ref int rowIndex, ref double minimum)
            {
                // 지역 함수는 ref 파라미터를 캡처할 수 없으므로 로컬에 누적한 뒤 되돌린다.
                int localRowIndex = rowIndex;
                double localMinimum = minimum;
                foreach (LocalEdge edge in edges)
                {
                    double3 difference = Position(edge.SecondVertex, movement) - Position(edge.FirstVertex, movement);
                    double lengthSquared = (double)edge.BaselineLength * edge.BaselineLength;
                    double ratioSquared = math.lengthsq(difference) / lengthSquared;
                    AddEdgeRow(ratioSquared - minLength * minLength, 2 * difference / lengthSquared);
                    AddEdgeRow(maxLength * maxLength - ratioSquared, -2 * difference / lengthSquared);

                    void AddEdgeRow(double value, double3 gradient)
                    {
                        JointProjectionRow row = BeginRow(value, movement, linearize, ref localRowIndex, ref localMinimum);
                        if (linearize)
                        {
                            row.Add(local[edge.FirstVertex], -gradient);
                            row.Add(local[edge.SecondVertex], gradient);
                            row.Normalize(value, movement);
                        }
                    }
                }
                rowIndex = localRowIndex;
                minimum = localMinimum;
            }

            private void MeasurePairAngles(double3[] movement, bool linearize,
                ref int rowIndex, ref double minimum)
            {
                foreach (NativeSkinningFacePair pair in pairs)
                {
                    double3 first = Position(pair.FirstOpposite, movement);
                    double3 second = Position(pair.SecondOpposite, movement);
                    double3 edge0 = Position(pair.FirstEdge, movement);
                    double3 edge1 = Position(pair.SecondEdge, movement);
                    double value = MeasureFoldCosine(first, second, edge0, edge1,
                        out double3 firstGradient, out double3 secondGradient) - minCosine;
                    JointProjectionRow row = BeginRow(value, movement, linearize, ref rowIndex, ref minimum);
                    if (linearize)
                    {
                        AddNormalGradient(row, local[pair.FirstOpposite], local[pair.FirstEdge],
                            local[pair.SecondEdge], first, edge0, edge1, firstGradient);
                        AddNormalGradient(row, local[pair.SecondOpposite], local[pair.SecondEdge],
                            local[pair.FirstEdge], second, edge1, edge0, secondGradient);
                        row.Normalize(value, movement);
                    }
                }
            }

            private void MeasureFaceAreas(double3[] movement, bool linearize,
                ref int rowIndex, ref double minimum)
            {
                foreach (var face in faces)
                {
                    double3 first = Position(face.First, movement);
                    double3 second = Position(face.Second, movement);
                    double3 third = Position(face.Third, movement);
                    double value = math.dot(FaceNormal(first, second, third), face.Reference) - MinimumSignedAreaRatio;
                    JointProjectionRow row = BeginRow(value, movement, linearize, ref rowIndex, ref minimum);
                    if (linearize)
                    {
                        AddNormalGradient(row, local[face.First], local[face.Second], local[face.Third],
                            first, second, third, face.Reference);
                        row.Normalize(value, movement);
                    }
                }
            }

            private void MeasureDisplacementBounds(double3[] movement, bool linearize,
                ref int rowIndex, ref double minimum)
            {
                for (int index = 0; index < vertices.Length; index++)
                {
                    int vertex = vertices[index];
                    double3 delta = Position(vertex, movement) - ToDouble(baseline[vertex]);
                    double value = 1 - math.lengthsq(delta) / maxDisplacementSquared;
                    JointProjectionRow row = BeginRow(value, movement, linearize, ref rowIndex, ref minimum);
                    if (linearize)
                    {
                        row.Add(index, -2 * delta / maxDisplacementSquared);
                        row.Normalize(value, movement);
                    }
                }
            }
        }
    }
}
