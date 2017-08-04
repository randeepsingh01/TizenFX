/*
 * Copyright (c) 2016 Samsung Electronics Co., Ltd All Rights Reserved
 *
 * Licensed under the Apache License, Version 2.0 (the License);
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an AS IS BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Native = Interop.ScreenMirroring;

namespace Tizen.Multimedia
{
    /// <summary>
    /// Provides the ability to connect to and disconnect from a screen mirroring source,
    /// start, pause, and resume the screen mirroring as a sink.
    /// </summary>
    public class ScreenMirroring : IDisposable, IDisplayable<ScreenMirroringErrorCode>
    {
        private const string LogTag = "Tizen.Multimedia.ScreenMirroring";

        private const int Port = 2022;

        private ScreenMirroringVideoInfo _videoInfo;
        private ScreenMirroringAudioInfo _audioInfo;
        private IntPtr _handle;

        private AtomicState _state;

        private bool _disposed = false;

        internal IntPtr Handle
        {
            get
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ScreenMirroring));
                }

                return _handle;
            }
        }

        /// <summary>
        /// Initializes a new instance of the ScreenMirroring class.
        /// </summary>
        /// <feature>http://tizen.org/feature/network.wifi.direct.display</feature>
        /// <exception cref="NotSupportedException">The feature is not supported.</exception>
        public ScreenMirroring()
        {
            Native.Create(out _handle).ThrowIfError("Failed to create ScreenMirroring.");

            _state = new AtomicState();

            _audioInfo = new ScreenMirroringAudioInfo(this);
            _videoInfo = new ScreenMirroringVideoInfo(this);

            RegisterStateChangedEvent();

            Log.Debug(LogTag, "screen mirroring sink created : " + _handle);
        }

        ~ScreenMirroring()
        {
            Dispose(false);
        }

        /// <summary>
        /// Occurs when the state is changed.
        /// </summary>
        public event EventHandler<ScreenMirroringStateChangedEventArgs> StateChanged;

        /// <summary>
        /// Occurs when an error occurs.
        /// </summary>
        public event EventHandler<ScreenMirroringErrorOccurredEventArgs> ErrorOccurred;

        #region Display support

        private Display _display;

        private void DetachDisplay()
        {
            if (_display != null)
            {
                _display.SetOwner(null);
                _display = null;
            }
        }

        private void SetDisplay(Display display)
        {
            if (display == null)
            {
                throw new ArgumentNullException(nameof(Display));
            }

            display.SetOwner(this);
            display.ApplyTo(this).ThrowIfError("Failed to set display.");

            _display = display;
        }

        ScreenMirroringErrorCode IDisplayable<ScreenMirroringErrorCode>.ApplyEvasDisplay(DisplayType type,
            ElmSharp.EvasObject evasObject)
        {
            Debug.Assert(Enum.IsDefined(typeof(DisplayType), type));

            return Native.SetDisplay(Handle, (int)type, evasObject);
        }
        #endregion

        /// <summary>
        /// Gets the negotiated audio info.
        /// </summary>
        /// <value>The <see cref="ScreenMirroringAudioInfo"/> if it has been connected, otherwise null.</value>
        public ScreenMirroringAudioInfo AudioInfo
        {
            get
            {
                if (IsConnected == false)
                {
                    return null;
                }

                return _audioInfo;
            }
        }

        /// <summary>
        /// Gets the negotiated video info.
        /// </summary>
        /// <value>The <see cref="ScreenMirroringVideoInfo"/> if it has been connected, otherwise null.</value>
        public ScreenMirroringVideoInfo VideoInfo
        {
            get
            {
                if (IsConnected == false)
                {
                    return null;
                }

                return _videoInfo;
            }
        }

        private bool IsConnected
        {
            get
            {
                return _state.IsOneOf(ScreenMirroringState.Connected, ScreenMirroringState.Playing,
                    ScreenMirroringState.Paused);
            }
        }

        /// <summary>
        /// Prepares the screen mirroring with the specified display.
        /// </summary>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Idle"/>.\n
        /// \n
        /// All supported resolutions will be candidates.
        /// </remarks>
        /// <param name="display">The display where the mirroring will be played on.</param>
        /// <exception cref="ArgumentException">
        ///    <paramref name="display"/> has already been assigned to another.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="display"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        public void Prepare(Display display)
        {
            PrepareCore(display, (ScreenMirroringResolutions)0);
        }

        /// <summary>
        /// Prepares the screen mirroring with the specified display and resolutions.
        /// </summary>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Idle"/>.
        /// </remarks>
        /// <param name="display">The display where the mirroring will be played on.</param>
        /// <param name="resolutions">The desired resolutions.</param>
        /// <exception cref="ArgumentException">
        ///    <paramref name="resolutions"/> contain invalid flags.\n
        ///    -or-\n
        ///    <paramref name="display"/> has already been assigned to another.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="display"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        public void Prepare(Display display, ScreenMirroringResolutions resolutions)
        {
            ValidationUtil.ValidateFlagsEnum(resolutions, (ScreenMirroringResolutions)((1 << 7) - 1), nameof(resolutions));

            PrepareCore(display, resolutions);
        }

        private void PrepareCore(Display display, ScreenMirroringResolutions resolutions)
        {
            ValidateState(ScreenMirroringState.Idle);

            Native.SetResolution(Handle, resolutions).ThrowIfError("Failed to set resolutions.");

            try
            {
                SetDisplay(display);

                Native.Prepare(Handle).ThrowIfError("Failed to prepare.");
            }
            catch
            {
                DetachDisplay();
                throw;
            }
        }

        /// <summary>
        /// Creates the connection and ready for receiving data from a mirroring source.
        /// </summary>
        /// <param name="sourceIp">The source ip address to connect.</param>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Prepared"/> state by
        /// <see cref="Prepare(Display, ScreenMirroringResolutions)"/>.
        /// </remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <privilege>http://tizen.org/privilege/internet</privilege>
        /// <exception cref="ArgumentNullException"><paramref name="sourceIp"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="sourceIp"/> is a zero-length string, contains only white space.</exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        /// <exception cref="UnauthorizedAccessException">Caller does not have required permission.</exception>
        public Task ConnectAsync(string sourceIp)
        {
            if (sourceIp == null)
            {
                throw new ArgumentNullException(nameof(sourceIp));
            }

            if (string.IsNullOrWhiteSpace(sourceIp))
            {
                throw new ArgumentException($"{nameof(sourceIp)} is a zero-length string.", nameof(sourceIp));
            }

            ValidateState(ScreenMirroringState.Prepared);

            Native.SetIpAndPort(Handle, sourceIp, Port.ToString()).ThrowIfError("Failed to set ip.");

            var tcs = new TaskCompletionSource<bool>();

            Task.Factory.StartNew(() =>
            {
                Native.Connect(Handle).ThrowIfError("Failed to connect");
                tcs.SetResult(true);
            });

            return tcs.Task;
        }

        /// <summary>
        /// Starts mirroring from the source.
        /// </summary>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Connected"/> state by
        /// <see cref="ConnectAsync(string)"/>.
        /// </remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <privilege>http://tizen.org/privilege/internet</privilege>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        /// <exception cref="UnauthorizedAccessException">Caller does not have required permission.</exception>
        public Task StartAsync()
        {
            ValidateState(ScreenMirroringState.Connected);

            var tcs = new TaskCompletionSource<bool>();

            Task.Factory.StartNew(() =>
            {
                Native.StartAsync(Handle).ThrowIfError("Failed to start.");
                tcs.TrySetResult(true);
            });

            return tcs.Task;
        }

        /// <summary>
        /// Pauses mirroring from the source.
        /// </summary>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Playing"/> state by
        /// <see cref="StartAsync"/>.
        /// </remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <privilege>http://tizen.org/privilege/internet</privilege>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        /// <exception cref="UnauthorizedAccessException">Caller does not have required permission.</exception>
        public Task PauseAsync()
        {
            ValidateState(ScreenMirroringState.Playing);

            var tcs = new TaskCompletionSource<bool>();

            Task.Factory.StartNew(() =>
            {
                Native.PauseAsync(Handle).ThrowIfError("Failed to prepare.");
                tcs.TrySetResult(true);
            });

            return tcs.Task;
        }

        /// <summary>
        /// Resumes mirroring from the source.
        /// </summary>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Paused"/> state by
        /// <see cref="PauseAsync"/>.
        /// </remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <privilege>http://tizen.org/privilege/internet</privilege>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        /// <exception cref="UnauthorizedAccessException">Caller does not have required permission.</exception>
        public Task ResumeAsync()
        {
            ValidateState(ScreenMirroringState.Paused);

            var tcs = new TaskCompletionSource<bool>();

            Task.Factory.StartNew(() =>
            {
                Native.ResumeAsync(Handle).ThrowIfError("Failed to resume.");
                tcs.TrySetResult(true);
            });

            return tcs.Task;
        }

        /// <summary>
        /// Disconnects from the source.
        /// </summary>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Connected"/>,
        /// <see cref="ScreenMirroringState.Playing"/> or <see cref="ScreenMirroringState.Paused"/>.
        /// </remarks>
        /// <privilege>http://tizen.org/privilege/internet</privilege>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        /// <exception cref="UnauthorizedAccessException">Caller does not have required permission.</exception>
        public void Disconnect()
        {
            ValidateState(ScreenMirroringState.Connected, ScreenMirroringState.Playing,
                ScreenMirroringState.Paused);

            Native.Disconnect(Handle).ThrowIfError("Failed to disconnect.");
        }

        /// <summary>
        /// Unprepares the screen mirroring.
        /// </summary>
        /// <remarks>
        /// The state must be <see cref="ScreenMirroringState.Prepared"/>,
        /// or <see cref="ScreenMirroringState.Disconnected"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     The current state is not in the valid.\n
        ///     -or-\n
        ///     An internal error occurs.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The <see cref="ScreenMirroring"/> has already been disposed.</exception>
        public void Unprepare()
        {
            ValidateState(ScreenMirroringState.Prepared, ScreenMirroringState.Disconnected);

            Native.Unprepare(Handle).ThrowIfError("Failed to reset.");

            DetachDisplay();
        }

        /// <summary>
        /// Releases all resource used by the <see cref="ScreenMirroring"/> object.
        /// </summary>
        /// <remarks>
        /// Call <see cref="Dispose()"/> when you are finished using the <see cref="ScreenMirroring"/>.
        /// The <see cref="Dispose()"/> method leaves the <see cref="ScreenMirroring"/> in an unusable
        /// state. After calling <see cref="Dispose"()/>, you must release all references to the
        /// <see cref="ScreenMirroring"/> so the garbage collector can reclaim the memory that the
        /// <see cref="ScreenMirroring"/> was occupying.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources used by the ScreenMirroring.
        /// </summary>
        /// <param name="disposing">
        /// true to release both managed and unmanaged resources; false to release only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                DetachDisplay();

                if (_handle != IntPtr.Zero)
                {
                    Native.Destroy(_handle);
                    _handle = IntPtr.Zero;
                }

                _disposed = true;
            }
        }

        private Native.StateChangedCallback _stateChangedCallback;

        private void RegisterStateChangedEvent()
        {
            _stateChangedCallback = (_, state, error) =>
            {
                var prevState = _state.Value;

                _state.Value = state;

                if (prevState != state)
                {
                    StateChanged?.Invoke(this, new ScreenMirroringStateChangedEventArgs(state));

                }

                if (error != ScreenMirroringErrorCode.None)
                {
                    ErrorOccurred?.Invoke(this, new ScreenMirroringErrorOccurredEventArgs(
                        ScreenMirroringError.InvalidOperation));
                }
            };

            Native.SetStateChangedCb(Handle, _stateChangedCallback).
                ThrowIfError("Failed to initialize StateChanged event.");
        }

        private void ValidateState(params ScreenMirroringState[] required)
        {
            Debug.Assert(required.Length > 0);

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ScreenMirroring));
            }

            var curState = _state.Value;
            if (!required.Contains(curState))
            {
                throw new InvalidOperationException($"The screen mirroring is not in a valid state. " +
                    $"Current State : { curState }, Valid State : { string.Join(", ", required) }.");
            }
        }

    }

    internal class AtomicState
    {
        private int _value;

        public AtomicState()
        {
            _value = (int)ScreenMirroringState.Idle;
        }

        public ScreenMirroringState Value
        {
            get
            {
                return (ScreenMirroringState)Interlocked.CompareExchange(ref _value, 0, 0);
            }
            set
            {
                Interlocked.Exchange(ref _value, (int)value);
            }
        }

        public bool IsOneOf(params ScreenMirroringState[] states)
        {
            return states.Contains(Value);
        }
    }
}
