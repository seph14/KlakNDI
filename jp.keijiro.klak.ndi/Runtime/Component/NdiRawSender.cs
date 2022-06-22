using UnityEngine;
using UnityEngine.Rendering;

namespace Klak.Ndi {
    [ExecuteInEditMode]
    public sealed partial class NdiRawSender : MonoBehaviour {
        #region Sender objects
        Interop.Send _send;
        ReadbackPool _pool;
        FormatConverter _converter;
        System.Action<AsyncGPUReadbackRequest> _onReadback;

        void PrepareSenderObjects() {
            // Private object initialization
            if (_send == null) _send = Interop.Send.Create(ndiName);
            if (_pool == null) _pool = new ReadbackPool();
            if (_converter == null) _converter = new FormatConverter(_resources);
            if (_onReadback == null) _onReadback = OnReadback;
        }

        void ReleaseSenderObjects() {
            // Total synchronization: This may cause a frame hiccup, but it's
            // needed to dispose the readback buffers safely.
            AsyncGPUReadback.WaitAllRequests();

            // Private objet disposal
            _send?.Dispose();
            _send = null;

            _pool?.Dispose();
            _pool = null;

            _converter?.Dispose();
            _converter = null;

            // We don't dispose _onReadback because it's reusable.
        }
        #endregion

        #region Capture coroutine for the Texture/GameView capture methods
        System.Collections.IEnumerator CaptureCoroutine() {
            for (var eof = new WaitForEndOfFrame(); true;) {
                // Wait for the end of the frame.
                yield return eof;

                PrepareSenderObjects();

                // Texture capture method
                if ( frameUpdated && sourceTexture != null) {
                    var (w, h) = (sourceTexture.width, sourceTexture.height);
                    if (RGBChannel) {
                        _pool.NewEntry(w, h, keepAlpha, RGBChannel, metadata)
                            .RequestReadback(sourceTexture, _onReadback);
                    } else {
                        // Pixel format conversion
                        var buffer = _converter.Encode(sourceTexture, keepAlpha, true);
                        // Readback entry allocation and request
                        _pool.NewEntry(w, h, keepAlpha, RGBChannel, metadata)
                             .RequestReadback(buffer, _onReadback);
                    }
                    frameUpdated = false;
                }
            }
        }
        #endregion

        #region GPU readback completion callback
        unsafe void OnReadback(AsyncGPUReadbackRequest req) {
            // Readback entry retrieval
            var data  = req.GetData<byte>();
            var entry = _pool.FindEntry(data);
            if (entry == null) return;
            
            // Invalid state detection
            if (req.hasError || _send == null || _send.IsInvalid || _send.IsClosed)  {
                // Do nothing but release the readback entry.
                _pool.Free(entry);
                return;
            }

            // Frame data
            var frame = new Interop.VideoFrame {
                Width       = entry.Width,
                Height      = entry.Height,
                FourCC      = entry.FourCC,
                FrameRateN  = 30000,
                FrameRateD  = 1001,
                AspectRatio = 0f,
                LineStride  = entry.Stride,
                FrameFormat = Interop.FrameFormat.Progressive,
                Timecode    = int.MaxValue,
                Data        = entry.ImagePointer,
                Metadata    = entry.MetadataPointer,
                Timestamp   = int.MaxValue
            };

            // Async-send initiation
            // This causes a synchronization for the last frame -- i.e., It locks
            // the thread if the last frame is still under processing.
            _send.SendVideoAsync(frame);
            // We don't need the last frame anymore. Free it.
            _pool.FreeMarkedEntry();
            // Mark this frame to get freed in the next frame.
            _pool.Mark(entry);
        }
        #endregion

        #region Component state controller

        // Component state reset without NDI object disposal
        internal void ResetState(bool willBeActive) {
            // Camera capture coroutine termination
            // We use this to kill only a single coroutine. It may sound like
            // overkill, but I think there is no side effect in doing so.
            StopAllCoroutines();

            // The following part of code is to activate the subcomponents. We can
            // break here if willBeActive is false.
            if (!willBeActive) return;
            // Capture coroutine initiation
            StartCoroutine(CaptureCoroutine());
        }

        // Component state reset with NDI object disposal
        internal void Restart(bool willBeActivate) {
            ResetState(willBeActivate);
            ReleaseSenderObjects();
        }

        internal void ResetState() => ResetState(isActiveAndEnabled);
        internal void Restart() => Restart(isActiveAndEnabled);
        #endregion

        #region MonoBehaviour implementation
        void OnEnable() => ResetState();
        void OnDisable() => Restart(false);
        void OnDestroy() => Restart(false);
        #endregion
    }
}
