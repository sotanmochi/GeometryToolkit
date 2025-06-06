using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace GeometryToolkit
{
    public static class ContourLoopExtractor
    {
        public static List<ContourLoop> Execute(MeshCutData meshCutData)
        {
            return Execute(meshCutData.IntersectionPoints, meshCutData.IntersectionLines, meshCutData.CuttingPlane.normal);
        }

        public static List<ContourLoop> Execute(IReadOnlyList<Vector3> points, HashSet<LineSegment> lines, Vector3 cuttingPlaneNormal)
        {
            Profiler.BeginSample("ContourLoopExtractor.Execute");

            var contourLoops = new List<ContourLoop>();

            if (points == null || lines == null || points.Count < 3 || lines.Count < 3)
            {
                return contourLoops;
            }

            var pointIdToLinesMap = new Dictionary<int, List<LineSegment>>();
            foreach (var line in lines)
            {
                if (!pointIdToLinesMap.ContainsKey(line.PointId1))
                {
                    pointIdToLinesMap[line.PointId1] = new List<LineSegment>();
                }

                if (!pointIdToLinesMap.ContainsKey(line.PointId2))
                {
                    pointIdToLinesMap[line.PointId2] = new List<LineSegment>();
                }

                pointIdToLinesMap[line.PointId1].Add(line);
                pointIdToLinesMap[line.PointId2].Add(line);
            }

            var visitedLines = new HashSet<LineSegment>(lines.Count);
            var loopPointIdBuffer = ArrayPool<int>.Shared.Rent(2 * lines.Count);

            try
            {
                foreach (var line in lines)
                {
                    if (visitedLines.Contains(line))
                    {
                        continue; // Skip visited lines.
                    }

                    if (TraceContourLoop(line, pointIdToLinesMap, visitedLines, out var loopPointCount, loopPointIdBuffer))
                    {
                        var contourLoop = new ContourLoop(capacity: loopPointCount);
                        foreach (var pointId in loopPointIdBuffer.AsSpan(0, loopPointCount))
                        {
                            contourLoop.AddPoint(points[pointId]);
                        }

                        contourLoop.UpdatePerimeter();
                        contourLoop.UpdateSignedArea(cuttingPlaneNormal);

                        contourLoops.Add(contourLoop);
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(loopPointIdBuffer);
            }

            Profiler.EndSample();
            return contourLoops;
        }
        
        private static bool TraceContourLoop(LineSegment startLine, Dictionary<int, List<LineSegment>> pointIdToLinesMap, 
                                            HashSet<LineSegment> visitedLines, out int loopPointCount, Span<int> loopPoints)
        {
            loopPointCount = 0;
            loopPoints.Clear();

            loopPoints[loopPointCount++] = startLine.PointId1;
            loopPoints[loopPointCount++] = startLine.PointId2;
            visitedLines.Add(startLine);

            var startPointId = startLine.PointId1;
            var endPointId = startLine.PointId2;

            var iteration = 0;
            var maxIteration = pointIdToLinesMap.Values.Sum(list => list.Count);

            while (iteration < maxIteration)
            {
                var nextLines = pointIdToLinesMap[endPointId];

                // If the next line is not found, stop tracing.
                if (nextLines == null || nextLines.Count == 0)
                {
                    break;
                }
                // If only one line is left and it is already visited, stop tracing.
                if (nextLines.Count == 1 && visitedLines.Contains(nextLines[0]))
                {
                    break;
                }

                foreach (var nextLine in nextLines)
                {
                    if (visitedLines.Contains(nextLine))
                    {
                        continue; // Skip visited lines.
                    }

                    // Check connection.
                    if (nextLine.PointId1 == endPointId || nextLine.PointId2 == endPointId)
                    {
                        endPointId = endPointId != nextLine.PointId1 ? nextLine.PointId1 : nextLine.PointId2;
                        loopPoints[loopPointCount++] = endPointId;
                        visitedLines.Add(nextLine);
                    }

                    // If the loop is closed, finish tracing.
                    if (endPointId == startPointId)
                    {
                        return true;
                    }
                }

                iteration++;
            }
            
            return endPointId == startPointId;
        }
    }
}
