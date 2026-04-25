using UnityEngine;

namespace GeometryToolkit
{
    public static class TorusMeshGenerator
    {
        /// <summary>
        /// Generate a torus mesh
        /// </summary>
        /// <param name="majorRadius">Major radius (distance from the center of the torus to the center of the tube)</param>
        /// <param name="minorRadius">Minor radius (radius of the tube thickness)</param>
        /// <param name="segments">Number of segments around the circumference</param>
        /// <param name="tubeSegments">Number of segments around the tube cross-section</param>
        /// <returns>Generated torus mesh</returns>
        public static Mesh Generate(float majorRadius, float minorRadius, int segments, int tubeSegments)
        {
            if (majorRadius <= 0 || minorRadius <= 0 || segments < 3 || tubeSegments < 3)
            {
                var message = "Invalid parameters for torus mesh generation. " +
                              "Ensure that majorRadius and minorRadius are positive, and segments and tubeSegments are at least 3.";
                throw new System.ArgumentException(message);
            }

            var mesh = new Mesh();
            mesh.name = "Torus";

            var vertexCount = (segments + 1) * (tubeSegments + 1);
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var triangles = new int[segments * tubeSegments * 6]; // 6 vertices per segment (2 triangles)

            // Generate vertices, normals, and UV coordinates
            for (var i = 0; i <= segments; i++)
            {
                // Angle around the major circumference
                var u = (float)i / segments * Mathf.PI * 2.0f;
                
                for (var j = 0; j <= tubeSegments; j++)
                {
                    // Angle around the minor circumference (cross-section)
                    var v = (float)j / tubeSegments * Mathf.PI * 2.0f;
                    
                    // Vertex index
                    var vertexIndex = i * (tubeSegments + 1) + j;
                    
                    // Calculate vertex position
                    var x = (majorRadius + minorRadius * Mathf.Cos(v)) * Mathf.Cos(u);
                    var y = minorRadius * Mathf.Sin(v);
                    var z = (majorRadius + minorRadius * Mathf.Cos(v)) * Mathf.Sin(u);
                    vertices[vertexIndex] = new Vector3(x, y, z);
                    
                    // Calculate normal vector (direction from the center of the tube to the vertex)
                    var centerOfTube = new Vector3(majorRadius * Mathf.Cos(u), 0, majorRadius * Mathf.Sin(u));
                    normals[vertexIndex] = (vertices[vertexIndex] - centerOfTube).normalized;
                    
                    // Set UV coordinates
                    uvs[vertexIndex] = new Vector2((float)i / segments, (float)j / tubeSegments);
                }
            }

            // Generate triangle indices
            for (var i = 0; i < segments; i++)
            {
                for (var j = 0; j < tubeSegments; j++)
                {
                    var triangleIndex = (i * tubeSegments + j) * 6;
                    var currentVertex = i * (tubeSegments + 1) + j;

                    // First triangle
                    triangles[triangleIndex] = currentVertex;
                    triangles[triangleIndex + 1] = currentVertex + 1;
                    triangles[triangleIndex + 2] = currentVertex + tubeSegments + 1;

                    // Second triangle
                    triangles[triangleIndex + 3] = currentVertex + 1;
                    triangles[triangleIndex + 4] = currentVertex + tubeSegments + 2;
                    triangles[triangleIndex + 5] = currentVertex + tubeSegments + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uvs;

            return mesh;
        }
    }
}
