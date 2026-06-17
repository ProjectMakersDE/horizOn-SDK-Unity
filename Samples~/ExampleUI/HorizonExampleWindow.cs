#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PM.horizOn.Cloud.ExampleUI
{
    /// <summary>
    /// Editor window to display the horizOn SDK Example UI.
    /// Opens via Window > horizOn > SDK Example
    /// </summary>
    public class HorizonExampleWindow : EditorWindow
    {
        private HorizonExampleUIController _controller;

        [MenuItem("Window/horizOn/SDK Example")]
        public static void ShowWindow()
        {
            HorizonExampleWindow wnd = GetWindow<HorizonExampleWindow>();
            wnd.titleContent = new GUIContent("horizOn SDK Example");
            wnd.minSize = new Vector2(600, 700);
        }

        public void CreateGUI()
        {
            // Load UXML
            var visualTree = Resources.Load<VisualTreeAsset>("HorizonExampleUI");
            if (visualTree == null)
            {
                Debug.LogError("Failed to load HorizonExampleUI.uxml from Resources folder");
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            // Load USS by name so it resolves regardless of where the sample is
            // installed (Package Manager sample, .unitypackage, or manual copy).
            StyleSheet styleSheet = null;
            var ussGuids = AssetDatabase.FindAssets("HorizonExampleUI t:StyleSheet");
            if (ussGuids.Length > 0)
            {
                styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    AssetDatabase.GUIDToAssetPath(ussGuids[0]));
            }
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }
            else
            {
                Debug.LogWarning("Failed to load HorizonExampleUI.uss stylesheet");
            }

            // Initialize controller
            _controller = new HorizonExampleUIController();
            _controller.Initialize(rootVisualElement);

            // Subscribe to play mode state changes
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDestroy()
        {
            // Unsubscribe from play mode state changes
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Update UI when play mode state changes
            if (_controller != null)
            {
                _controller.OnPlayModeChanged();
            }
        }
    }
}
#endif
