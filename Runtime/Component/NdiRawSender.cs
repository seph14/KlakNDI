using UnityEngine;
using UnityEngine.Rendering;
using System.Threading;
using System;

namespace Klak.Ndi {
    [ExecuteInEditMode]
    public sealed partial class NdiRawSender : MonoBehaviour {
        #region Sender objects
        Interop.Send        _send;
        ReadbackPool        _pool;
        FormatConverter     _converter;
        Interop.VideoFrame  _frame;
        ReadbackEntry       _entry;
        System.Action<AsyncGPUReadbackRequest> _onReadback;

        // offload send from other thread
        Thread              _thread;
        CancellationTokenSource _cancelTokenSource;
        CancellationToken   _token;
        bool                _readySend = false;

        void PrepareSenderObjects() {
            // Private object initialization
            if (_send == null) _send = Interop.Send.Create(ndiName);
            if (_pool == null) _pool = new ReadbackPool();
            if (_converter == null) _converter = new FormatConverter(_resources);
            if (_onReadback == null) _onReadback = OnReadback;

            if (_sendOnThread) {
                // thread initialization
                if (_cancelTokenSource == null) {
                    _cancelTokenSource = new CancellationTokenSource();
                    _token = _cancelTokenSource.Token;
                }

                if (_thread == null) {
                    _thread = new Thread(ndiProcess);
                    _thread.Start();
                }
            }
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

                // do not send if no connections are available
                if (_send.GetNumConnections() > 0) {
                    _hasConnections = true;
                    // Texture capture method
                    if (frameUpdated && sourceTexture != null) {
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
                    } else if(_fetchScreen) {
                        // Game View screen capture with a temporary RT
                        var (w, h) = (Screen.width, Screen.height);
                        var tempRT = RenderTexture.GetTemporary(w, h, 0);
                        ScreenCapture.CaptureScreenshotIntoRenderTexture(tempRT);
                        
                        if (RGBChannel) {
                            _pool.NewEntry(w, h, keepAlpha, RGBChannel, metadata)
                                .RequestReadback(tempRT, _onReadback);
                        } else {
                            // Pixel format conversion
                            var buffer = _converter.Encode(sourceTexture, keepAlpha, true);
                            // Readback entry allocation and request
                            _pool.NewEntry(w, h, keepAlpha, RGBChannel, metadata)
                                 .RequestReadback(buffer, _onReadback);
                        }
                        
                        RenderTexture.ReleaseTemporary(tempRT);
                    }
                } else _hasConnections = false;
            }
        }
        #endregion

        #region GPU readback completion callback
        unsafe void OnReadback(AsyncGPUReadbackRequest req) {
            // Readback entry retrieval
            var entry = _pool.FindEntry(req.GetData<byte>());
            if (entry == null) return;
            _entry = entry;

            // Invalid state detection
            if (_readySend || req.hasError || _send == null || 
                _send.IsInvalid || _send.IsClosed) {
                // Do nothing but release the readback entry.
                _pool.Free(_entry);
                return;
            }

            // Frame data
            _frame = new Interop.VideoFrame {
                Width       = _entry.Width,
                Height      = _entry.Height,
                FourCC      = _entry.FourCC,
                FrameRateN  = 30000,
                FrameRateD  = 1001,
                AspectRatio = 0f,
                LineStride  = _entry.Stride,
                FrameFormat = Interop.FrameFormat.Progressive,
                Timecode    = int.MaxValue,
                Data        = _entry.ImagePointer,
                Metadata    = _entry.MetadataPointer,
                Timestamp   = -1
            };

            if (_sendOnThread) {
                _readySend = true;
            } else {
                _send.SendVideoAsync(_frame);
                // We don't need the last frame anymore. Free it.
                _pool.FreeMarkedEntry();
                // Mark this frame to get freed in the next frame.
                _pool.Mark(_entry);
            }
        }
        #endregion

        #region Process thread
        unsafe void ndiProcess() {
            while (!_token.IsCancellationRequested) {
                if (_readySend) {
                    // Async-send initiation
                    // This causes a synchronization for the last frame -- i.e., It locks
                    // the thread if the last frame is still under processing.
                    _send.SendVideoAsync(_frame);
                    // We don't need the last frame anymore. Free it.
                    _pool.FreeMarkedEntry();
                    // Mark this frame to get freed in the next frame.
                    _pool.Mark(_entry);
                    _readySend = false;
                }
            }
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
        void OnDestroy() {
            Restart(false);
            if (_sendOnThread) {
                _cancelTokenSource?.Cancel();
                _cancelTokenSource?.Dispose();
                _cancelTokenSource = null;
                _thread?.Join();
                GC.SuppressFinalize(this);
            }
        }
        #endregion
    }
}
