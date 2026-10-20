using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A1 실험 세팅. 투명 창이 되려면 플레이어 설정 몇 개가 맞아야 하고,
/// 카메라는 알파 0으로 클리어하면서 후처리를 꺼야 한다. 손으로 맞추면
/// 하나 빠뜨렸을 때 "왜 투명이 안 되지"로 시간을 버리므로 메뉴로 묶었다.
/// </summary>
public static class A1Setup
{
    const string Folder = "Assets/Experiments/A_Overlay";
    const string ScenePath = Folder + "/A1_Overlay.unity";
    const string MatPath = Folder + "/MalangStandIn.mat";

    [MenuItem("MalangLab/A1 오버레이 — 전체 세팅 (설정 + 씬)", priority = 0)]
    public static void SetUpEverything()
    {
        string settings = ApplyPlayerSettingsInternal();
        CreateSceneInternal();
        EditorUtility.DisplayDialog("A1 세팅 완료",
            settings + "\n씬: " + ScenePath +
            "\n\n이제 빌드해서 확인하세요 (에디터에서는 투명이 안 보입니다).",
            "확인");
    }

    [MenuItem("MalangLab/A1 오버레이 — 플레이어 설정만 적용", priority = 20)]
    public static void ApplyPlayerSettings()
    {
        EditorUtility.DisplayDialog("플레이어 설정 적용", ApplyPlayerSettingsInternal(), "확인");
    }

    [MenuItem("MalangLab/A1 오버레이 — 실험 씬만 만들기", priority = 21)]
    public static void CreateScene()
    {
        CreateSceneInternal();
        Debug.Log($"[A1] 씬을 만들었습니다: {ScenePath}");
    }

    /// <summary>
    /// A1에서 확인된 필수 설정. D3D12는 플립 모델 스왑체인만 쓸 수 있고,
    /// 플립 모델은 레이어드 창 색 키와 DWM 알파 합성 둘 다와 호환되지 않는다.
    /// 이게 투명이 안 되던 가장 큰 원인이었다 — 선택이 아니라 필수다.
    /// </summary>
    [MenuItem("MalangLab/A1 오버레이 — 그래픽 API를 D3D11로 고정 (필수)", priority = 40)]
    public static void ForceD3D11()
    {
        var target = BuildTarget.StandaloneWindows64;
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D11 });
        AssetDatabase.SaveAssets();
        Debug.Log("[A1] Windows x64 그래픽 API를 D3D11 단독으로 고정했습니다.");
    }

    /// <summary>
    /// 활성 URP 에셋에서 알파가 최종 출력까지 살아남게 한다.
    /// PrefilterAlphaOutput이 특히 중요하다 — 켜두면 알파 셰이더 variant가
    /// 플레이어 빌드에서만 걷혀 나가서, 에디터에선 되고 빌드에선 깨진다.
    /// </summary>
    [MenuItem("MalangLab/A1 오버레이 — URP 알파 출력 켜기 (필수)", priority = 41)]
    public static void EnableUrpAlphaOutput()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) { Debug.LogError("[A1] 활성 렌더 파이프라인 에셋이 없습니다."); return; }

        var so = new SerializedObject(rp);
        var log = new StringBuilder($"[A1] {rp.name}:\n");

        void Flip(string prop, bool want)
        {
            var p = so.FindProperty(prop);
            if (p == null) { log.AppendLine($"  {prop} — 없음 (URP 버전 차이)"); return; }
            log.AppendLine($"  {prop}: {p.boolValue} → {want}");
            p.boolValue = want;
        }

        Flip("m_AllowPostProcessAlphaOutput", true);
        Flip("m_PrefilterAlphaOutput", false);
        Flip("m_SupportsHDR", false);   // HDR 경로는 알파 변수를 늘린다

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    // ──────────────────────────────────────────────────────────────────────
    static string ApplyPlayerSettingsInternal()
    {
        var log = new StringBuilder();

        // 포커스를 잃어도 계속 돌아야 한다. 이게 꺼져 있으면 다른 창을
        // 클릭하는 순간 오버레이가 멈춘다 — 상주형에선 치명적.
        SetBool(log, "Run In Background", PlayerSettings.runInBackground, true,
            v => PlayerSettings.runInBackground = v);

        SetBool(log, "Visible In Background", PlayerSettings.visibleInBackground, true,
            v => PlayerSettings.visibleInBackground = v);

        // DXGI 플립 모델 스왑체인은 투명 창과 충돌한다.
        SetBool(log, "Use DXGI Flip Model Swapchain", PlayerSettings.useFlipModelSwapchain, false,
            v => PlayerSettings.useFlipModelSwapchain = v);

        SetBool(log, "Resizable Window", PlayerSettings.resizableWindow, false,
            v => PlayerSettings.resizableWindow = v);

        SetBool(log, "Allow Fullscreen Switch", PlayerSettings.allowFullscreenSwitch, false,
            v => PlayerSettings.allowFullscreenSwitch = v);

        SetBool(log, "Force Single Instance", PlayerSettings.forceSingleInstance, true,
            v => PlayerSettings.forceSingleInstance = v);

        // 창 스타일을 직접 조작하므로 창 모드여야 한다.
        if (PlayerSettings.fullScreenMode != FullScreenMode.Windowed)
        {
            log.AppendLine($"  Fullscreen Mode: {PlayerSettings.fullScreenMode} → Windowed");
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        }

        // A1은 화면 전체를 덮는 창으로 시작한다. 작은 창 방식은 A2에서 비교한다.
        var res = Screen.currentResolution;
        if (PlayerSettings.defaultScreenWidth != res.width)
        {
            log.AppendLine($"  Default Width: {PlayerSettings.defaultScreenWidth} → {res.width}");
            PlayerSettings.defaultScreenWidth = res.width;
        }
        if (PlayerSettings.defaultScreenHeight != res.height)
        {
            log.AppendLine($"  Default Height: {PlayerSettings.defaultScreenHeight} → {res.height}");
            PlayerSettings.defaultScreenHeight = res.height;
        }

        AssetDatabase.SaveAssets();

        if (log.Length == 0) return "플레이어 설정은 이미 맞습니다.\n";
        return "바꾼 설정:\n" + log;
    }

    static void SetBool(StringBuilder log, string label, bool current, bool want,
                        System.Action<bool> apply)
    {
        if (current == want) return;
        log.AppendLine($"  {label}: {current} → {want}");
        apply(want);
    }

    // ──────────────────────────────────────────────────────────────────────
    static void CreateSceneInternal()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 카메라: 알파 0으로 클리어 + 후처리 off ──
        // URP는 후처리를 거치면서 알파를 1로 덮어버릴 수 있다. 계획서에 적어둔
        // "URP는 후처리 때문에 알파가 깨질 수 있다"가 바로 이 지점이다.
        var camGo = new GameObject("Overlay Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;   // HDR 경로에서 알파가 더 쉽게 깨진다
        cam.allowMSAA = false;
        camGo.transform.position = new Vector3(0f, 0f, -6f);

        var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = false;
        urp.antialiasing = AntialiasingMode.None;
        urp.renderShadows = false;

        // ── 조명 ──
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        // ── 말랑이 대역(공) ──
        // 전용 머티리얼을 만든다. CreatePrimitive가 물려주는 기본 머티리얼은
        // 공유 에셋이라 색을 바꾸면 프로젝트 전체가 물든다.
        var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blob.name = "Malang (stand-in)";
        blob.transform.position = Vector3.zero;
        blob.transform.localScale = Vector3.one * 2f;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(1f, 0.55f, 0.62f) };
            AssetDatabase.CreateAsset(mat, MatPath);
            blob.GetComponent<Renderer>().sharedMaterial = mat;
        }
        else
        {
            Debug.LogWarning("[A1] URP Lit 셰이더를 찾지 못했습니다. 머티리얼은 기본값으로 둡니다.");
        }

        // ── 창 제어 ──
        var ctrl = new GameObject("Overlay Window");
        ctrl.AddComponent<OverlayWindow>();

        EditorSceneManager.SaveScene(scene, ScenePath);

        // 빌드했을 때 이 씬이 먼저 뜨도록 맨 앞에 넣는다.
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != ScenePath) scenes.Add(s);
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.SaveAssets();
    }
}
