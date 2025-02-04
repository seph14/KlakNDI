using UnityEngine;
using UnityEditor;

namespace Klak.Ndi.Editor {

[CanEditMultipleObjects]
[CustomEditor(typeof(NdiRawSender))]
sealed class NdiRawSenderEditor : UnityEditor.Editor {
    static class Labels {
        public static Label NdiName = "NDI Name";
    }

    #pragma warning disable CS0649

    AutoProperty _ndiName;
    AutoProperty _keepAlpha;
    AutoProperty _rgbaChannel;
    AutoProperty _frameUpdated;
    AutoProperty _fetchScreen;
    AutoProperty _sourceTexture;
    AutoProperty _sendOnThread;
    #pragma warning restore

    void OnEnable() => AutoProperty.Scan(this);

    public override void OnInspectorGUI() {
        serializedObject.Update();

        // NDI Name
        EditorGUILayout.DelayedTextField(_ndiName, Labels.NdiName);

        // Keep Alpha
        EditorGUILayout.PropertyField(_keepAlpha);
        // RGB Channel
        EditorGUILayout.PropertyField(_rgbaChannel);
        // Frame Updated
        EditorGUILayout.PropertyField(_frameUpdated);
        // Fetch Screen
        EditorGUILayout.PropertyField(_fetchScreen);
        // Send On Thread
        EditorGUILayout.PropertyField(_sendOnThread);

        EditorGUI.indentLevel++;

        // Source Texture
        EditorGUILayout.PropertyField(_sourceTexture);

        EditorGUI.indentLevel--;

        serializedObject.ApplyModifiedProperties();
    }
}

} // namespace Klak.Ndi.Editor
