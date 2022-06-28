using UnityEngine;

namespace Klak.Ndi {

public sealed partial class NdiRawSender : MonoBehaviour {
    #region NDI source settings
    [SerializeField] string _ndiName = "NDI Raw Sender";
    string _ndiNameRuntime;

    public string ndiName
      { get => _ndiNameRuntime;
        set => SetNdiName(value); }

    void SetNdiName(string name) {
        if (_ndiNameRuntime == name) return;
        _ndiName = _ndiNameRuntime = name;
        Restart();
    }

    [SerializeField] bool _keepAlpha = false;
    [SerializeField] bool _frameUpdated = false;
    [SerializeField] bool _rgbaChannel = false;
    [SerializeField] bool _sendOnThread = false;
    bool _hasConnections = false;

    public bool keepAlpha { 
        get => _keepAlpha;
        set => _keepAlpha = value; 
    }
    public bool frameUpdated {
        get => _frameUpdated;
        set => _frameUpdated = value;
    }
    public bool RGBChannel {
        get => _rgbaChannel;
        set => _rgbaChannel = value;
    }
    
    public bool HasConnections {
        get => _hasConnections;
    }
    public bool SendOnThread {
        get => _sendOnThread;
        set => _sendOnThread = value;
    }
    #endregion

    #region Capture target settings
    [SerializeField] Texture _sourceTexture = null;

    public Texture sourceTexture
      { get => _sourceTexture;
        set => _sourceTexture = value; }
    #endregion

    #region Runtime property
    public string metadata { get; set; }
    public Interop.Send internalSendObject => _send;
    #endregion

    #region Resources asset reference
    [SerializeField, HideInInspector] NdiResources _resources = null;
    public void SetResources(NdiResources resources)
      => _resources = resources;
    #endregion

    #region Editor change validation
    // Applies changes on the serialized fields to the runtime properties.
    // We use OnValidate on Editor, which also works as an initializer.
    // Player never call it, so we use Awake instead of it.

    #if UNITY_EDITOR
    void OnValidate()
    #else
    void Awake()
    #endif
    {
        ndiName = _ndiName;
    }
    #endregion
}

} // namespace Klak.Ndi
