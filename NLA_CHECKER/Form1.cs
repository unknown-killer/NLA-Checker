using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Register_CheckerTest
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }
        public static List<string> _combo = new List<string>();
        public static List<string> _combo_0 = new List<string>();
        private string _trackingHandler;
        public static Random Rand = new Random();

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly object _lockObject = new object();


        private readonly object _lockGet = new object();
        public void Timer()
        {
            _stopwatch.Start();

            re:
            Invoke((MethodInvoker)delegate
            {
                timertxt.Text = _stopwatch.Elapsed.ToString("hh\\:mm\\:ss");
            });
            Thread.Sleep(1000);
            goto re;
        }
        public bool _work = false;
        bool _abort = false;
       
 

        private int _non_nla;
        private int _badpr;
        private int _check;
        private int _error;
        private int _errors_full;
        private int _nla;
       
        public void dooo()
        {
         
            
            var oThread2 = new Thread(Timer) { IsBackground = true };
            oThread2.Start();
            var str = DateTime.Now.ToString("dd-MM-yy [hh_mm]");
            _trackingHandler = Application.StartupPath + "\\" + str;
            if (!Directory.Exists(_trackingHandler)) Directory.CreateDirectory(_trackingHandler);
            var add_thred = int.Parse(numericUpDown1.Value.ToString());

            if (add_thred >= _combo.Count)
            {
                add_thred = _combo.Count;
            }
            int num = 0;
            while (num < add_thred)
            {

                Thread th = new Thread(new ThreadStart(Worcher_Set));
                th.IsBackground = true;
                th.Start();
                num++;
                Thread.Sleep(5);
            }
        }

        private static int milliseconds = 0;

        private static int int_tries = 0;

    

        public void check_length()
        {
            while (true)
            {
                Thread.Sleep(3000);

                if (_combo.Count == 0)
                {
                    _work = false;
                }
            }

           
        }
        private void Worcher_Set()
        {
            
            while (_work)
            {
                try
                {

                    string text;
                    lock (_lockGet)
                    {

                        text = string.Empty;
                        text = _combo[0];
                        _combo.RemoveAt(0);


                    }

              
                    if (string.IsNullOrEmpty(text))
                    {
                        Thread.Sleep(900);
                    }
                    else
                    {
                        try
                        {
                            CheckAcc(text);
                        }
                        catch (Exception) { }
                    }
                }
                catch
                {
                   
                }
            }
              
        }
        public static RdpConnectionType CheckIfPortIsRdp(string host, int port, int timeout)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(host, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(timeout);
                    if (!success)
                    {
                        return RdpConnectionType.NotRdp;
                    }

                    client.EndConnect(result);

                    using (var stream = client.GetStream())
                    {
                        // RDP Negotiation Request Packet
                        // This packet advertises support for TLS (0x01) and NLA (0x02)
                        byte[] rdpNegotiationRequest = {
                        0x03, 0x00, 0x00, 0x13, // TPKT Header
                        0x0E, // Length of remaining TPDU
                        0xE0, // Connect-Request TPDU
                        0x00, 0x00, // DST-REF
                        0x00, 0x00, // SRC-REF
                        0x00,       // Class
                        0x01, 0x00, 0x08, 0x00, // RDP Negotiation Request (Type 0x01, Flags 0x00, Length 0x0008)
                        0x03, 0x00, 0x00, 0x00  // Requested Protocols: TLS (0x01) | NLA (0x02) = 0x03
                    };

                        stream.Write(rdpNegotiationRequest, 0, rdpNegotiationRequest.Length);
                        stream.ReadTimeout = timeout;

                        try
                        {
                            byte[] response = new byte[19]; // Read enough for the negotiation response header
                            int totalBytesRead = 0;
                            int bytesToRead = response.Length;

                            while (totalBytesRead < response.Length)
                            {
                                int bytesRead = stream.Read(response, totalBytesRead, bytesToRead);
                                if (bytesRead == 0)
                                {
                                    // Connection closed
                                    break;
                                }
                                totalBytesRead += bytesRead;
                                bytesToRead -= bytesRead;
                            }

                            if (totalBytesRead >= 19) // Ensure we have enough data for header analysis
                            {
                                // Check TPKT Header
                                if (response[0] == 0x03 && response[1] == 0x00)
                                {
                                    // Check TPDU Type (Data TPDU or others that might carry negotiation)
                                    // More robustly check for RDP Negotiation Response or Failure

                                    // Look for the RDP Negotiation structure within the TPDU
                                    // Typically starts after TPKT (4 bytes) + TPDU header (variable, but at least 3 bytes: LI, TPDU Code, EOT/DT)
                                    // A common offset is 7 bytes in (TPKT=4 + TPDU LI + TPDU Code + DST-REF high)
                                    for (int i = 5; i <= totalBytesRead - 7; i++) // Ensure room for Type (1) + Flags (1) + Length (2) + Result/Code (4)
                                    {
                                        if (response[i] == 0x02 && response[i + 1] == 0x00 && response[i + 2] == 0x08 && response[i + 3] == 0x00)
                                        {
                                            // Found RDP Negotiation Response (Type 0x02, Length 0x0008)
                                            // Extract security protocol flags (little-endian, 4 bytes starting at i+4)
                                            if (totalBytesRead >= i + 8)
                                            {
                                                uint securityProtocol = BitConverter.ToUInt32(response, i + 4);
                                                // Check for NLA support flag (HYBRID_SUPPORTED = 0x02)
                                                if ((securityProtocol & 0x02) != 0)
                                                {
                                                    return RdpConnectionType.NlaSupported;
                                                }
                                                else if ((securityProtocol & 0x01) != 0) // Check for TLS support (SSL_SUPPORTED = 0x01)
                                                {
                                                    // TLS supported, but not NLA. Could be Standard RDP Security or TLS without enforced NLA at connect time.
                                                    return RdpConnectionType.StandardOrTls;
                                                }
                                                else
                                                {
                                                    // Other protocol or none specified in response
                                                    return RdpConnectionType.StandardOrTls; // Defaulting to non-NLA for simplicity
                                                }
                                            }
                                        }
                                        else if (response[i] == 0x03 && response[i + 1] == 0x00 && response[i + 2] == 0x08 && response[i + 3] == 0x00)
                                        {
                                            // Found RDP Negotiation Failure (Type 0x03, Length 0x0008)
                                            // This usually means negotiation failed, which can happen if NLA is required but not properly negotiated
                                            // or if the server doesn't like the client's request. It's a strong indicator the port is RDP,
                                            // but the simple negotiation failed. Often implies NLA requirement or misconfiguration.
                                            // For a basic check, we can consider this as potentially NLA-related failure.
                                            // A more precise check would parse the failure code.
                                            return RdpConnectionType.NlaSupported; // Interpreted as likely NLA-related failure
                                        }
                                    }

                                    // If we got a TPKT but no clear negotiation packet, it might be a direct MCS connect
                                    // which often indicates NLA is off or legacy mode.
                                    // The original check for 0x03, 0x00 is a basic RDP signature.
                                    return RdpConnectionType.StandardOrTls; // Fallback if negotiation structures not clearly found
                                }
                            }

                            // If the initial read didn't yield enough data or didn't match TPKT,
                            // check if any data was read that might resemble an RDP signature.
                            if (totalBytesRead > 0)
                            {
                                if (response[0] == 0x03 && response[1] == 0x00)
                                {
                                    // Basic RDP signature found, assume standard for now if negotiation wasn't clear
                                    return RdpConnectionType.StandardOrTls;
                                }
                                else if (response[0] == 0x16 && response[5] == 0x01)
                                {
                                    // TLS Alert or handshake record, might indicate TLS is attempted
                                    // but failed, or server is expecting TLS handshake start.
                                    // This is ambiguous but indicates the port is likely RDP-related.
                                    return RdpConnectionType.StandardOrTls; // Could be TLS attempt
                                }
                            }

                        }
                        catch (IOException) when (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0)
                        {
                            // Connection closed gracefully after write, likely not RDP or negotiation failed silently
                            // It was likely RDP that closed the connection after the negotiation request.
                            // This could be an indicator of NLA being required but negotiation failing.
                            // However, it's ambiguous. Let's be cautious.
                            // return RdpConnectionType.NlaSupported; // Might indicate NLA requirement causing immediate close
                            // Better to return NotRdp if we can't confirm the protocol type clearly.
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Any exception during connection or stream operations means it's likely not an RDP service
                // or is inaccessible.
            }

            return RdpConnectionType.NotRdp;
        }



        public enum RdpConnectionType
        {
            NotRdp,
            StandardOrTls, // Could be Standard RDP Security or TLS, often implies non-NLA at this simple check stage
            NlaSupported   // NLA (Network Level Authentication) is supported/required
        }



        private void CheckAcc(string ip)
        {
            try
            {
                int count = 0;

            StatusStrip:

                RdpConnectionType rdpResult;

                if (ip.Contains(":"))
                {
                   var splited = ip.Split(':');
                   int port = int.Parse(splited[1]);
                   rdpResult = CheckIfPortIsRdp(splited[0], port, milliseconds);
                }
                else
                {
                    rdpResult = CheckIfPortIsRdp(ip, 3389, milliseconds);
                }

              
                if (rdpResult == RdpConnectionType.StandardOrTls)
                {

                    //_nla

                    _nla++;
                    Invoke((MethodInvoker)delegate { lbl_nla.Text = "• NLA : " + _nla.ToString(); });
                    int check_shode = _error + _non_nla + _nla;
                    _checkedIps = check_shode;
                    bunifuProgressBar1.Value = check_shode;
                    double ch_darsad = check_shode;
                    Invoke((MethodInvoker)delegate
                    {
                        
                        c_c.Text = "• Checked : " + check_shode.ToString();
                        double min_darsad = int.Parse(lbl_ccc.Text.ToString());
                        var remaning = int.Parse(lbl_ccc.Text.ToString()) - check_shode;
                        double darsad = (ch_darsad / min_darsad) * 100;
                        darsad = Math.Round((Double)darsad, 2);
                        lbl_remain.Text = "• Remain : " + remaning.ToString();

                    });
                    lock (_lockObject)
                    {

                        using (var sw = File.AppendText(_trackingHandler + @"\NLA.txt"))
                        {
                            sw.WriteLine(ip);
                            sw.Close();
                        }
                      

                    }
                }
                else if (rdpResult == RdpConnectionType.NlaSupported)
                {
                    //_none_nla

                    _non_nla++;
                    Invoke((MethodInvoker)delegate { lbl_none_nla.Text = "• No_NLA : " + _non_nla.ToString(); });
                    int check_shode = _error + _non_nla + _nla;
                    bunifuProgressBar1.Value = check_shode;
                    double ch_darsad = check_shode;
                    Invoke((MethodInvoker)delegate
                    {
                        
                        c_c.Text = "• Checked : " + check_shode.ToString();
                        double min_darsad = int.Parse(lbl_ccc.Text.ToString());
                        var remaning = int.Parse(lbl_ccc.Text.ToString()) - check_shode;
                        double darsad = (ch_darsad / min_darsad) * 100;
                        darsad = Math.Round((Double)darsad, 2);
                        lbl_remain.Text = "• Remain : " + remaning.ToString();

                    });
                    lock (_lockObject)
                    {
                        using (var sw = File.AppendText(_trackingHandler + @"\No_NLA.txt"))
                        {
                            sw.WriteLine(ip);
                            sw.Close();
                        }


                    }
                }
                else
                {
                    count++;
                    if (count >= int_tries)
                    {
                        //_error
                        _error++;
                        Invoke((MethodInvoker)delegate { lbl_error.Text = "• Error :" + _error.ToString(); });
                        int check_shode = _error + _non_nla + _nla;
                        bunifuProgressBar1.Value=check_shode;
                        double ch_darsad = check_shode;
                        Invoke((MethodInvoker)delegate
                        {
                            c_c.Text = "• Checked : " + check_shode.ToString();
                            double min_darsad = int.Parse(lbl_ccc.Text.ToString());
                            var remaning = int.Parse(lbl_ccc.Text.ToString()) - check_shode;
                            double darsad = (ch_darsad / min_darsad) * 100;
                            darsad = Math.Round((Double)darsad, 2);
                            lbl_remain.Text = "• Remain : " + remaning.ToString();

                        });
                        lock (_lockObject)
                        {
                            using (var sw = File.AppendText(_trackingHandler + @"\Error.txt"))
                            {
                                sw.WriteLine(ip);
                                sw.Close();
                            }

                        }
                    }
                    else
                    {
                        goto StatusStrip;
                    }
                  

                }


            }catch { 


            
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {  
        }

       

        public void stoper()
        {
            try
            {
                while (true)
                {
                    _work = false;
                }
            }
            catch { }
        }

        public static string GetApplicationRoot()
        {
            var exePath = new Uri(System.Reflection.
            Assembly.GetExecutingAssembly().CodeBase).LocalPath;
            return new FileInfo(exePath).DirectoryName;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            cpm_bar.Maximum = 20000;
            cpm_bar.Minimum = 0;
        }


        
        private void button5_Click_1(object sender, EventArgs e)
        {

        }


        public void Save_Auto()
        {

           while(_work)
            {
                Thread.Sleep(20000);
                try
                {
                    lock (_lockGet)
                    {
                        _combo_0 = _combo;
                    }
                    File.WriteAllLines(_trackingHandler + @"\Error.txt", _combo_0);
                   
                    _combo_0.Clear();
                 
                }
                catch
                {

                }
                
            }

        }

        public void Save_Unchecked()
        {
            MessageBox.Show("Click <OK> For Save Your Unchecked Combo.");

            try
            {

                foreach (string s in _combo)
                {
                    _combo_0.Add(s);
                }
                 
                foreach (string s in _combo_0)
                {
                    lock (_lockObject)
                    {
                        using (var sw = File.AppendText(_trackingHandler + @"\Not_Checked.txt"))
                        {
                            sw.WriteLine(s);
                            sw.Close();

                        }


                    }
                }
                _combo_0.Clear();
                MessageBox.Show("Save Progress Finished ! You Can see Not Check in Result Folder !");
            }
            catch
            {

            }

        }
        private void button6_Click(object sender, EventArgs e)
        {
            if (!_abort)
            {
                MessageBox.Show("You Need Stop The Software First ! ");
            }
            else
            {
                var oThread2 = new Thread(Save_Unchecked) { IsBackground = true };
                oThread2.Start();
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {

        }

        private void button8_Click_1(object sender, EventArgs e)
        {
            //try
            //{
            //    int CPM = 200 / int.Parse(_stopwatch.Elapsed.ToString("mm"));
            //    cpm_bar.Value = CPM;
            //}
            //catch { }
           
        }

        private void button1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void button1_DragDrop(object sender, DragEventArgs e)
        {
            //    string[] filePaths = (string[])e.Data.GetData(DataFormats.FileDrop, false);
            //    if (Path.GetExtension(filePaths[0]).ToLower() == ".txt")
            //    {
            //        _combo.Clear();
            //        _combo.AddRange(File.ReadAllLines(filePaths[0]));
            //        lbl_combo.Text = "• Combo Loaded : " + string.Concat(_combo.Count);
            //        lbl_ccc.Text = string.Concat(_combo.Count);
            //        MessageBox.Show(string.Concat(_combo.Count) + " Combo Loaded ! ");
            //    }
        }

        private void button2_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        public void testmethods(string login)
        {
        }
       
        private void button8_Click_2(object sender, EventArgs e)
        {
     

           
        }

        private void BTN_LOAD_IP_Click(object sender, EventArgs e)
        {
            _combo.Clear();
            var dialog = new OpenFileDialog
            {
                Filter = @"Text File (*.txt)|*.txt",
                Title = @"Load IP List"
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            _combo.AddRange(File.ReadAllLines(dialog.FileName));
            lbl_combo.Text = "• IP Loaded : " + _combo.Count.ToString();
            lbl_ccc.Text = _combo.Count.ToString();
         
            bunifuProgressBar1.MaximumValue = _combo.Count;
        }
         int _checkedIps;
        public void StartStatsMonitor()
        {

          
            int lastCheckedIps = 0;
            DateTime lastTime = DateTime.Now;

            while (_work)
            {
                Thread.Sleep(2000); 

                int currentCheckedIps = _checkedIps;
                int ipsCheckedInLastMinute = currentCheckedIps - lastCheckedIps;


              


                int cpms = Int32.Parse(ipsCheckedInLastMinute.ToString());
                if (cpms > 0)
                {
                    double elapsedMinutes = (DateTime.Now - lastTime).TotalMinutes;
           
                    double ipsPerMinute = elapsedMinutes > 0 ? ipsCheckedInLastMinute / elapsedMinutes : 0;
                    cpm_bar.Maximum = cpms * 2;
                    cpm_bar.Value = cpms;
                    lastCheckedIps = currentCheckedIps;
                    lastTime = DateTime.Now;
                }

             
               
                
            }
        }

        private void BTN_START_Click(object sender, EventArgs e)
        {
            string timeout = numericUpDown2.Text;
            if (int.TryParse(timeout, out int seconds))
            {
                milliseconds = seconds * 1000;

            }

            string try_str = numericUpDown3.Text;
            if (int.TryParse(try_str, out int tries))
            {
                int_tries = tries;

            }

            _abort = false;
            _work = true;
            var oThread22 = new Thread(StartStatsMonitor) { IsBackground = true };
            oThread22.Start();


            var oThread2 = new Thread(dooo) { IsBackground = true };
            oThread2.Start();

            var oThread3 = new Thread(check_length) { IsBackground = true };
            oThread3.Start();
        }

        private void BTN_STOP_Click(object sender, EventArgs e)
        {
            _work = false;
            _abort = true;
            _stopwatch.Stop();
        }

        private void BTN_SAVE_UN_Click(object sender, EventArgs e)
        {
            if (!_abort)
            {
                MessageBox.Show("You Need Stop The Software First ! ");
            }
            else
            {
                var oThread2 = new Thread(Save_Unchecked) { IsBackground = true };
                oThread2.Start();
            }
        }

        private void BTN_RESET_APP_Click(object sender, EventArgs e)
        {
            try
            {
                Application.Restart();

            }
            catch
            {
                try
                {
                    Thread.Sleep(1000);
                    Application.Restart();
                }
                catch
                {
                    try
                    {
                        Thread.Sleep(5000);
                        Application.Restart();
                    }
                    catch
                    {

                    }
                }
            }
        }

        private void bunifuFormControlBox1_HelpClicked(object sender, EventArgs e)
        {
            string telegramChannel = "https://t.me/KillerTM_rdp"; 
            Process.Start(telegramChannel);
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string telegramChannel = "https://t.me/KillerTM_rdp"; 
            Process.Start(telegramChannel);
        }
        private bool mouseIsDown = false;
        private Point firstPoint;
        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            firstPoint = e.Location;
            mouseIsDown = true;
        }

        private void Form1_MouseUp(object sender, MouseEventArgs e)
        {
            mouseIsDown = false;
        }

        private void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseIsDown)
            {
                int xDiff = firstPoint.X - e.Location.X;
                int yDiff = firstPoint.Y - e.Location.Y;

                int x = this.Location.X - xDiff;
                int y = this.Location.Y - yDiff;
                this.Location = new Point(x, y);
            }
        }

        private void bunifuLabel1_MouseDown(object sender, MouseEventArgs e)
        {
            firstPoint = e.Location;
            mouseIsDown = true;
        }

        private void bunifuLabel1_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseIsDown)
            {
                int xDiff = firstPoint.X - e.Location.X;
                int yDiff = firstPoint.Y - e.Location.Y;

                int x = this.Location.X - xDiff;
                int y = this.Location.Y - yDiff;
                this.Location = new Point(x, y);
            }
        }

        private void bunifuLabel1_MouseUp(object sender, MouseEventArgs e)
        {
            mouseIsDown = false;
        }

        private void bunifuPictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            mouseIsDown = false;
        }

        private void bunifuPictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseIsDown)
            {
                int xDiff = firstPoint.X - e.Location.X;
                int yDiff = firstPoint.Y - e.Location.Y;

                int x = this.Location.X - xDiff;
                int y = this.Location.Y - yDiff;
                this.Location = new Point(x, y);
            }
        }

        private void bunifuPictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            firstPoint = e.Location;
            mouseIsDown = true;

        }
    }
}
