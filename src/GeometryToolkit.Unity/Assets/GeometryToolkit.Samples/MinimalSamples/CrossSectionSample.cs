using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.Samples
{
    public sealed class CrossSectionSample : MonoBehaviour
    {
        void Start()
        {
            var plane = new Plane(Vector3.up, new Vector3(0, 0, 0));

            var unitSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphereMesh = unitSphere.GetComponent<MeshFilter>().mesh;

            var meshData = MeshDataBuffer.Create(sphereMesh, unitSphere.transform);
            var meshCutData = MeshCutter.Execute(new List<MeshDataBuffer>(){ meshData }, plane);

            var contours = ContourLoopExtractor.Execute(meshCutData);
            var crossSection = CrossSection.Generate(contours[0]);

            Debug.Log($"Perimeter: {crossSection.Perimeter} [m], Area: {crossSection.SignedArea} [m²]");
        }
    }
}
