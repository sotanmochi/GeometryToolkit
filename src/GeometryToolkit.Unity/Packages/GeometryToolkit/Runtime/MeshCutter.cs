using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace GeometryToolkit
{
    public static class MeshCutter
    {
        public static MeshCutData Execute(List<MeshDataBuffer> targets, Plane plane)
        {
            var output = new MeshCutData(plane);

            foreach (var target in targets)
            {
                var vertexCount = target.Vertices.Length;
                var vertexBuffer = ArrayPool<Vector3>.Shared.Rent(vertexCount);

                try
                {
                    for (var i = 0; i < vertexCount; i++)
                    {
                        vertexBuffer[i] = target.LocalToWorldMatrix.MultiplyPoint3x4(target.Vertices[i]);
                    }
                    Execute(vertexBuffer.AsSpan(0, vertexCount), target.Triangles, plane, ref output);
                }
                finally
                {
                    ArrayPool<Vector3>.Shared.Return(vertexBuffer);
                }
            }

            return output;
        }

        public static void Execute(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> triangles, Plane plane, ref MeshCutData output)
        {
            Profiler.BeginSample("MeshCutter.Execute");

            if (vertices.Length < 3 || triangles.Length < 3)
            {
                throw new ArgumentException("Invalid input data provided for mesh cutting. The vertices or triangles are too few.");
            }

            Span<Vector3> intersectionPoints = stackalloc Vector3[3];

            for (var i = 0; i < triangles.Length; i += 3)
            {
                var v1 = vertices[triangles[i]];
                var v2 = vertices[triangles[i + 1]];
                var v3 = vertices[triangles[i + 2]];

                if (FindIntersectionLine(v1, v2, v3, plane, out var intersectionPointCount, intersectionPoints))
                {
                    var triangleNormal = Vector3.Cross(v2 - v1, v3 - v1).normalized;
                    var direction = Vector3.Cross(plane.normal, triangleNormal).normalized;

                    var point1 = intersectionPoints[0];
                    var point2 = intersectionPoints[1];
                    if (Vector3.Dot(point2 - point1, direction) < 0)
                    {
                        point1 = intersectionPoints[1];
                        point2 = intersectionPoints[0];
                    }

                    var pointId1 = output.AddIntersectionPoint(point1);
                    var pointId2 = output.AddIntersectionPoint(point2);
                    output.AddIntersectionLine(new LineSegment(pointId1, pointId2));
                }
            }

            Profiler.EndSample();
        }

        private const float Epsilon = 0.00001f;

        public static bool FindIntersectionLine(Vector3 p1, Vector3 p2, Vector3 p3, Plane plane, out int intersectionPointCount, Span<Vector3> intersectionPoints)
        {
            if (intersectionPoints.Length != 3)
            {
                throw new ArgumentException("The length of output buffer must be 3.");
            }

            intersectionPointCount = 0;
            intersectionPoints[0] = Vector3.zero;
            intersectionPoints[1] = Vector3.zero;
            intersectionPoints[2] = Vector3.zero;

            var signedDist1 = plane.GetDistanceToPoint(p1);
            var signedDist2 = plane.GetDistanceToPoint(p2);
            var signedDist3 = plane.GetDistanceToPoint(p3);
            var dist1 = Mathf.Abs(signedDist1);
            var dist2 = Mathf.Abs(signedDist2);
            var dist3 = Mathf.Abs(signedDist3);

            // Check if the points exist on the plane.
            if (dist1 < Epsilon)
            {
                intersectionPoints[intersectionPointCount++] = p1;
            }
            if (dist2 < Epsilon)
            {
                intersectionPoints[intersectionPointCount++] = p2;
            }
            if (dist3 < Epsilon)
            {
                intersectionPoints[intersectionPointCount++] = p3;
            }

            if (intersectionPointCount >= 2)
            {
                return intersectionPointCount == 2;
            }

            // Check if the edges cross the plane.
            if (signedDist1 * signedDist2 < 0 && dist1 >= Epsilon && dist2 >= Epsilon)
            {
                var t = dist1 / (dist1 + dist2);
                intersectionPoints[intersectionPointCount++] = Vector3.Lerp(p1, p2, t);
            }
            if (signedDist2 * signedDist3 < 0 && dist2 >= Epsilon && dist3 >= Epsilon)
            {
                var t = dist2 / (dist2 + dist3);
                intersectionPoints[intersectionPointCount++] = Vector3.Lerp(p2, p3, t);
            }
            if (signedDist3 * signedDist1 < 0 && dist3 >= Epsilon && dist1 >= Epsilon)
            {
                var t = dist3 / (dist3 + dist1);
                intersectionPoints[intersectionPointCount++] = Vector3.Lerp(p3, p1, t);
            }

            return intersectionPointCount == 2;
        }
    }
}
