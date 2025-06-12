# GeometryToolkit

GeometryToolkitはUnity向けのメッシュ処理ライブラリです。

## Features
**MeshCutter**: メッシュを平面で切断して交点と交線を取得します。

**ContourLoopExtractor**: メッシュの切断面の輪郭ループを抽出します。

```csharp
var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
var mesh = obj.GetComponent<MeshFilter>().mesh;

var meshData = MeshDataBuffer.Create(mesh, obj.transform);
var meshCutData = MeshCutter.Execute(new List<MeshDataBuffer>(){ meshData }, new Plane(Vector3.up, new Vector3(0, 0, 0)));

var contours = ContourLoopExtractor.Execute(meshCutData);

var crossSection = CrossSection.Generate(contours[0]);

Debug.Log($"Perimeter: {crossSection.Perimeter} [m], Area: {crossSection.SignedArea} [m²], Centroid: {crossSection.Centroid}");
```
