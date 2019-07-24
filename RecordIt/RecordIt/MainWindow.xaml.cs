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
}
