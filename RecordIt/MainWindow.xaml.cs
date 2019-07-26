using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CSCore;
using CSCore.SoundIn;
using CSCore.Codecs.WAV;
using CSCore.SoundOut;
using CSCore.XAudio2;

namespace RecordIt
{
    public partial class MainWindow : Window
    {
        private bool m_isRecording = false;
        private WasapiCapture m_capture;
        private WaveWriter m_ww;
        private ISoundOut m_soundOut;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            m_isRecording = !m_isRecording;

            if (m_isRecording)
            {
                ((Button)sender).Content = "Stop";
                RecordSilence();
                RecordAudio();
            }
            else
            {
                StopRecording();
                StopSilence();
                ((Button)sender).Content = "Record";
            }
        }

        private void RecordAudio()
        {
            m_capture = new WasapiLoopbackCapture();

            //if nessesary, you can choose a device here
            //to do so, simply set the device property of the capture to any MMDevice
            //to choose a device, take a look at the sample here: http://cscore.codeplex.com/
            m_capture.Initialize();

            m_ww = new WaveWriter("dump.wav", m_capture.WaveFormat);

            m_capture.DataAvailable += (s, e) =>
                {
                    m_ww.Write(e.Data, e.Offset, e.ByteCount);
                };

            m_capture.Start();
        }

        private void StopRecording()
        {
            m_capture.Stop();
            m_capture.Dispose();

            m_ww.Dispose();
        }

        private void RecordSilence()
        {
            m_soundOut = new WasapiOut();
            m_soundOut.Initialize(new SilenceGenerator());
            m_soundOut.Play();
        }

        private void StopSilence()
        {
            m_soundOut.Stop();
            m_soundOut.Dispose();
        }
    }

    public class SilenceGenerator : IWaveSource
    {
        private readonly WaveFormat _waveFormat = new WaveFormat(44100, 16, 2);

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        public WaveFormat WaveFormat
        {
            get { return _waveFormat; }
        }

        public long Position
        {
            get { return -1; }
            set
            {
                throw new InvalidOperationException();
            }
        }

        public long Length
        {
            get { return -1; }
        }

        public bool CanSeek => throw new NotImplementedException();

        public void Dispose()
        {
            // do nothing
        }
    }

    public class SimpleMixer : ISampleSource
    {
        private readonly WaveFormat _waveFormat;
        private readonly List<ISampleSource> _sampleSources = new List<ISampleSource>();
        private readonly object _lockObj = new object();
        private float[] _mixerBuffer;

        public bool FillWithZeros { get; set; }

        public bool DivideResult { get; set; }

        public SimpleMixer(int channelCount, int sampleRate)
        {
            if (channelCount < 1)
                throw new ArgumentOutOfRangeException("channelCount");
            if (sampleRate < 1)
                throw new ArgumentOutOfRangeException("sampleRate");

            _waveFormat = new WaveFormat(sampleRate, 32, channelCount, AudioEncoding.IeeeFloat);
            FillWithZeros = false;
        }

        public void AddSource(ISampleSource source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (source.WaveFormat.Channels != WaveFormat.Channels ||
               source.WaveFormat.SampleRate != WaveFormat.SampleRate)
                throw new ArgumentException("Invalid format.", "source");

            lock (_lockObj)
            {
                if (!Contains(source))
                    _sampleSources.Add(source);
            }
        }

        public void RemoveSource(ISampleSource source)
        {
            //don't throw null ex here
            lock (_lockObj)
            {
                if (Contains(source))
                    _sampleSources.Remove(source);
            }
        }

        public bool Contains(ISampleSource source)
        {
            if (source == null)
                return false;
            return _sampleSources.Contains(source);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int numberOfStoredSamples = 0;

            if (count > 0 && _sampleSources.Count > 0)
            {
                lock (_lockObj)
                {
                    _mixerBuffer = _mixerBuffer.CheckBuffer(count);
                    List<int> numberOfReadSamples = new List<int>();
                    for (int m = _sampleSources.Count - 1; m >= 0; m--)
                    {
                        var sampleSource = _sampleSources[m];
                        int read = sampleSource.Read(_mixerBuffer, 0, count);
                        for (int i = offset, n = 0; n < read; i++, n++)
                        {
                            if (numberOfStoredSamples <= i)
                                buffer[i] = _mixerBuffer[n];
                            else
                                buffer[i] += _mixerBuffer[n];
                        }
                        if (read > numberOfStoredSamples)
                            numberOfStoredSamples = read;

                        if (read > 0)
                            numberOfReadSamples.Add(read);
                        else
                        {
                            //raise event here
                            RemoveSource(sampleSource); //remove the input to make sure that the event gets only raised once.
                        }
                    }

                    if (DivideResult)
                    {
                        numberOfReadSamples.Sort();
                        int currentOffset = offset;
                        int remainingSources = numberOfReadSamples.Count;

                        foreach (var readSamples in numberOfReadSamples)
                        {
                            if (remainingSources == 0)
                                break;

                            while (currentOffset < offset + readSamples)
                            {
                                buffer[currentOffset] /= remainingSources;
                                buffer[currentOffset] = Math.Max(-1, Math.Min(1, buffer[currentOffset]));
                                currentOffset++;
                            }
                            remainingSources--;
                        }
                    }
                }
            }

            if (FillWithZeros && numberOfStoredSamples != count)
            {
                Array.Clear(
                    buffer,
                    Math.Max(offset + numberOfStoredSamples - 1, 0),
                    count - numberOfStoredSamples);

                return count;
            }

            return numberOfStoredSamples;
        }

        public bool CanSeek { get { return false; } }

        public WaveFormat WaveFormat
        {
            get { return _waveFormat; }
        }

        public long Position
        {
            get { return 0; }
            set
            {
                throw new NotSupportedException();
            }
        }

        public long Length
        {
            get { return 0; }
        }

        public void Dispose()
        {
            lock (_lockObj)
            {
                foreach (var sampleSource in _sampleSources.ToArray())
                {
                    sampleSource.Dispose();
                    _sampleSources.Remove(sampleSource);
                }
            }
        }
    }
}
