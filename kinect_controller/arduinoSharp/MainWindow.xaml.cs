using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Media;
using System.Threading;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Ports;
using System.Windows.Threading;
using Microsoft.Research.Kinect.Nui;
using Ozeki.Media;
using Ozeki.Media.MediaHandlers;
using Ozeki.Network.Nat;
using Ozeki.VoIP;
using Ozeki.VoIP.SDK;
using Ozeki.VoIP.Media;
using NAudio.Wave;
using NAudio.Utils;
using NAudio.CoreAudioApi;


namespace talkBot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        DispatcherTimer serialTimer, staticTimer, unansweredTimer;
        SerialPort port;
        Runtime nui;
        byte[] depthData = new byte[320 * 240 * 2];
        Point head = new Point();
        Point rightHand = new Point();
        Point leftHand = new Point();
        double headDepth = 0d;
        double rightHandDepth = 0d;
        double leftHandDepth = 0d;
        double jointDepth = 0d;
        const double priorityOffset = 0.1d;
        bool tracking = false;
        int trackedSkeletonID = 0;
        SoundPlayer player = new SoundPlayer();

        //ISoundEngine engine;

        // for debugging purposes
        Point trackedJoint = new Point();
        String jointName = "";

        // Bones color map        
        Dictionary<JointID, Brush> jointColors = new Dictionary<JointID, Brush>() { 
            {JointID.HipCenter, new SolidColorBrush(Color.FromRgb(169, 176, 155))},
            {JointID.Spine, new SolidColorBrush(Color.FromRgb(169, 176, 155))},
            {JointID.ShoulderCenter, new SolidColorBrush(Color.FromRgb(168, 230, 29))},
            {JointID.Head, new SolidColorBrush(Color.FromRgb(200, 0,   0))},
            {JointID.ShoulderLeft, new SolidColorBrush(Color.FromRgb(79,  84,  33))},
            {JointID.ElbowLeft, new SolidColorBrush(Color.FromRgb(84,  33,  42))},
            {JointID.WristLeft, new SolidColorBrush(Color.FromRgb(255, 126, 0))},
            {JointID.HandLeft, new SolidColorBrush(Color.FromRgb(215,  86, 0))},
            {JointID.ShoulderRight, new SolidColorBrush(Color.FromRgb(33,  79,  84))},
            {JointID.ElbowRight, new SolidColorBrush(Color.FromRgb(33,  33,  84))},
            {JointID.WristRight, new SolidColorBrush(Color.FromRgb(77,  109, 243))},
            {JointID.HandRight, new SolidColorBrush(Color.FromRgb(37,   69, 243))},
            {JointID.HipLeft, new SolidColorBrush(Color.FromRgb(77,  109, 243))},
            {JointID.KneeLeft, new SolidColorBrush(Color.FromRgb(69,  33,  84))},
            {JointID.AnkleLeft, new SolidColorBrush(Color.FromRgb(229, 170, 122))},
            {JointID.FootLeft, new SolidColorBrush(Color.FromRgb(255, 126, 0))},
            {JointID.HipRight, new SolidColorBrush(Color.FromRgb(181, 165, 213))},
            {JointID.KneeRight, new SolidColorBrush(Color.FromRgb(71, 222,  76))},
            {JointID.AnkleRight, new SolidColorBrush(Color.FromRgb(245, 228, 156))},
            {JointID.FootRight, new SolidColorBrush(Color.FromRgb(77,  109, 243))}
        };

        private void Window_Loaded(object sender, EventArgs e)
        {
            nui = new Runtime();
            serialTimer = new DispatcherTimer();
            staticTimer = new DispatcherTimer();
            unansweredTimer = new DispatcherTimer();
            serialTimer.Tick += serialTimerEvent;
            staticTimer.Tick += staticTimerEvent;
            unansweredTimer.Tick += unansweredTimerEvent;
            GC.KeepAlive(serialTimer);

            // Initialize Soft Phone
            InitializeSoftPhone();

            // Initialize audio engine
            //engine = new ISoundEngine();
            // Interface with Arduino

            string[] portList = SerialPort.GetPortNames();
            if (portList.Length > 0)
            {
                Debug.Text = "Port used: " + portList[0].ToString();
                port = new SerialPort(portList[0], 9600, Parity.None, 8, StopBits.One);
                port.Open();
                if (port.IsOpen)
                {
                    port.Write("a");
                }
            }

            // Create a background worker to check soundcard
            BackgroundWorker checkSoundCard = new BackgroundWorker();
            checkSoundCard.DoWork += delegate(object s, DoWorkEventArgs args)
            {
                MMDeviceEnumerator devEnum = new MMDeviceEnumerator();
                MMDevice defaultDevice = devEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                while (true)
                {
                    if (defaultDevice.AudioMeterInformation.MasterPeakValue*100 > 1)
                    {
                        if (port.IsOpen)
                        {
                            port.Write("t");
                        }
                    }
                }
            };
            checkSoundCard.RunWorkerAsync();

            // Initialize Kinect
            try
            {
                nui.Initialize(RuntimeOptions.UseSkeletalTracking | RuntimeOptions.UseDepthAndPlayerIndex);
            }
            catch (InvalidOperationException)
            {
                System.Windows.MessageBox.Show("Runtime initialization failed. Please make sure Kinect device is plugged in.");
                return;
            }

            try
            {
                nui.DepthStream.Open(ImageStreamType.Depth, 2, ImageResolution.Resolution320x240, ImageType.DepthAndPlayerIndex);
            }
            catch (InvalidOperationException)
            {
                System.Windows.MessageBox.Show("Failed to open stream. Please make sure to specify a supported image type and resolution.");
                return;
            }

            nui.SkeletonFrameReady += new EventHandler<SkeletonFrameReadyEventArgs>(nui_SkeletonFrameReady);
            nui.DepthFrameReady += new EventHandler<ImageFrameReadyEventArgs>(nui_DepthFrameReady);
        }
        #region Timer Events
        void serialTimerEvent(object sender, EventArgs e)
        {
            // send data through serial every half a second
            // calculate joint coordinates and send data through serial port               
            sendTrackedJoint();
        }

        void staticTimerEvent(object sender, EventArgs e)
        {
            //Make phone call
            MakePhoneCall();
        }

        void unansweredTimerEvent(object sender, EventArgs e)
        {
            // Executes if call not answered, only once
            unansweredTimer.Stop();
            if (phoneLineInformation == PhoneLineState.RegistrationSucceded)
            {
                //Hangup call if line busy
                call.HangUp();
                //Remake phone call
                MakePhoneCall();
            }
            else
            {
                //Play prerecorded sound
                PlayAudio();                
            }
        }


        #endregion
        #region Skeleton Tracking
        void nui_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            if (!serialTimer.IsEnabled)
            {// set a timer to update the serial port every 1/10 second
                serialTimer.Interval = TimeSpan.FromMilliseconds(100);
                serialTimer.Start();
                serialTimerDebug.Text = "Serial Timer ENABLED";
            }

            SkeletonFrame skeletonFrame = e.SkeletonFrame;
            int iSkeleton = 0;
            Brush[] brushes = new Brush[6];
            brushes[0] = new SolidColorBrush(Color.FromRgb(255, 0, 0));
            brushes[1] = new SolidColorBrush(Color.FromRgb(0, 255, 0));
            brushes[2] = new SolidColorBrush(Color.FromRgb(64, 255, 255));
            brushes[3] = new SolidColorBrush(Color.FromRgb(255, 255, 64));
            brushes[4] = new SolidColorBrush(Color.FromRgb(255, 64, 255));
            brushes[5] = new SolidColorBrush(Color.FromRgb(128, 128, 255));
            skeleton.Children.Clear();
            int trackToken = 0;
            foreach (SkeletonData data in skeletonFrame.Skeletons)
            {
                if (SkeletonTrackingState.Tracked == data.TrackingState)
                {
                    if (trackedSkeletonID == data.TrackingID)
                    //update token only if same skeleton tracked more than once
                    {
                        trackToken++;
                    }
                    if (!tracking)
                    // new viewer arrives
                    {
                        trackedSkeletonID = data.TrackingID;
                        tracking = true;
                        //start the timer when a new user arrives
                        // after 10 seconds the head will start talking
                        staticTimer.Interval = TimeSpan.FromSeconds(10);
                        staticTimer.Start();
                        staticTimerDebug.Text = "static Timer ENABLED";
                    }

                    // Draw bones
                    Brush brush = brushes[iSkeleton % brushes.Length];
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.HipCenter, JointID.Spine, JointID.ShoulderCenter, JointID.Head));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.ShoulderCenter, JointID.ShoulderLeft, JointID.ElbowLeft, JointID.WristLeft, JointID.HandLeft));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.ShoulderCenter, JointID.ShoulderRight, JointID.ElbowRight, JointID.WristRight, JointID.HandRight));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.HipCenter, JointID.HipLeft, JointID.KneeLeft, JointID.AnkleLeft, JointID.FootLeft));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.HipCenter, JointID.HipRight, JointID.KneeRight, JointID.AnkleRight, JointID.FootRight));

                    // Draw joints
                    foreach (Joint joint in data.Joints)
                    {
                        Point jointPos = getDisplayPosition(joint);
                        Line jointLine = new Line();
                        jointLine.X1 = jointPos.X - 3;
                        jointLine.X2 = jointLine.X1 + 6;
                        jointLine.Y1 = jointLine.Y2 = jointPos.Y;
                        jointLine.Stroke = jointColors[joint.ID];
                        jointLine.StrokeThickness = 6;
                        skeleton.Children.Add(jointLine);


                        // Follow only first skeleton tracked
                        if (data.TrackingID == trackedSkeletonID)
                        {
                            //Debug.Text = String.Format("Skeleton ID: {0}\nTracked Skeleton ID: {1}",
                            //                            data.TrackingID, trackedSkeletonID);
                            switch (joint.ID)
                            {
                                case JointID.Head:
                                    head = jointPos;
                                    // Calculate and Print the right hand depth
                                    headDepth = joint.Position.Z;
                                    break;
                                case JointID.HandRight:
                                    rightHand = jointPos;
                                    // Calculate and Print the right hand depth
                                    rightHandDepth = joint.Position.Z;
                                    break;
                                case JointID.HandLeft:
                                    leftHand = jointPos;
                                    // Calculate and Print the right hand depth
                                    leftHandDepth = joint.Position.Z;
                                    break;
                            }
                        }
                    }
                }
                iSkeleton++;
            }
            // if after browsing all skeletons the previously tracked skeleton is not there renews tracked skeleton
            if (trackToken == 0)
            {
                tracking = false;
                staticTimer.Stop();
                staticTimerDebug.Text = "static Timer DISABLED";
            }
        }

        Polyline getBodySegment(Microsoft.Research.Kinect.Nui.JointsCollection joints, Brush brush, params JointID[] ids)
        {
            System.Windows.Media.PointCollection points = new System.Windows.Media.PointCollection(ids.Length);
            for (int i = 0; i < ids.Length; ++i)
            {
                points.Add(getDisplayPosition(joints[ids[i]]));
            }

            Polyline polyline = new Polyline();
            polyline.Points = points;
            polyline.Stroke = brush;
            polyline.StrokeThickness = 5;
            return polyline;
        }
        #endregion
        void sendTrackedJoint()
        {

            if ((rightHandDepth <= headDepth - priorityOffset) && (rightHandDepth <= leftHandDepth))
            {
                trackedJoint = rightHand;
                jointName = "rightHand";
                jointDepth = rightHandDepth;

            }
            else if ((leftHandDepth <= headDepth - priorityOffset) && (leftHandDepth < rightHandDepth))
            {
                trackedJoint = leftHand;
                jointName = "leftHand";
                jointDepth = leftHandDepth;
            }
            else
            {
                trackedJoint = head;
                jointName = "Head";
                jointDepth = headDepth;
            }
            String sposX = ((int)trackedJoint.X / 3).ToString();
            // invert Y-coordinates
            String sposY = ((int)(depth.Height - trackedJoint.Y) / 2).ToString();
            String sposDepth = ((int)(jointDepth * 1000)).ToString();
            Debug.Text = String.Format("Track Skeleton ID: {4}\nJoint selected: {0}\nDistance: {1}\nX-Coord: {2}\nY-Coord: {3}",
                                        jointName, sposDepth, sposX, sposY, trackedSkeletonID);
            // >>>
            // Send tracked joint's coordinates through serial port
            if (port.IsOpen)
            {
                for (int i = 0; i < sposX.Length; i++)
                //send all the numbers in the X-coordinate one by one
                {
                    port.Write(sposX[i].ToString());
                }
                port.WriteLine("x");
                for (int i = 0; i < sposY.Length; i++)
                //send all the numbers in the Y-coordinate one by one
                {
                    port.Write(sposY[i].ToString());
                }
                port.WriteLine("y");
                for (int i = 0; i < sposDepth.Length; i++)
                //send all the numbers in the joint depth one by one
                {
                    port.Write(sposDepth[i].ToString());
                }
                port.WriteLine("d");
            }
        }


        #region Depth Frame
        void nui_DepthFrameReady(object sender, ImageFrameReadyEventArgs e)
        {
            PlanarImage Image = e.ImageFrame.Image;
            depthData = Image.Bits;
            byte[] ColoredBytes = GenerateColoredBytes(e.ImageFrame);

            depth.Source = BitmapSource.Create(Image.Width, Image.Height, 96, 96, PixelFormats.Bgr32, null, ColoredBytes, Image.Width * 4);
        }


        private int GetDistanceWithPlayerIndex(byte firstFrame, byte secondFrame)
        {
            int distance = (int)(firstFrame >> 3 | secondFrame << 5);
            return distance;
        }

        private int GetPlayerIndex(byte firstFrame)
        {
            // Applies 111 mask to get the first two bites which define the player
            return (int)firstFrame & 7;
        }

        int indexOfPixelinBytes(int x, int y, int width, int bpp)
        {
            return (x + y * width) * bpp;
        }

        private byte[] GenerateColoredBytes(ImageFrame imageFrame)
        {
            int height = imageFrame.Image.Height;
            int width = imageFrame.Image.Width;

            Byte[] colorFrame = new byte[imageFrame.Image.Height * imageFrame.Image.Width * 4];

            const int BlueIndex = 0;
            const int GreenIndex = 1;
            const int RedIndex = 2;

            var depthIndex = 0;
            for (var y = 0; y < height; y++)
            {
                var heightOffset = y * width;

                for (var x = 0; x < width; x++)
                {
                    var index = (x + heightOffset) * 4;
                    var distance = GetDistanceWithPlayerIndex(depthData[depthIndex], depthData[depthIndex + 1]);

                    if (distance <= 900)
                    {
                        colorFrame[index + BlueIndex] = 255;
                        colorFrame[index + GreenIndex] = 0;
                        colorFrame[index + RedIndex] = 0;
                    }
                    else if (distance > 900 && distance <= 2000)
                    {
                        colorFrame[index + BlueIndex] = 0;
                        colorFrame[index + GreenIndex] = 255;
                        colorFrame[index + RedIndex] = 0;
                    }
                    else if (distance > 2000)
                    {
                        colorFrame[index + BlueIndex] = 0;
                        colorFrame[index + GreenIndex] = 0;
                        colorFrame[index + RedIndex] = 255;
                    }

                    // Color a player
                    if (GetPlayerIndex(depthData[depthIndex]) > 0)
                    {
                        colorFrame[index + BlueIndex] = 0;
                        colorFrame[index + GreenIndex] = 255;
                        colorFrame[index + RedIndex] = 255;
                    }

                    //jump two bytes to get the next pixel
                    depthIndex += 2;
                }
            }
            return colorFrame;
        }



        private Point getDisplayPosition(Joint joint)
        {
            float depthX, depthY;

            nui.SkeletonEngine.SkeletonToDepthImage(joint.Position, out depthX, out depthY);
            depthX = Math.Max(0, Math.Min(depthX * 320, 320));  //convert to 320, 240 space
            depthY = Math.Max(0, Math.Min(depthY * 240, 240));  //convert to 320, 240 space
            int colorX, colorY;
            ImageViewArea iv = new ImageViewArea();
            // only ImageResolution.Resolution640x480 is supported at this point
            nui.NuiCamera.GetColorPixelCoordinatesFromDepthPixel(ImageResolution.Resolution640x480, iv, (int)depthX, (int)depthY, (short)0, out colorX, out colorY);

            // map back to skeleton.Width & skeleton.Height
            return new Point((int)(skeleton.Width * colorX / 640.0), (int)(skeleton.Height * colorY / 480));
        }
        #endregion
        #region Soft Phone
        ISoftPhone softPhone;
        PhoneLineState phoneLineInformation;
        IPhoneCall call;
        IPhoneLine phoneLine;
        Microphone microphone = new Microphone();
        Speaker speaker = new Speaker();
        MediaConnector connector = new MediaConnector();
        PhoneCallMediaSender mediaSender = new PhoneCallMediaSender();
        PhoneCallMediaReceiver mediaReceiver = new PhoneCallMediaReceiver();

        private void InitializeSoftPhone()
        {
            /* Create a real or a test softphone...
             * */
            softPhone = SoftPhoneFactory.CreateSoftPhone(GetLocalIP().ToString(), 5700, 5750, 5700);
            //softPhone = new VoIP.SDK.Mock.ArbSoftPhone(1000, 2000, 20, 50);

            /* Set the SIP Account settings..
             * */
            SIPAccount acc = new SIPAccount(
                registrationRequired: true,
                displayName: "david.montero",
                userName: "david.montero",
                registerName: "david.montero",
                registerPassword: "c1ent0un0",
                domainServerHost: "77.72.169.134",
                domainServerPort: 5060
            );
            /* Create a phone line using the softphone.
             * */
            softPhoneDebug.Text = "Creating Phone Line";
            phoneLine = softPhone.CreatePhoneLine(acc);
            softPhoneDebug.Text = "Phone Line created";
            /* Create event handler for the phone line.
             * */
            softPhoneDebug.Text = "Creating PhoneLine Event";
            phoneLine.PhoneLineStateChanged +=
                new EventHandler<VoIPEventArgs<PhoneLineState>>(phoneLine_PhoneLineInformation);
            softPhoneDebug.Text = "Event created";
            /* Register the phone line on softphone.
             * */
            softPhoneDebug.Text = "Registering PhoneLine";
            softPhone.RegisterPhoneLine(phoneLine);
            
        }

        private static IPAddress GetLocalIP()
        {
            foreach (var n in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (n.AddressFamily == AddressFamily.InterNetwork && n != IPAddress.Loopback)
                    return n;
            }
            return IPAddress.Any;
        }

        private void phoneLine_PhoneLineInformation(object sender, VoIPEventArgs<PhoneLineState> e)
        {
            phoneLine = sender as IPhoneLine;
            phoneLineInformation = e.Item;
            if (sender == null)
                return;
            if (e.Item != PhoneLineState.RegistrationSucceded)
                return;
            else
                softPhoneDebug.Text = "Phone line registered";
        }

        private void MakePhoneCall()
        {
            if (call != null)
            {
                MessageBox.Show("Line Busy!!");
                return;
            }
            string[] lines = File.ReadAllLines("../../Resources/YellowPages.txt");
            Random rand = new Random();
            String number = lines[rand.Next(lines.Length)];
            if (phoneLineInformation != PhoneLineState.RegistrationSucceded && phoneLineInformation != PhoneLineState.NoRegNeeded)
            {
                unansweredTimerDebug.Text = "Phone line state is not valid!";
                unansweredTimer.Interval = TimeSpan.FromSeconds(1);
                unansweredTimer.Start();
                return;
            }
            softPhoneDebug.Text = String.Format("Dialing number: {0}", number);
            call = softPhone.CreateCallObject(phoneLine, number);
            // >>>
            call.Start();
            //start the timer when making a phone call
            // after 5 seconds the head will hangup and say a sentence
            unansweredTimer.Interval = TimeSpan.FromSeconds(5);
            unansweredTimer.Start();
            unansweredTimerDebug.Text = "unanswered call Timer ENABLED";
        }

        #endregion
        #region Audio
        void PlayAudio()
        {
            Random rand = new Random();
            String phrase = "";
            int number = rand.Next(0, 3);
            switch (number)
            {
                case 0:
                    phrase = "../../Resources/alone.wav";
                    break;
                case 1:
                    phrase = "../../Resources/looking.wav";
                    break;
                case 2:
                    phrase = "../../Resources/outta.wav";
                    break;
            }
            WaveStream outStream = new WaveFileReader(phrase);
            IWavePlayer player = new WaveOut();

            player.Init(outStream);
            player.Play();
        }

        #endregion

        private void Window_Closed(object sender, EventArgs e)
        {
            if (port.IsOpen)
            {
                port.Write("k");
                Thread.Sleep(100);
                port.Close();
            }
            nui.Uninitialize();
            Environment.Exit(0);
        }
    }
}
