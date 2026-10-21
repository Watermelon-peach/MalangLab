using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A3-1 씬을 만든다. 말랑이 하나와 벽 네 개뿐이다.
///
/// B1 씬과 달리 후보를 비교하지 않는다. 변형 방식은 이미 정해졌고,
/// 여기서 보는 건 "그 방식이 벽 충돌까지 감당하는가" 하나다.
/// </summary>
public static class A3Setup
{
    const string Folder = "Assets/Experiments/A_Overlay";
    const string ScenePath = Folder + "/A3_WallContact.unity";
    const string MatPath = Folder + "/MalangBlob.mat";

    [MenuItem("MalangLab/A3 벽 충돌 — 실험 씬 만들기", priority = 200)]
    public static void CreateScene()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.16f, 0.19f, 1f);
        camGo.transform.position = new Vector3(0f, 0f, -9f);
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

        var keyGo = new GameObject("Key Light");
        var key = keyGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.3f;
        keyGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

        var fillGo = new GameObject("Fill Light");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.45f;
        fill.color = new Color(0.8f, 0.85f, 1f);
        fillGo.transform.rotation = Quaternion.Euler(10f, 150f, 0f);

        // ── 벽 ──
        var wallsGo = new GameObject("Screen Walls");
        var walls = wallsGo.AddComponent<ScreenWalls>();
        var soWalls = new SerializedObject(walls);
        soWalls.FindProperty("cam").objectReferenceValue = cam;
        soWalls.ApplyModifiedProperties();

        // ── 말랑이 ──
        var mat = MakeMaterial(MatPath, "MalangLab/ShaderDeformBlob", new Color(1f, 0.55f, 0.62f));
        var blobGo = new GameObject("Malang");
        blobGo.AddComponent<MeshFilter>();
        blobGo.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var blob = blobGo.AddComponent<ShaderDeformBlob>();
        var bodyComp = blobGo.AddComponent<BlobBody>();

        var soBody = new SerializedObject(bodyComp);
        soBody.FindProperty("walls").objectReferenceValue = walls;
        soBody.ApplyModifiedProperties();

        // ── 입력 + HUD ──
        var rig = new GameObject("A3 Rig");
        var driver = rig.AddComponent<BlobInputDriver>();
        var hud = rig.AddComponent<A3Hud>();

        var soDriver = new SerializedObject(driver);
        soDriver.FindProperty("cam").objectReferenceValue = cam;
        // 몸이 더 쉽게 끌려와야 벽까지 밀어붙일 수 있다.
        soDriver.FindProperty("followThreshold").floatValue = 0.25f;
        soDriver.FindProperty("enablePoke").boolValue = false;   // B1 결론
        soDriver.ApplyModifiedProperties();

        var soHud = new SerializedObject(hud);
        soHud.FindProperty("body").objectReferenceValue = bodyComp;
        soHud.FindProperty("driver").objectReferenceValue = driver;
        soHud.FindProperty("blob").objectReferenceValue = blob;
        soHud.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[A3] 씬을 만들었습니다: {ScenePath}\n" +
                  "Play를 누르고 말랑이를 벽이나 작업표시줄 선으로 끌어보세요.");
    }

    static Material MakeMaterial(string path, string shaderName, Color color)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[A3] 셰이더를 찾지 못했습니다: {shaderName}");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.45f);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
