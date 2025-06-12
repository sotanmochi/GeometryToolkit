using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using Cysharp.Threading.Tasks;
using SFB;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UniVRM10;

namespace GeometryToolkit.Samples
{
    // VrmLoader
    public class VRMLoader : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "UploadFile")]
        private static extern void OpenFilePanel(string gameObjectName, string methodName, string filter, bool multiple);
#endif

        [SerializeField] private UIDocument _uiDocument;

        private readonly List<GameObject> _loadedModels = new();

        private VisualElement _loadVrmButton;
        private VisualElement _clearVrmButton;

        public event Action<GameObject> ModelLoaded;
        public event Action<GameObject> ClearButtonClicked;

        private void Awake()
        {
            _loadVrmButton = _uiDocument.rootVisualElement.Q<VisualElement>("load-vrm-button");
            _clearVrmButton = _uiDocument.rootVisualElement.Q<VisualElement>("clear-vrm-button");
            _loadVrmButton.RegisterCallback<PointerDownEvent>(OpenFileBrowser);
            _clearVrmButton.RegisterCallback<PointerDownEvent>(OnClearButtonClicked);
        }

        private void OnDestroy()
        {
            _loadVrmButton.UnregisterCallback<PointerDownEvent>(OpenFileBrowser);
            _clearVrmButton.UnregisterCallback<PointerDownEvent>(OnClearButtonClicked);
        }

        private void OnClearButtonClicked(PointerDownEvent evt)
        {
            foreach (var model in _loadedModels)
            {
                if (model != null)
                {
                    ClearButtonClicked?.Invoke(model);
                    Destroy(model);
                }
            }
            _loadedModels.Clear();
        }

        private void OpenFileBrowser(PointerDownEvent evt)
        {
            var extension = ".vrm";

#if UNITY_WEBGL && !UNITY_EDITOR
            OpenFilePanel(gameObject.name, "LoadVrmModel", extension, false);
#else
            var paths = StandaloneFileBrowser.OpenFilePanel("Open VRM File", "", extension, false);
            if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                LoadVrmModel(paths[0]);
            }
#endif
        }

        public void LoadVrmModel(string path)
        {
            LoadVrmModelAsync(path).Forget();
        }

        public async UniTaskVoid LoadVrmModelAsync(string path)
        {
            byte[] bytes = null;

            if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
            {
                var webRequest = UnityWebRequest.Get(path);
                await webRequest.SendWebRequest();
                bytes = webRequest.downloadHandler.data;
            }
            else
            {
                bytes = await File.ReadAllBytesAsync(path);
            }

            if (bytes == null)
            {
                Debug.Log("<color=orange>Failed to load VRM file</color>");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            var instance = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: true, showMeshes: true,
                awaitCaller: new UniGLTF.RuntimeOnlyNoThreadAwaitCaller());
#else
            var instance = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: true, showMeshes: true);
#endif
            if (instance == null)
            {
                Debug.Log("<color=orange>Failed to parse VRM model</color>");
                return;
            }

            instance.transform.rotation = Quaternion.Euler(0, 180, 0);

            _loadedModels.Add(instance.gameObject);
            ModelLoaded?.Invoke(instance.gameObject);
        }
    }
}
