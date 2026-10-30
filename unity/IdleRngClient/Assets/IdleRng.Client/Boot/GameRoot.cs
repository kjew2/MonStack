using System;
using IdleRng.Client.Core;
using IdleRng.Client.Screens;
using IdleRng.Sim.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace IdleRng.Client.Boot
{
    /// <summary>
    /// Composition root and the only MonoBehaviour (specs/07 section 4). Self-bootstraps after the first scene
    /// loads, so pressing Play in any scene (even an empty one) starts the game: no scene, prefab or UXML assets.
    /// Unity callbacks are forwarded to the plain-C# SimRunnerCore (10 Hz fixed step on unscaled time).
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("IdleRngGame");
            DontDestroyOnLoad(go);
            go.AddComponent<GameRoot>();
        }

        UIDocument _doc;
        ContentDb _content;
        ISaveBridge _save;
        OffsetClock _clock;
        SimRunnerCore _runner;
        Shell _shell;
        Label _fatal;
        bool _useBuiltinFont;
        float _fpsAcc;
        int _fpsFrames;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.runInBackground = true;

            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Ui.C(Palette.Background);
                cam.orthographic = true;
                DontDestroyOnLoad(cam.gameObject);
            }
            InputSetup.EnsureEventSystem();
            CreateDocument();
        }

        void CreateDocument()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1080, 1920);
            settings.screenMatchMode = PanelScreenMatchMode.Expand;   // the whole 1080x1920 reference always fits
            var theme = Resources.Load<ThemeStyleSheet>("IdleRngTheme");
            if (theme != null) settings.themeStyleSheet = theme;

            // Add the UIDocument on an inactive object so panelSettings is set before its OnEnable runs.
            var uiGo = new GameObject("UI");
            uiGo.SetActive(false);
            uiGo.transform.SetParent(transform, false);
            _doc = uiGo.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            uiGo.SetActive(true);

            _useBuiltinFont = theme == null;
        }

        void Start()
        {
            // The document root is created in UIDocument.OnEnable; stretch it over the whole panel explicitly.
            var root = _doc.rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            if (_useBuiltinFont)
            {
                // No theme found: give the tree a font explicitly so labels still render.
                try
                {
                    var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (font != null) root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
                }
                catch (Exception e) { Debug.LogWarning("IdleRng: no built-in font: " + e.Message); }
            }

            try
            {
                _content = ContentLocator.Load();
                Debug.Log("IdleRng: content " + _content.Version + " from " + ContentLocator.Describe);
            }
            catch (Exception e)
            {
                ShowFatal("Content failed to load from " + ContentLocator.Describe + ":\n\n" + e.Message);
                Debug.LogException(e);
                return;
            }
            _save = SaveBridgeFactory.Create(Application.persistentDataPath);
            _clock = new OffsetClock(new IdleRng.Sim.Time.SystemClock());
            StartSession();
        }

        void StartSession()
        {
            try
            {
                _runner = SimRunnerCore.Start(_content, _save, _clock);
                _shell = new Shell(_doc.rootVisualElement, _runner.Vm);
            }
            catch (Exception e)
            {
                _runner = null;
                ShowFatal("Failed to start the session:\n\n" + e);
                Debug.LogException(e);
            }
        }

        void ShowFatal(string message)
        {
            if (_fatal == null)
            {
                _fatal = Ui.Text("", Ui.Body, Palette.Bad);
                _fatal.style.position = Position.Absolute;
                _fatal.style.left = 20; _fatal.style.right = 20; _fatal.style.top = 20;
                _doc.rootVisualElement.Add(_fatal);
            }
            _fatal.text = message;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fpsAcc += dt; _fpsFrames++;
            if (_fpsAcc >= 0.5f) { DebugScreen.Fps = _fpsFrames / _fpsAcc; _fpsAcc = 0; _fpsFrames = 0; }

            if (_runner == null) return;
            _runner.Frame(dt);
            _shell.Refresh(Time.unscaledTime);

            if (_runner.Vm.ResetRequested)
            {
                _save.DeleteProfile();
                _shell.Dispose();
                _clock.OffsetSeconds = 0;
                StartSession();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (_runner == null) return;
            if (paused) _runner.OnPause();
            else _runner.OnResume();
        }

        void OnApplicationQuit() => _runner?.OnQuit();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
