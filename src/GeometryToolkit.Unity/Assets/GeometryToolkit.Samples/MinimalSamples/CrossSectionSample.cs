using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.Samples
{
    public sealed class CrossSectionSample : MonoBehaviour
    {
        void Start()
        {
            var plane = new Plane(Vector3.up, new Vector3(0, 0, 0));

            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = obj.GetComponent<MeshFilter>().mesh;

            var meshData = MeshDataBuffer.Create(mesh, obj.transform);
            var meshCutData = MeshCutter.Execute(new List<MeshDataBuffer>(){ meshData }, plane);

            var contours = ContourLoopExtractor.Execute(meshCutData);
            var crossSection = CrossSection.Generate(contours[0]);

            Debug.Log($"Perimeter: {crossSection.Perimeter} [m], Area: {crossSection.SignedArea} [m²], Centroid: {crossSection.Centroid}");
        }
    }
}
