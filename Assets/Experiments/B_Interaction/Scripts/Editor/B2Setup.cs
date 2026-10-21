using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// B2 씬을 만든다. 말랑이 하나뿐이다.
///
/// B1처럼 후보를 나란히 놓지 않는다. 비교 대상이 "방식"이 아니라 "수치"라
/// 같은 몸에서 갈아끼우는 쪽이 맞다 — 몸이 둘이면 조명도 각도도 달라져서
/// 그 차이가 손맛 차이로 읽힌다.
/// </summary>
public static class B2Setup
{
    const string Folder = "Assets/Experiments/B_Interaction";
    const string ScenePath = Folder + "/B2_StretchFeel.unity";
    const string MatPath = Folder + "/ShaderDeform.mat";

    [MenuItem("MalangLab/B2 늘리기 손맛 — 실험 씬 만들기", priority = 110)]
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
        // B1보다 가깝다. 손맛을 보려면 말랑이가 화면에서 커야 한다.
        camGo.transform.position = new Vector3(0f, 0f, -5.5f);
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

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("MalangLab/ShaderDeformBlob"));
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.62f));
        mat.SetFloat("_Smoothness", 0.45f);
        EditorUtility.SetDirty(mat);

        var blobGo = new GameObject("Malang");
        blobGo.AddComponent<MeshFilter>();
        blobGo.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var blob = blobGo.AddComponent<ShaderDeformBlob>();

        var rig = new GameObject("B2 Rig");
        var driver = rig.AddComponent<BlobInputDriver>();
        var b2 = rig.AddComponent<B2Rig>();

        var soDriver = new SerializedObject(driver);
        soDriver.FindProperty("cam").objectReferenceValue = cam;
        soDriver.FindProperty("enablePoke").boolValue = false;   // B1 결론
        soDriver.ApplyModifiedProperties();

        var soB2 = new SerializedObject(b2);
        soB2.FindProperty("blob").objectReferenceValue = blob;
        soB2.FindProperty("driver").objectReferenceValue = driver;
        soB2.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[B2] 씬을 만들었습니다: {ScenePath}\n" +
                  "Play 후 1~5로 프리셋을 바꾸며 만져보세요. 잡은 채로 바꿔도 됩니다.");
    }
}
