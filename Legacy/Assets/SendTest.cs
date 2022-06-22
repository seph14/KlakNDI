using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Klak.Ndi;

public class SendTest : MonoBehaviour {
    protected NdiRawSender _sender;
    public Texture2D texture;

    void Start() {
        _sender = FindObjectOfType<NdiRawSender>();

#if false
        texture = new Texture2D(256, 256, TextureFormat.RGB24, 0, false);
#else
        texture = new Texture2D(256, 256, TextureFormat.RGBA32, 0, false);
#endif
        Color32[] col = new Color32[256 * 256];

        for (int i = 0; i < 256 * 256; i++) {
            col[i] = new Color32(
                255,//(byte)Random.Range(0, 255),
                32,//(byte)Random.Range(0, 255),
                128,//(byte)Random.Range(0, 255),
                255//(byte)Random.Range(0, 255)
            );
        }

        texture.SetPixels32(col);
        texture.Apply(false, true);

        _sender.sourceTexture = texture;
    }
}
