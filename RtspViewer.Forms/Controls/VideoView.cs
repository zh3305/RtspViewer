﻿using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using NAudio.Wave;
using RtspViewer.Configuration;
using RtspViewer.RawFramesDecoding;
using RtspViewer.RawFramesDecoding.DecodedFrames;
using RtspViewer.RawFramesReceiving;

namespace RtspViewer.Forms.Controls
{
    public partial class VideoView : UserControl
    {
        private IRawFramesSource _rawFramesSource;
        private IAudioSource _audioSource;
        private IVideoSource _videoSource;

        private Dispatcher _dispatcher;
        private Bitmap _bitmap;
        private TransformParameters _transformParameters;
        private DateTime _startTime;
        private BufferedWaveProvider _player;
        private WaveOut _waveOut;

        public string ConnectionStatus { get; private set; } = "Disconnected";

        public long FramesReceived { get; private set; }

        public bool Started { get; private set; }

        public TimeSpan TimeElapsed
        {
            get 
            {
                if (Started)
                {
                    return DateTime.UtcNow - _startTime;
                }

                return TimeSpan.Zero;
            }
        }

        public VideoView()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            InitializeComponent();
        }

        ~VideoView() 
        {
            Stop();
        }

        public void InitialiseStream(StreamConfiguration config)
        {
            Stop();

            _rawFramesSource = new RawFramesSource(config);
            _audioSource = new RealtimeAudioSource(_rawFramesSource);
            _videoSource = new RealtimeVideoSource(_rawFramesSource);  
        }

        public void Start()
        {
            if (!Started && _rawFramesSource != null)
            {
                Started = true;
                _rawFramesSource.ConnectionStatusChanged += RawFramesSource_OnConnectionStatusChanged;
                _audioSource.FrameReceived += AudioSource_FrameReceived;
                _videoSource.FrameReceived += VideoSource_FrameReceived;

                _bitmap = new Bitmap(Width, Height);
                _transformParameters = _bitmap.GetTransformParameters();

                _rawFramesSource.Start();
                _startTime = DateTime.UtcNow;
            }
        }

        public void Stop()
        {
            if (Started && _rawFramesSource != null)
            {
                Started = false;
                _rawFramesSource.Stop();

                _rawFramesSource.ConnectionStatusChanged -= RawFramesSource_OnConnectionStatusChanged;
                _audioSource.FrameReceived -= AudioSource_FrameReceived;
                _videoSource.FrameReceived -= VideoSource_FrameReceived;

                ConnectionStatus = "Disconnected";
                FramesReceived = 0;
            }
        }

        private void ReinitializeBitmap(int width, int height)
        {
            if (Started)
            {
                // Allow the next frame to size correctly
                _bitmap = new Bitmap(width, height);
                _transformParameters = _bitmap.GetTransformParameters();
            }
            else if (_bitmap != null)
            {
                // Resize the current frame
                _dispatcher.Invoke(() =>
                {
                    Bitmap resized = new Bitmap(width, height);
                    using (Graphics g = Graphics.FromImage(resized))
                    {
                        g.DrawImage(_bitmap, 0, 0, width, height);
                    }

                    video.Image = resized;
                }, DispatcherPriority.Send);
            }
        }

        private void RawFramesSource_OnConnectionStatusChanged(object sender, string status)
        {
            ConnectionStatus = status;
        }

        private void AudioSource_FrameReceived(object sender, IDecodedAudioFrame frame)
        {
            if (_player == null)
            {
                _player = new BufferedWaveProvider(
                    new WaveFormat(
                        frame.Format.SampleRate,
                        frame.Format.BitPerSample,
                        frame.Format.Channels));
                _player.BufferLength = 2560 * 16;
                _player.DiscardOnBufferOverflow = true;

                _waveOut = new WaveOut();
                _waveOut.Init(_player);
                _waveOut.Volume = 1.0f;
            }

            _player.AddSamples(
                frame.DecodedBytes.Array,
                frame.DecodedBytes.Offset,
                frame.DecodedBytes.Count);

            if (_waveOut.PlaybackState != PlaybackState.Playing && Started)
            { 
                _waveOut.Play();
            }
        }

        private void VideoSource_FrameReceived(object sender, IDecodedVideoFrame frame)
        {
            FramesReceived++;

            _dispatcher.Invoke(() =>
            {
                _bitmap.UpdateBitmap(frame, _transformParameters);
                video.Image = _bitmap;
            }, DispatcherPriority.Send);
        }

        private void Video_Click(object sender, EventArgs e)
        {
            if (Started)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }

        private void Video_SizeChanged(object sender, EventArgs e)
        {
            _dispatcher.Invoke(() => ReinitializeBitmap(Width, Height), DispatcherPriority.Send);
        }
    }
}
