using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.Samples
{
    public sealed class CrossSectionVisualizerApp : MonoBehaviour
    {
        [SerializeField] private List<GameObject> _targetObjects;
        [SerializeField] private CrossSectionVisualizer _visualizer;
        [SerializeField] private VRMLoader _vrmLoader;
        [SerializeField] private MeshCutterPlaneControlView _meshCutterPlaneControlView;
        [SerializeField] private CrossSectionListView _crossSectionListView;

        private readonly Dictionary<int, List<MeshDataBuffer>> _instanceIdToMeshDataBuffers = new();
        private readonly List<MeshDataBuffer> _meshCutTargets = new();
        private readonly List<CrossSection> _currentCrossSections = new();

        private Vector3 _planePosition = new Vector3(0, 1, 0);
        private Vector3 _planeEulerAngles = Vector3.zero;

        private void Start()
        {
            _vrmLoader.ModelLoaded += AddTargetObject;
            _vrmLoader.ClearButtonClicked += RemoveTargetObject;
            _meshCutterPlaneControlView.PlaneTransformChanged += OnPlaneTransformChanged;
            _crossSectionListView.VisibilityChanged += OnCrossSectionVisibilityChanged;

            _meshCutterPlaneControlView.SetDefaultPlaneTransform(_planePosition, _planeEulerAngles);

            InitializeMeshDataBuffers();
            UpdateCrossSection();

            _vrmLoader.LoadVrmModel($"{Application.streamingAssetsPath}/VRM/AvatarSample_A.vrm");
        }

        private void OnDestroy()
        {
            _vrmLoader.ModelLoaded -= AddTargetObject;
            _vrmLoader.ClearButtonClicked -= RemoveTargetObject;
            _meshCutterPlaneControlView.PlaneTransformChanged -= OnPlaneTransformChanged;
            _crossSectionListView.VisibilityChanged -= OnCrossSectionVisibilityChanged;
        }

        private void UpdateCrossSection()
        {
            _meshCutTargets.Clear();
            foreach (var targetObject in _targetObjects)
            {
                if (targetObject == null) continue;

                var instanceId = targetObject.GetInstanceID();
                if (_instanceIdToMeshDataBuffers.TryGetValue(instanceId, out var meshDataBuffer))
                {
                    _meshCutTargets.AddRange(meshDataBuffer);
                }
            }

            var planeNormal = Quaternion.Euler(_planeEulerAngles) * Vector3.up;
            var meshCutData = MeshCutter.Execute(_meshCutTargets, new Plane(planeNormal, _planePosition));
            var contours = ContourLoopExtractor.Execute(meshCutData);

            _currentCrossSections.Clear();
            for (var i = 0; i < contours.Count; i++) // Avoid allocation caused by foreach loop.
            {
                _currentCrossSections.Add(CrossSection.Generate(contours[i]));
            }

            _visualizer.SetCrossSections(_currentCrossSections);
            _crossSectionListView.SetCrossSections(_currentCrossSections);
        }

        private void AddTargetObject(GameObject targetObject)
        {
            if (targetObject != null && !_targetObjects.Contains(targetObject))
            {
                _targetObjects.Add(targetObject);
                AddMeshDataBuffer(targetObject);
                UpdateCrossSection();
            }
        }

        private void RemoveTargetObject(GameObject targetObject)
        {
            if (targetObject != null && _targetObjects.Contains(targetObject))
            {
                _targetObjects.Remove(targetObject);
                _instanceIdToMeshDataBuffers.Remove(targetObject.GetInstanceID());
                UpdateCrossSection();
            }
        }

        private void InitializeMeshDataBuffers()
        {
            _instanceIdToMeshDataBuffers.Clear();
            foreach (var targetObject in _targetObjects)
            {
                if (targetObject == null) continue;
                AddMeshDataBuffer(targetObject);
            }
        }

        private void AddMeshDataBuffer(GameObject targetObject)
        {
            if (targetObject == null) return;

            var instanceId = targetObject.GetInstanceID();

            var skinnedMeshRenderers = targetObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (skinnedMeshRenderers.Length > 0)
            {
                foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
                {
                    if (skinnedMeshRenderer.sharedMesh != null)
                    {
                        if (!_instanceIdToMeshDataBuffers.TryGetValue(instanceId, out var meshDataBuffer))
                        {
                            meshDataBuffer = new List<MeshDataBuffer>();
                            _instanceIdToMeshDataBuffers[instanceId] = meshDataBuffer;
                        }
                        meshDataBuffer.Add(MeshDataBuffer.Create(skinnedMeshRenderer.sharedMesh, skinnedMeshRenderer.transform));
                    }
                }
            }
            else
            {
                var meshFilters = targetObject.GetComponentsInChildren<MeshFilter>();
                if (meshFilters.Length > 0)
                {
                    foreach (var meshFilter in meshFilters)
                    {
                        if (meshFilter.sharedMesh != null)
                        {
                            if (!_instanceIdToMeshDataBuffers.TryGetValue(instanceId, out var meshDataBuffer))
                            {
                                meshDataBuffer = new List<MeshDataBuffer>();
                                _instanceIdToMeshDataBuffers[instanceId] = meshDataBuffer;
                            }
                            meshDataBuffer.Add(MeshDataBuffer.Create(meshFilter.sharedMesh, meshFilter.transform));
                        }
                    }
                }
            }
        }

        private void OnPlaneTransformChanged(Vector3 position, Vector3 eulerAngles)
        {
            _planePosition = position;
            _planeEulerAngles = eulerAngles;
            UpdateCrossSection();
        }

        private void OnCrossSectionVisibilityChanged(int index, bool isVisible)
        {
            if (isVisible)
            {
                _visualizer.Show(index);
            }
            else
            {
                _visualizer.Hide(index);
            }
        }
    }
}
