using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// B1 실험 씬을 만든다. 후보 둘을 나란히 놓고 같은 입력으로 만지게 한다.
///
/// 따로 띄워놓고 번갈아 보면 "아까 그게 더 나았나?"가 되어 비교가 안 된다.
/// 한 화면에 두고 번갈아 잡아보는 게 손맛 비교의 유일한 방법이다.
/// </summary>
public static class B1Setup
{
    const string Folder = "Assets/Experiments/B_Interaction";
    const string ScenePath = Folder + "/B1_Deformation.unity";
    const string MeshMatPath = Folder + "/MeshSpring.mat";
    const string ShaderMatPath = Folder + "/ShaderDeform.mat";

    [MenuItem("MalangLab/B1 변형 비교 — 실험 씬 만들기", priority = 100)]
    public static void CreateScene()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 카메라 ──
        // B는 에디터 Play 모드로 충분하므로 A1처럼 알파 0으로 클리어할 필요가 없다.
        // 오히려 배경이 보여야 변형 윤곽이 잘 읽힌다.
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.16f, 0.19f, 1f);
        camGo.transform.position = new Vector3(0f, 0f, -7f);
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = false;

        // ── 조명 ──
        // 변형을 눈으로 보려면 음영이 또렷해야 한다. 정면광만 있으면 납작해 보인다.
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

        // ── 후보 ① 메시 스프링 ──
        var meshMat = MakeMaterial(MeshMatPath, "Universal Render Pipeline/Lit",
                                   new Color(1f, 0.55f, 0.62f));
        var a = new GameObject("A - Mesh Spring");
        a.transform.position = new Vector3(-2.2f, 0f, 0f);
        a.AddComponent<MeshFilter>();
        a.AddComponent<MeshRenderer>().sharedMaterial = meshMat;
        var meshBlob = a.AddComponent<MeshSpringBlob>();

        // ── 후보 ② 셰이더 변형 ──
        var shaderMat = MakeMaterial(ShaderMatPath, "MalangLab/ShaderDeformBlob",
                                     new Color(0.6f, 0.8f, 1f));
        var b = new GameObject("B - Shader Deform");
        b.transform.position = new Vector3(2.2f, 0f, 0f);
        b.AddComponent<MeshFilter>();
        b.AddComponent<MeshRenderer>().sharedMaterial = shaderMat;
        var shaderBlob = b.AddComponent<ShaderDeformBlob>();

        // ── 입력 + HUD ──
        var rig = new GameObject("B1 Rig");
        var driver = rig.AddComponent<BlobInputDriver>();
        var hud = rig.AddComponent<B1Hud>();

        var soDriver = new SerializedObject(driver);
        soDriver.FindProperty("cam").objectReferenceValue = cam;
        soDriver.ApplyModifiedProperties();

        var soHud = new SerializedObject(hud);
        soHud.FindProperty("driver").objectReferenceValue = driver;
        var arr = soHud.FindProperty("blobs");
        arr.arraySize = 2;
        arr.GetArrayElementAtIndex(0).objectReferenceValue = meshBlob;
        arr.GetArrayElementAtIndex(1).objectReferenceValue = shaderBlob;
        soHud.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[B1] 씬을 만들었습니다: {ScenePath}\n" +
                  "Play를 눌러 왼쪽(메시 스프링)과 오른쪽(셰이더 변형)을 번갈아 만져보세요.");
    }

    static Material MakeMaterial(string path, string shaderName, Color color)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[B1] 셰이더를 찾지 못했습니다: {shaderName}");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;

        // 셰이더마다 색 프로퍼티 이름이 다르다.
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.45f);

        EditorUtility.SetDirty(mat);
        return mat;
    }
}
