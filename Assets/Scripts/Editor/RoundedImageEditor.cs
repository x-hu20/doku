using UnityEditor;
using UnityEditor.UI;

/// <summary>
/// RoundedImage 的自定义 Inspector。
/// Unity 内置的 ImageEditor 只渲染 Image 自身字段，不会显示子类新增的 cornerRadius/cornerSegments，
/// 故继承 ImageEditor 并在末尾追加这两个字段，保留 Image 原有 UI（Source Image、Color 等）的同时露出圆角配置。
/// 放置在 Editor 文件夹下，仅编辑器编译。
/// </summary>
[CustomEditor(typeof(RoundedImage), true)]
public class RoundedImageEditor : ImageEditor
{
    private SerializedProperty _cornerRadius;
    private SerializedProperty _cornerSegments;

    protected override void OnEnable()
    {
        base.OnEnable();
        _cornerRadius = serializedObject.FindProperty("cornerRadius");
        _cornerSegments = serializedObject.FindProperty("cornerSegments");
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI(); // 先画 Image 的全部原有字段

        serializedObject.Update();
        EditorGUILayout.PropertyField(_cornerRadius);
        EditorGUILayout.PropertyField(_cornerSegments);
        serializedObject.ApplyModifiedProperties();
    }
}
